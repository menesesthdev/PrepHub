using PrepHub.Domain.Common;

namespace PrepHub.Domain.Entidades;

/// <summary>
/// Resposta do candidato a uma questão dentro de uma tentativa. Guarda a seleção,
/// se está marcada para revisão e o tempo gasto — estado necessário para reproduzir
/// fielmente a experiência de prova (navegação, "marcar para revisão", telemetria de tempo).
/// </summary>
public class RespostaDaTentativa : Entity
{
    private readonly List<Guid> _selectedOptionIds = new();

    // Construtor exigido pelo EF Core.
    private RespostaDaTentativa()
    {
    }

    public RespostaDaTentativa(Guid examAttemptId, Guid questionId, Guid? id = null)
        : base(id ?? Guid.NewGuid())
    {
        ExamAttemptId = examAttemptId;
        QuestionId = questionId;
    }

    public Guid ExamAttemptId { get; private set; }

    public Guid QuestionId { get; private set; }

    public bool IsFlaggedForReview { get; private set; }

    public int TimeSpentSeconds { get; private set; }

    /// <summary>Ids das alternativas selecionadas. Vazio = questão ainda não respondida.</summary>
    public IReadOnlyCollection<Guid> SelectedOptionIds => _selectedOptionIds;

    public bool IsAnswered => _selectedOptionIds.Count > 0;

    public void DefinirSelecao(IEnumerable<Guid> optionIds)
    {
        _selectedOptionIds.Clear();
        _selectedOptionIds.AddRange(optionIds.Distinct());
    }

    public void DefinirMarcacao(bool flagged) => IsFlaggedForReview = flagged;

    public void AdicionarTempoGasto(int seconds)
    {
        if (seconds > 0)
        {
            TimeSpentSeconds += seconds;
        }
    }
}
