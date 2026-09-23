namespace PrepHub.Application.Contracts;

/// <summary>
/// Desfecho de uma gravação de resposta. Os três casos existem porque o Web precisa responder
/// diferente a cada um: gravou, chegou tarde, ou veio malformada.
/// </summary>
/// <remarks>
/// Distinguir <see cref="TentativaEncerrada"/> de <see cref="RespostaInvalida"/> importa: a
/// primeira é corrida legítima (o prazo venceu entre a última navegação e o envio), a segunda é
/// cliente quebrado ou POST forjado. Colapsar as duas num "não gravou" esconderia justamente o
/// caso que vale investigar.
/// </remarks>
public enum ResultadoDeSalvarResposta
{
    /// <summary>Seleção, marcação e tempo foram persistidos.</summary>
    Gravada,

    /// <summary>A tentativa já estava finalizada (manualmente ou por tempo esgotado).</summary>
    TentativaEncerrada,

    /// <summary>
    /// A questão não pertence à composição sorteada desta tentativa, ou alguma alternativa
    /// enviada não pertence à questão.
    /// </summary>
    RespostaInvalida
}
