using PrepHub.Domain.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PrepHub.Infrastructure.Persistence.Configurations;

public sealed class QuestaoDaTentativaConfiguration : IEntityTypeConfiguration<QuestaoDaTentativa>
{
    public void Configure(EntityTypeBuilder<QuestaoDaTentativa> builder)
    {
        builder.ToTable("ExamAttemptQuestions");
        builder.HasKey(q => q.Id);

        builder.Property(q => q.ExamAttemptId).IsRequired();
        builder.Property(q => q.QuestionId).IsRequired();
        builder.Property(q => q.OrderIndex).IsRequired();

        // Uma questão não cai duas vezes na mesma prova, e cada posição é ocupada uma só vez.
        builder.HasIndex(q => new { q.ExamAttemptId, q.QuestionId }).IsUnique();
        builder.HasIndex(q => new { q.ExamAttemptId, q.OrderIndex }).IsUnique();

        // Restrict, e não Cascade: apagar uma questão do banco com histórico apontando para ela
        // apagaria em silêncio a composição de provas já feitas. Questão obsoleta sai do sorteio
        // por não estar mais nos arquivos de seed, não por ser removida do banco.
        builder.HasOne<Questao>()
            .WithMany()
            .HasForeignKey(q => q.QuestionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
