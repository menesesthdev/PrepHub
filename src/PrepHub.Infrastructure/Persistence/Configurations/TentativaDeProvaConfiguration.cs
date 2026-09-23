using PrepHub.Domain.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PrepHub.Infrastructure.Persistence.Configurations;

public sealed class TentativaDeProvaConfiguration : IEntityTypeConfiguration<TentativaDeProva>
{
    public void Configure(EntityTypeBuilder<TentativaDeProva> builder)
    {
        builder.ToTable("ExamAttempts");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.ExamId).IsRequired();
        builder.Property(a => a.UserId).IsRequired();
        builder.Property(a => a.StartedAt).IsRequired();
        builder.Property(a => a.FinishedAt);
        builder.Property(a => a.ScorePercent).HasPrecision(5, 2);
        builder.Property(a => a.Passed);

        builder.HasOne<Exame>()
            .WithMany()
            .HasForeignKey(a => a.ExamId)
            .OnDelete(DeleteBehavior.Restrict);

        // Apagar o usuário leva junto o histórico de tentativas dele.
        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Histórico do candidato: "minhas tentativas, da mais recente para a mais antiga".
        builder.HasIndex(a => new { a.UserId, a.StartedAt });

        builder.HasMany(a => a.Answers)
            .WithOne()
            .HasForeignKey(ans => ans.ExamAttemptId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(a => a.Answers).UsePropertyAccessMode(PropertyAccessMode.Field);

        // A composição sorteada pertence à tentativa e morre com ela.
        builder.HasMany(a => a.Questions)
            .WithOne()
            .HasForeignKey(q => q.ExamAttemptId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(a => a.Questions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
