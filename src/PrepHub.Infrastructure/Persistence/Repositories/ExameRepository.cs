using PrepHub.Application.Abstractions;
using PrepHub.Domain.Entidades;
using PrepHub.Domain.Sorteio;
using Microsoft.EntityFrameworkCore;

namespace PrepHub.Infrastructure.Persistence.Repositories;

public sealed class ExameRepository : IExameRepository
{
    private readonly PrepHubDbContext _db;

    public ExameRepository(PrepHubDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Exame>> ObterTodosAsync(CancellationToken cancellationToken = default)
        => await _db.Exams.AsNoTracking().ToListAsync(cancellationToken);

    public async Task<Exame?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await _db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<Exame?> ObterComAreasAsync(Guid id, CancellationToken cancellationToken = default)
        => await _db.Exams
            .AsNoTracking()
            .Include(e => e.SkillAreas)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<Exame?> ObterComConteudoAsync(Guid id, CancellationToken cancellationToken = default)
        => await _db.Exams
            .AsNoTracking()
            .Include(e => e.SkillAreas)
            .Include(e => e.Questions)
                .ThenInclude(q => q.Options)
            .AsSplitQuery()
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<IReadOnlyList<QuestaoSorteavel>> ObterPoolAsync(
        Guid examId,
        CancellationToken cancellationToken = default)
        // IsActive filtra aqui e SÓ aqui: uma questão aposentada não entra em prova NOVA, mas
        // continua sendo carregada por ObterQuestoesAsync — senão a prova de quem já a recebeu
        // encolheria e a revisão de estudo dela ficaria em branco.
        => await _db.Questions
            .AsNoTracking()
            .Where(q => q.ExamId == examId && q.IsActive)
            .Select(q => new QuestaoSorteavel(q.Id, q.SkillAreaId, q.Topic))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Questao>> ObterQuestoesAsync(
        IReadOnlyCollection<Guid> questionIds,
        CancellationToken cancellationToken = default)
    {
        if (questionIds.Count == 0)
        {
            return Array.Empty<Questao>();
        }

        return await _db.Questions
            .AsNoTracking()
            .Include(q => q.Options)
            .Where(q => questionIds.Contains(q.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> ObterOpcoesCorretasAsync(
        IReadOnlyCollection<Guid> questionIds,
        CancellationToken cancellationToken = default)
    {
        if (questionIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<Guid>>();
        }

        // Só as alternativas corretas viajam do banco: o sorteio compara conjuntos de ids e nunca
        // precisa do texto de nada.
        var corretas = await _db.AnswerOptions
            .AsNoTracking()
            .Where(o => o.IsCorrect && questionIds.Contains(o.QuestionId))
            .Select(o => new { o.QuestionId, o.Id })
            .ToListAsync(cancellationToken);

        return corretas
            .GroupBy(o => o.QuestionId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<Guid>)g.Select(o => o.Id).ToList());
    }
}
