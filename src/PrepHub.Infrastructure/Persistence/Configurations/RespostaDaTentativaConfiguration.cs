using PrepHub.Domain.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PrepHub.Infrastructure.Persistence.Configurations;

public sealed class RespostaDaTentativaConfiguration : IEntityTypeConfiguration<RespostaDaTentativa>
{
    public void Configure(EntityTypeBuilder<RespostaDaTentativa> builder)
    {
        builder.ToTable("ExamAttemptAnswers");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.ExamAttemptId).IsRequired();
        builder.Property(a => a.QuestionId).IsRequired();
        builder.Property(a => a.IsFlaggedForReview).IsRequired();
        builder.Property(a => a.TimeSpentSeconds).IsRequired();

        // Uma resposta por questão dentro da mesma tentativa.
        builder.HasIndex(a => new { a.ExamAttemptId, a.QuestionId }).IsUnique();

        // SelectedOptionIds é persistido como TEXT (Guids separados por vírgula) — portável
        // entre SQLite e PostgreSQL, sem depender de tipos de array de um provider específico.
        var converter = new ValueConverter<IReadOnlyCollection<Guid>, string>(
            v => string.Join(',', v.Select(id => id.ToString())),
            v => Deserialize(v));

        var comparer = new ValueComparer<IReadOnlyCollection<Guid>>(
            (a, b) => (a ?? new List<Guid>()).SequenceEqual(b ?? new List<Guid>()),
            v => v.Aggregate(0, (hash, id) => HashCode.Combine(hash, id.GetHashCode())),
            v => v.ToList());

        builder.Property(a => a.SelectedOptionIds)
            .HasColumnName("SelectedOptionIds")
            .HasColumnType("TEXT")
            .HasConversion(converter, comparer);

        builder.Metadata
            .FindProperty(nameof(RespostaDaTentativa.SelectedOptionIds))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }

    private static IReadOnlyCollection<Guid> Deserialize(string value)
        => string.IsNullOrWhiteSpace(value)
            ? new List<Guid>()
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Guid.Parse)
                .ToList();
}
