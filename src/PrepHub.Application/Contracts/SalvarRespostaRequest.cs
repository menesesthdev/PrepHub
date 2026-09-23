namespace PrepHub.Application.Contracts;

/// <summary>Comando para gravar/atualizar a resposta de uma questão durante a prova.</summary>
public sealed record SalvarRespostaRequest(
    Guid AttemptId,
    Guid QuestionId,
    IReadOnlyList<Guid> SelectedOptionIds,
    bool IsFlaggedForReview,
    int TimeSpentSeconds);
