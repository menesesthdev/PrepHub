using PrepHub.Domain.Enums;

namespace PrepHub.Application.Contracts;

/// <summary>
/// Status de uma questão na tela de revisão. <paramref name="SelectedCount"/> e
/// <paramref name="RequiredSelections"/> permitem distinguir "Completo" de "Incompleto" —
/// a prova real marca como incompleta a questão de múltipla resposta parcialmente respondida.
/// </summary>
public sealed record StatusDaQuestaoDto(
    Guid QuestionId,
    int Number,
    bool IsAnswered,
    bool IsFlaggedForReview,
    int SelectedCount,
    int RequiredSelections);

/// <summary>
/// Estado geral de uma tentativa em andamento: dados do exame, tempo restante e o status
/// de cada questão — tudo que o header fixo e a tela de revisão precisam para renderizar.
/// </summary>
public sealed record EstadoDaTentativaDto(
    Guid AttemptId,
    string ExamCode,
    string ExamName,
    int TimeLimitMinutes,
    DateTime StartedAt,
    int RemainingSeconds,
    bool IsFinished,
    IReadOnlyList<StatusDaQuestaoDto> Questions,
    FornecedorDoExame Vendor = FornecedorDoExame.Microsoft);
