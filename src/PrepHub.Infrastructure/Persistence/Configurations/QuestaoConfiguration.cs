using PrepHub.Domain.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PrepHub.Infrastructure.Persistence.Configurations;

public sealed class QuestaoConfiguration : IEntityTypeConfiguration<Questao>
{
    public void Configure(EntityTypeBuilder<Questao> builder)
    {
        builder.ToTable("Questions");
        builder.HasKey(q => q.Id);

        builder.Property(q => q.ExamId).IsRequired();
        builder.Property(q => q.SkillAreaId).IsRequired();
        builder.Property(q => q.ExternalId).IsRequired().HasMaxLength(120);
        builder.Property(q => q.Text).IsRequired().HasMaxLength(2000);
        builder.Property(q => q.Explanation).IsRequired().HasMaxLength(4000);
        builder.Property(q => q.Topic).HasMaxLength(200);
        builder.Property(q => q.Type).IsRequired().HasConversion<int>();
        builder.Property(q => q.IsActive).IsRequired().HasDefaultValue(true);

        // Índice único da chave de seed: é o que impede que reimportar um lote duplique o banco.
        builder.HasIndex(q => q.ExternalId).IsUnique();

        // O sorteio filtra o pool por exame e por IsActive e agrupa por domínio; este índice cobre
        // exatamente essa leitura, que roda a cada início de simulado.
        builder.HasIndex(q => new { q.ExamId, q.IsActive, q.SkillAreaId });

        builder.HasMany(q => q.Options)
            .WithOne()
            .HasForeignKey(o => o.QuestionId)
            .OnDelete(DeleteBehavior.Cascade);

        // A propriedade Options devolve uma lista ordenada nova; o EF acessa o campo _options.
        builder.Navigation(q => q.Options).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
