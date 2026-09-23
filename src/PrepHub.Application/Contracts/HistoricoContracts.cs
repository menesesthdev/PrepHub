namespace PrepHub.Application.Contracts;

/// <summary>
/// Uma linha do histórico. Cobre tanto tentativa concluída (com nota e veredito) quanto
/// tentativa em andamento (com progresso), porque a tela lista as duas.
/// </summary>
/// <remarks>
/// A nota vem pronta na escala 1–1000: o percentual é regra de negócio interna e não aparece
/// na UI, então o DTO não o carrega — assim nenhuma view tem como exibi-lo por engano.
/// </remarks>
public sealed record ResumoDeTentativaDto(
    Guid Id,
    string ExamCode,
    string ExamName,
    DateTime StartedAt,
    DateTime? FinishedAt,
    bool Concluida,
    int? NotaEscalada,
    bool? Aprovado,
    int QuestoesRespondidas,
    int TotalDeQuestoes)
{
    /// <summary>Tempo entre início e fim; para tentativa aberta, ainda não faz sentido.</summary>
    public TimeSpan? Duracao => FinishedAt is null ? null : FinishedAt - StartedAt;
}

/// <summary>
/// Histórico completo de quem está logado: o que está em andamento, o que já foi concluído
/// e os números consolidados.
/// </summary>
public sealed record HistoricoDoUsuarioDto(
    IReadOnlyList<ResumoDeTentativaDto> EmAndamento,
    IReadOnlyList<ResumoDeTentativaDto> Concluidas,
    DesempenhoConsolidadoDto? Desempenho);

/// <summary>
/// Agregados sobre as tentativas CONCLUÍDAS. É <c>null</c> quando não há nenhuma — evita a tela
/// exibir "melhor nota: 0", que seria mentira diferente de "ainda não há nota".
/// </summary>
/// <param name="VariacaoDesdeAAnterior">
/// Diferença entre a nota mais recente e a anterior. <c>null</c> com uma só tentativa concluída,
/// quando não existe "antes" com que comparar.
/// </param>
public sealed record DesempenhoConsolidadoDto(
    int TotalConcluidas,
    int Aprovacoes,
    int MelhorNota,
    int NotaMaisRecente,
    int? VariacaoDesdeAAnterior);
