namespace PrepHub.Domain.Correcao;

/// <summary>
/// Placar consolidado de uma tentativa: percentual geral, nota na escala 1–1000,
/// aprovação e breakdown por skill area.
/// </summary>
public sealed record PlacarDaProva(
    int TotalQuestions,
    int CorrectAnswers,
    decimal ScorePercent,
    bool Passed,
    IReadOnlyList<PlacarPorArea> SkillAreas,
    int ScaledScore);
