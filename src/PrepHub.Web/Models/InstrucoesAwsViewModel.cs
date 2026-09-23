namespace PrepHub.Web.Models;

/// <summary>
/// O que a tela de instruções da AWS precisa mostrar — antes da tentativa existir (e por isso sem
/// <c>AttemptId</c>) e, durante a revisão, no modal "Instruções".
/// </summary>
public sealed record InstrucoesAwsViewModel(
    Guid ExamId,
    string ExamCode,
    string ExamName,
    int TotalQuestions,
    int TimeLimitMinutes);
