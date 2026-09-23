using PrepHub.Domain.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PrepHub.Infrastructure.Persistence.Configurations;

public sealed class OpcaoDeRespostaConfiguration : IEntityTypeConfiguration<OpcaoDeResposta>
{
    public void Configure(EntityTypeBuilder<OpcaoDeResposta> builder)
    {
        builder.ToTable("AnswerOptions");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.QuestionId).IsRequired();
        builder.Property(o => o.Text).IsRequired().HasMaxLength(1000);

        // Nulo em todo tipo que não seja arrastar-e-soltar — é o alvo do par candidato, não um
        // campo que toda alternativa tem. Ver OpcaoDeResposta.TargetText.
        builder.Property(o => o.TargetText).HasMaxLength(1000);

        builder.Property(o => o.IsCorrect).IsRequired();
        builder.Property(o => o.OrderIndex).IsRequired();
    }
}
