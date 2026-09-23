using PrepHub.Application.Abstractions;

namespace PrepHub.Infrastructure.Aleatoriedade;

/// <summary>
/// Aleatoriedade de produção. <see cref="Random.Shared"/> é thread-safe e não precisa de semente
/// própria — o sorteio de prova não é sorteio criptográfico: prever a próxima questão não dá
/// vantagem nenhuma a quem não estudou.
/// </summary>
public sealed class GeradorDeAleatoriedade : IGeradorDeAleatoriedade
{
    public Random Criar() => Random.Shared;
}
