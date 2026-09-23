using PrepHub.Domain.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PrepHub.Infrastructure.Persistence.Configurations;

public sealed class AreaDeHabilidadeConfiguration : IEntityTypeConfiguration<AreaDeHabilidade>
{
    public void Configure(EntityTypeBuilder<AreaDeHabilidade> builder)
    {
        builder.ToTable("SkillAreas");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.ExamId).IsRequired();
        builder.Property(s => s.Key).IsRequired().HasMaxLength(60);
        builder.Property(s => s.Name).IsRequired().HasMaxLength(200);
        builder.Property(s => s.WeightPercent).HasPrecision(5, 2);

        // O slug identifica a área dentro do exame — é por ele que os arquivos de seed a acham.
        builder.HasIndex(s => new { s.ExamId, s.Key }).IsUnique();

        builder.HasMany(s => s.Questions)
            .WithOne()
            .HasForeignKey(q => q.SkillAreaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(s => s.Questions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
