namespace PrepHub.Application.Abstractions;

/// <summary>
/// Fonte de tempo abstraída — o timer da prova e a submissão automática dependem do "agora".
/// Injetável para tornar a lógica de tempo testável sem depender de <see cref="DateTime.UtcNow"/>.
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }
}
