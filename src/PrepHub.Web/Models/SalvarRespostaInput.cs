namespace PrepHub.Web.Models;

/// <summary>Payload JSON enviado pelo front-end ao gravar a resposta de uma questão.</summary>
public sealed class SalvarRespostaInput
{
    public Guid QuestionId { get; set; }

    public List<Guid> SelectedOptionIds { get; set; } = new();

    public bool IsFlaggedForReview { get; set; }

    public int TimeSpentSeconds { get; set; }
}
