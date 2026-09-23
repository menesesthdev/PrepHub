using PrepHub.Application.Abstractions;
using PrepHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace PrepHub.Infrastructure.Observabilidade;

/// <summary>
/// Traduz o banco em <see cref="RetratoDoBanco"/>. Separado do serviço que o agenda para poder
/// ser testado contra um SQLite de verdade — é onde mora o risco real, já que uma consulta que o
/// provider não traduz só falha em tempo de execução.
/// </summary>
public sealed class LeitorDoRetratoDoBanco
{
    /// <summary>
    /// Janelas de "usuário ativo". Os rótulos vão para o Prometheus como valor de dimensão, então
    /// são curtos e estáveis — mudá-los quebra a continuidade da série.
    /// </summary>
    private static readonly (string Rotulo, TimeSpan Janela)[] Janelas =
    [
        ("24h", TimeSpan.FromHours(24)),
        ("7d", TimeSpan.FromDays(7)),
        ("30d", TimeSpan.FromDays(30))
    ];

    private readonly PrepHubDbContext _db;
    private readonly IClock _clock;

    public LeitorDoRetratoDoBanco(PrepHubDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<RetratoDoBanco> LerAsync(CancellationToken cancellationToken = default)
    {
        var agora = _clock.UtcNow;

        // Projeções anônimas e o mapeamento para os records DEPOIS, em memória: o EF traduz
        // consulta agrupada sem problema, mas construir um record posicional dentro do Select é
        // exatamente o tipo de expressão que muda de comportamento entre versões do provider.
        var porProvedor = await _db.Users
            .AsNoTracking()
            .GroupBy(u => u.Provider)
            .Select(g => new { Provedor = g.Key, Total = g.Count() })
            .ToListAsync(cancellationToken);

        var ativos = new List<ContagemPorJanela>(Janelas.Length);
        foreach (var (rotulo, janela) in Janelas)
        {
            var desde = agora - janela;
            var total = await _db.Users
                .AsNoTracking()
                .CountAsync(u => u.LastLoginAt >= desde, cancellationToken);

            ativos.Add(new ContagemPorJanela(rotulo, total));
        }

        var emAndamento = await _db.ExamAttempts
            .AsNoTracking()
            .CountAsync(a => a.FinishedAt == null, cancellationToken);

        var realizadas = await _db.ExamAttempts
            .AsNoTracking()
            .Where(a => a.FinishedAt != null)
            .Join(
                _db.Exams.AsNoTracking(),
                a => a.ExamId,
                e => e.Id,
                (a, e) => new { e.Code, a.Passed })
            .GroupBy(x => new { x.Code, x.Passed })
            .Select(g => new { g.Key.Code, g.Key.Passed, Total = g.Count() })
            .ToListAsync(cancellationToken);

        return new RetratoDoBanco(
            agora,
            porProvedor.Select(p => new ContagemPorProvedor(p.Provedor, p.Total)).ToList(),
            ativos,
            emAndamento,
            realizadas
                .Select(r => new ContagemDeProvasRealizadas(r.Code, r.Passed == true, r.Total))
                .ToList());
    }
}
