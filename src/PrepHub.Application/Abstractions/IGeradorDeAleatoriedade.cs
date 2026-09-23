namespace PrepHub.Application.Abstractions;

/// <summary>
/// Fonte de aleatoriedade do sorteio, abstraída pelo mesmo motivo que <see cref="IClock"/>:
/// teste com semente fixa reproduz a mesma prova e deixa as regras do sorteio verificáveis.
/// </summary>
public interface IGeradorDeAleatoriedade
{
    Random Criar();
}
