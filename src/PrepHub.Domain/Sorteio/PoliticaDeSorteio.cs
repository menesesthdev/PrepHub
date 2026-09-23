namespace PrepHub.Domain.Sorteio;

/// <summary>
/// Parâmetros que governam a montagem de uma prova a partir do banco de questões.
/// Ficam no Domain porque são regra de produto, não configuração de infraestrutura.
/// </summary>
/// <param name="TentativasDeMemoria">
/// Quantas tentativas anteriores do usuário contam como "recente". Uma questão vista dentro
/// dessa janela é considerada repetida; fora dela volta a ser tratada como inédita — a memória
/// é deliberadamente curta, senão o banco se esgota e o sorteio deixa de ter o que escolher.
/// </param>
/// <param name="ProporcaoMaximaRepetidas">
/// Teto de questões repetidas numa prova, como fração do total (0.15 = 15%). Vale sobre a prova
/// INTEIRA: o orçamento é calculado uma vez sobre o total e depois repartido entre os domínios,
/// e não aplicado como fração dentro de cada um — arredondar por domínio devolveria bem menos
/// vagas do que a fração declarada aqui. É um TETO para repetição deliberada, não uma garantia:
/// se o usuário já viu quase tudo, o sorteio ultrapassa o teto em vez de devolver uma prova
/// incompleta.
/// </param>
public sealed record PoliticaDeSorteio(
    int TentativasDeMemoria,
    decimal ProporcaoMaximaRepetidas)
{
    /// <summary>
    /// Padrão do projeto: esquece o que foi visto há mais de 3 tentativas e reserva até 15% da
    /// prova para reencontro deliberado — sempre priorizando o que a pessoa ERROU. Repetição
    /// zero desperdiçaria o maior valor pedagógico que o histórico oferece; repetição livre
    /// devolveria a mesma prova de sempre.
    /// </summary>
    public static PoliticaDeSorteio Padrao { get; } = new(TentativasDeMemoria: 3, ProporcaoMaximaRepetidas: 0.15m);
}
