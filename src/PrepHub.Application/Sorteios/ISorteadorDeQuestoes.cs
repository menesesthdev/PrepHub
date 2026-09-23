namespace PrepHub.Application.Sorteios;

/// <summary>
/// Escolhe quais questões compõem a próxima prova de um usuário.
/// </summary>
public interface ISorteadorDeQuestoes
{
    /// <summary>
    /// Monta a prova: respeita o peso de cada domínio e evita o que o usuário viu nas últimas
    /// tentativas, reservando uma cota pequena para reencontrar o que ele errou.
    /// </summary>
    /// <returns>Ids das questões na ordem de apresentação.</returns>
    Task<IReadOnlyList<Guid>> SortearAsync(
        Guid examId,
        Guid userId,
        CancellationToken cancellationToken = default);
}
