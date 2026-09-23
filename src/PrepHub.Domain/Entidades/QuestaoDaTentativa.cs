using PrepHub.Domain.Common;

namespace PrepHub.Domain.Entidades;

/// <summary>
/// Uma questão sorteada para uma tentativa, na posição em que será apresentada.
///
/// Existe porque a prova deixou de ser "todas as questões do exame": com centenas de questões no
/// banco e 40 por prova, a composição passa a ser um fato daquela tentativa e precisa ser
/// persistida. Sem isso, um simulado retomado depois — ou revisto no histórico — mostraria
/// questões diferentes das que a pessoa respondeu.
/// </summary>
public class QuestaoDaTentativa : Entity
{
    // Construtor exigido pelo EF Core.
    private QuestaoDaTentativa()
    {
    }

    public QuestaoDaTentativa(Guid examAttemptId, Guid questionId, int orderIndex, Guid? id = null)
        : base(id ?? Guid.NewGuid())
    {
        ExamAttemptId = examAttemptId;
        QuestionId = Guard.NotEmpty(questionId, nameof(questionId));
        OrderIndex = orderIndex;
    }

    public Guid ExamAttemptId { get; private set; }

    public Guid QuestionId { get; private set; }

    /// <summary>Posição na prova, base zero. É o "Item 12 de 40" que o candidato vê.</summary>
    public int OrderIndex { get; private set; }
}
