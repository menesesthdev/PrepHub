using PrepHub.Application.Abstractions;
using PrepHub.Application.Contracts;
using PrepHub.Domain.Sorteio;

namespace PrepHub.Application.Sorteios;

/// <summary>
/// Costura o que o <see cref="SorteioDeQuestoes"/> precisa: o pool do exame, os pesos dos
/// domínios e o histórico do usuário. A decisão em si continua no Domain — aqui só se busca
/// dado e se traduz "o que ele marcou" em "acertou ou não".
/// </summary>
public sealed class SorteadorDeQuestoesService : ISorteadorDeQuestoes
{
    private readonly IExameRepository _examRepository;
    private readonly ITentativaDeProvaRepository _attemptRepository;
    private readonly IGeradorDeAleatoriedade _aleatoriedade;
    private readonly PoliticaDeSorteio _politica;

    public SorteadorDeQuestoesService(
        IExameRepository examRepository,
        ITentativaDeProvaRepository attemptRepository,
        IGeradorDeAleatoriedade aleatoriedade,
        PoliticaDeSorteio? politica = null)
    {
        _examRepository = examRepository;
        _attemptRepository = attemptRepository;
        _aleatoriedade = aleatoriedade;
        _politica = politica ?? PoliticaDeSorteio.Padrao;
    }

    public async Task<IReadOnlyList<Guid>> SortearAsync(
        Guid examId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var exam = await _examRepository.ObterComAreasAsync(examId, cancellationToken)
                   ?? throw new InvalidOperationException($"Exame {examId} não encontrado.");

        var pool = await _examRepository.ObterPoolAsync(examId, cancellationToken);
        if (pool.Count == 0)
        {
            throw new InvalidOperationException(
                $"O exame {exam.Code} não tem questões cadastradas — não há como montar uma prova.");
        }

        var areas = exam.SkillAreas
            .Select(a => new AreaSorteavel(a.Id, a.WeightPercent))
            .ToList();

        var historico = await MontarHistoricoAsync(examId, userId, cancellationToken);

        return SorteioDeQuestoes.Sortear(
            pool,
            areas,
            exam.TotalQuestions,
            historico,
            _politica,
            _aleatoriedade.Criar());
    }

    /// <summary>
    /// Converte o histórico bruto em acerto/erro. Consulta apenas as alternativas corretas das
    /// questões efetivamente vistas — nunca o banco inteiro.
    /// </summary>
    private async Task<IReadOnlyList<HistoricoDeQuestao>> MontarHistoricoAsync(
        Guid examId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var vistas = await _attemptRepository.ObterQuestoesVistasAsync(
            userId,
            examId,
            _politica.TentativasDeMemoria,
            cancellationToken);

        if (vistas.Count == 0)
        {
            return Array.Empty<HistoricoDeQuestao>();
        }

        var ids = vistas.Select(v => v.QuestaoId).Distinct().ToList();
        var corretasPorQuestao = await _examRepository.ObterOpcoesCorretasAsync(ids, cancellationToken);

        return vistas
            .Select(v => new HistoricoDeQuestao(v.QuestaoId, v.TentativasAtras, Acertou(v, corretasPorQuestao)))
            .ToList();
    }

    /// <summary>
    /// Mesma regra da correção: acerta quem seleciona exatamente o conjunto correto.
    /// </summary>
    /// <returns>
    /// <c>null</c> quando não há resultado a apurar — item apresentado numa tentativa que a pessoa
    /// abandonou sem responder. Ele conta como visto (não volta logo em seguida), mas não como
    /// errado: numa prova fechada no item 1, tratar os 40 itens como erro despejaria questões
    /// nunca lidas na fila de reforço e empurraria para fora dela os erros de verdade.
    /// </returns>
    private static bool? Acertou(
        QuestaoVistaDto vista,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> corretasPorQuestao)
    {
        if (vista.SelectedOptionIds.Count == 0)
        {
            // Prova encerrada em branco é erro, exatamente como CorretorDeProva a conta.
            return vista.TentativaConcluida ? false : null;
        }

        if (!corretasPorQuestao.TryGetValue(vista.QuestaoId, out var corretas))
        {
            // Questão que saiu do banco: tratar como acerto a mantém fora da fila de reforço,
            // que é o desfecho inofensivo — ela nem existe mais para ser sorteada.
            return true;
        }

        return vista.SelectedOptionIds.ToHashSet().SetEquals(corretas);
    }
}
