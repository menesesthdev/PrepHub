using PrepHub.Application.Abstractions;
using PrepHub.Application.Contracts;
using PrepHub.Domain.Correcao;
using PrepHub.Domain.Entidades;

namespace PrepHub.Application.Historico;

public sealed class HistoricoDeProvasService : IHistoricoDeProvasService
{
    private readonly ITentativaDeProvaRepository _tentativas;
    private readonly IExameRepository _exames;
    private readonly IUsuarioAtual _usuarioAtual;

    public HistoricoDeProvasService(
        ITentativaDeProvaRepository tentativas,
        IExameRepository exames,
        IUsuarioAtual usuarioAtual)
    {
        _tentativas = tentativas;
        _exames = exames;
        _usuarioAtual = usuarioAtual;
    }

    public async Task<HistoricoDoUsuarioDto> ObterHistoricoAsync(CancellationToken cancellationToken = default)
    {
        var userId = _usuarioAtual.Id;
        if (userId is null)
        {
            // Sem sessão não há histórico. Devolver vazio em vez de lançar porque a política de
            // autorização já barra o acesso antes daqui — chegar aqui sem usuário significaria
            // uma tela nova sem [Authorize], e uma lista vazia é melhor que erro 500.
            return Vazio();
        }

        var tentativas = await _tentativas.ObterDoUsuarioAsync(userId.Value, cancellationToken);
        if (tentativas.Count == 0)
        {
            return Vazio();
        }

        // Os exames vêm de uma consulta só, indexados por Id: com uma leitura por tentativa
        // isto seria N+1, e o histórico cresce com o uso.
        var exames = (await _exames.ObterTodosAsync(cancellationToken)).ToDictionary(e => e.Id);

        var resumos = tentativas
            // Tentativa cujo exame saiu do catálogo não tem como ser exibida (faltam código,
            // nome e o percentual de corte que ancora a escala da nota).
            .Where(t => exames.ContainsKey(t.ExamId))
            .Select(t => Mapear(t, exames[t.ExamId]))
            .ToList();

        var concluidas = resumos.Where(r => r.Concluida).ToList();

        return new HistoricoDoUsuarioDto(
            resumos.Where(r => !r.Concluida).ToList(),
            concluidas,
            Consolidar(concluidas));
    }

    private static HistoricoDoUsuarioDto Vazio() => new([], [], null);

    private static ResumoDeTentativaDto Mapear(TentativaDeProva tentativa, Exame exame)
    {
        // Nota escalada só existe para tentativa concluída: sem correção não há percentual, e
        // converter um placar parcial daria uma nota que não significa nada.
        int? nota = tentativa is { IsFinished: true, ScorePercent: { } percent }
            ? EscalaDeNota.Converter(percent, exame.PassingScorePercent, EscalaDeNota.NotaMinimaPara(exame.Vendor))
            : null;

        return new ResumoDeTentativaDto(
            tentativa.Id,
            exame.Code,
            exame.Name,
            tentativa.StartedAt,
            tentativa.FinishedAt,
            tentativa.IsFinished,
            nota,
            tentativa.Passed,
            tentativa.Answers.Count(a => a.IsAnswered),
            exame.TotalQuestions);
    }

    /// <summary>
    /// Números consolidados das tentativas concluídas. A lista chega ordenada da mais recente
    /// para a mais antiga (ordem do repositório), o que define quem é "a atual" e "a anterior".
    /// </summary>
    private static DesempenhoConsolidadoDto? Consolidar(IReadOnlyList<ResumoDeTentativaDto> concluidas)
    {
        if (concluidas.Count == 0)
        {
            return null;
        }

        var notas = concluidas.Select(c => c.NotaEscalada ?? 0).ToList();

        return new DesempenhoConsolidadoDto(
            concluidas.Count,
            concluidas.Count(c => c.Aprovado == true),
            notas.Max(),
            notas[0],
            notas.Count > 1 ? notas[0] - notas[1] : null);
    }
}
