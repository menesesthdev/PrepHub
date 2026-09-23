using PrepHub.Domain.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PrepHub.Infrastructure.Persistence.Configurations;

public sealed class ExameConfiguration : IEntityTypeConfiguration<Exame>
{
    public void Configure(EntityTypeBuilder<Exame> builder)
    {
        builder.ToTable("Exams");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Code).IsRequired().HasMaxLength(20);
        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.Property(e => e.TimeLimitMinutes).IsRequired();
        builder.Property(e => e.PassingScorePercent).IsRequired();
        builder.Property(e => e.TotalQuestions).IsRequired();

        // Default true: a coluna nasce valendo "publicado", então a migration não tranca o AZ-900
        // que já está no ar. Mesmo raciocínio do backfill de EmailConfirmedAt — coluna nova cujo
        // valor padrão bloqueia acesso é uma indisponibilidade silenciosa em produção.
        builder.Property(e => e.IsPublished).IsRequired().HasDefaultValue(true);

        // Sem HasDefaultValue: Microsoft é o zero do enum, e a migration já cria a coluna com 0 —
        // os quatro exames Azure que existiam antes dela continuam sendo Microsoft sem backfill.
        builder.Property(e => e.Vendor).IsRequired();

        builder.HasIndex(e => e.Code).IsUnique();

        builder.HasMany(e => e.SkillAreas)
            .WithOne()
            .HasForeignKey(s => s.ExamId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Questions)
            .WithOne()
            .HasForeignKey(q => q.ExamId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(e => e.SkillAreas).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(e => e.Questions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
