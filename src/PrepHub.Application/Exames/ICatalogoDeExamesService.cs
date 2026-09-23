using PrepHub.Application.Contracts;

namespace PrepHub.Application.Exames;

/// <summary>Consulta os exames disponíveis para a tela inicial.</summary>
public interface ICatalogoDeExamesService
{
    Task<IReadOnlyList<ResumoDeExameDto>> ObterExamesDisponiveisAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Um exame que pode ser iniciado agora, ou <c>null</c> se não existe ou está em construção.
    /// </summary>
    Task<ResumoDeExameDto?> ObterExameDisponivelAsync(Guid examId, CancellationToken cancellationToken = default);
}
