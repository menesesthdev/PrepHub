using PrepHub.Application.Abstractions;
using PrepHub.Domain.Entidades;
using Microsoft.EntityFrameworkCore;

namespace PrepHub.Infrastructure.Persistence;

/// <summary>
/// Contexto EF Core da aplicação. Também cumpre o papel de <see cref="IUnitOfWork"/>
/// (o SaveChangesAsync do DbContext já satisfaz a interface), mantendo a Application
/// desacoplada do EF.
/// </summary>
public class PrepHubDbContext : DbContext, IUnitOfWork
{
    public PrepHubDbContext(DbContextOptions<PrepHubDbContext> options)
        : base(options)
    {
    }

    public DbSet<Exame> Exams => Set<Exame>();
    public DbSet<AreaDeHabilidade> SkillAreas => Set<AreaDeHabilidade>();
    public DbSet<Questao> Questions => Set<Questao>();
    public DbSet<OpcaoDeResposta> AnswerOptions => Set<OpcaoDeResposta>();
    public DbSet<TentativaDeProva> ExamAttempts => Set<TentativaDeProva>();
    public DbSet<RespostaDaTentativa> ExamAttemptAnswers => Set<RespostaDaTentativa>();
    public DbSet<QuestaoDaTentativa> ExamAttemptQuestions => Set<QuestaoDaTentativa>();
    public DbSet<Usuario> Users => Set<Usuario>();
    public DbSet<TokenDeRedefinicaoDeSenha> PasswordResetTokens => Set<TokenDeRedefinicaoDeSenha>();
    public DbSet<TokenDeConfirmacaoDeEmail> EmailConfirmationTokens => Set<TokenDeConfirmacaoDeEmail>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PrepHubDbContext).Assembly);
    }
}
