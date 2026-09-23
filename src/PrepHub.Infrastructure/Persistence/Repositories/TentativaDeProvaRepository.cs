using PrepHub.Application.Abstractions;
using PrepHub.Application.Contracts;
using PrepHub.Domain.Entidades;
using Microsoft.EntityFrameworkCore;

namespace PrepHub.Infrastructure.Persistence.Repositories;

public sealed class TentativaDeProvaRepository : ITentativaDeProvaRepository
{
    private readonly PrepHubDbContext _db;

    public TentativaDeProvaRepository(PrepHubDbContext db)
    {
        _db = db;
    }

    public async Task AdicionarAsync(TentativaDeProva attempt, CancellationToken cancellationToken = default)
        => await _db.ExamAttempts.AddAsync(attempt, cancellationToken);

    public async Task AdicionarRespostaAsync(RespostaDaTentativa answer, CancellationToken cancellationToken = default)
        => await _db.ExamAttemptAnswers.AddAsync(answer, cancellationToken);

    // Rastreado (sem AsNoTracking): SalvarResposta/Finalizar precisam alterar a tentativa carregada.
    public async Task<TentativaDeProva?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await _db.ExamAttempts
            .Include(a => a.Answers)
            .Include(a => a.Questions)
            .AsSplitQuery()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    // AsNoTracking: o histórico é só leitura, nada aqui vai ser alterado.
    //
    // Ordenação por StartedAt e NÃO por nota: o SQLite não tem decimal nativo, então ordenar
    // por ScorePercent seria impreciso (é a dívida registrada no CLAUDE.md). Data é DateTime,
    // que o provider ordena corretamente — e é a ordem que o histórico quer de todo jeito.
    public async Task<IReadOnlyList<TentativaDeProva>> ObterDoUsuarioAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
        => await _db.ExamAttempts
            .AsNoTracking()
            .Include(a => a.Answers)
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.StartedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<QuestaoVistaDto>> ObterQuestoesVistasAsync(
        Guid userId,
        Guid examId,
        int maxTentativas,
        CancellationToken cancellationToken = default)
    {
        if (maxTentativas <= 0)
        {
            return Array.Empty<QuestaoVistaDto>();
        }

        // Só os ids das N tentativas mais recentes: a distância em tentativas é a posição nesta
        // lista, e é ela que o sorteio usa como "recente". FinishedAt vem junto porque o sorteio
        // trata item em branco de prova encerrada (erro) diferente de item em branco de prova
        // abandonada (nunca lido).
        var maisRecentes = await _db.ExamAttempts
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.ExamId == examId)
            .OrderByDescending(a => a.StartedAt)
            .ThenByDescending(a => a.Id)
            .Take(maxTentativas)
            .Select(a => new { a.Id, a.FinishedAt })
            .ToListAsync(cancellationToken);

        if (maisRecentes.Count == 0)
        {
            return Array.Empty<QuestaoVistaDto>();
        }

        var recentes = maisRecentes.Select(a => a.Id).ToList();

        var distancia = recentes
            .Select((id, indice) => (id, distancia: indice + 1))
            .ToDictionary(x => x.id, x => x.distancia);

        var concluida = maisRecentes.ToDictionary(a => a.Id, a => a.FinishedAt is not null);

        var apresentadas = await _db.ExamAttemptQuestions
            .AsNoTracking()
            .Where(q => recentes.Contains(q.ExamAttemptId))
            .Select(q => new { q.ExamAttemptId, q.QuestionId })
            .ToListAsync(cancellationToken);

        var respondidas = await _db.ExamAttemptAnswers
            .AsNoTracking()
            .Where(r => recentes.Contains(r.ExamAttemptId))
            .Select(r => new { r.ExamAttemptId, r.QuestionId, r.SelectedOptionIds })
            .ToListAsync(cancellationToken);

        var selecoes = respondidas.ToDictionary(
            r => (r.ExamAttemptId, r.QuestionId),
            r => r.SelectedOptionIds);

        // A união cobre os dois lados: item apresentado e não respondido (que continua sendo
        // "visto"), e tentativa antiga sem composição gravada, de que só restam as respostas.
        var vistos = apresentadas
            .Select(a => (a.ExamAttemptId, a.QuestionId))
            .Concat(respondidas.Select(r => (r.ExamAttemptId, r.QuestionId)))
            .Distinct();

        return vistos
            .Select(v => new QuestaoVistaDto(
                v.QuestionId,
                distancia[v.ExamAttemptId],
                selecoes.TryGetValue(v, out var selecao) ? selecao : Array.Empty<Guid>(),
                concluida[v.ExamAttemptId]))
            .ToList();
    }
}
