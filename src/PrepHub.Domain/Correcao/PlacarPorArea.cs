namespace PrepHub.Domain.Correcao;

/// <summary>
/// Placar por domínio de habilidade — base do "score report" por skill area,
/// como na tela de resultado real da prova.
/// </summary>
public sealed record PlacarPorArea(
    Guid SkillAreaId,
    string SkillAreaName,
    decimal WeightPercent,
    int TotalQuestions,
    int CorrectAnswers)
{
    public decimal ScorePercent => TotalQuestions == 0
        ? 0m
        : Math.Round((decimal)CorrectAnswers / TotalQuestions * 100m, 1);
}
