namespace PrepHub.Application.Abstractions;

/// <summary>
/// Envia e-mail transacional (hoje só o link de redefinição de senha).
/// </summary>
/// <remarks>
/// Abstração porque o provedor de envio é decisão de infraestrutura e ambiente: em
/// desenvolvimento o link vai para o log, em produção sai por SMTP. Quem chama não muda.
/// </remarks>
public interface IEnviadorDeEmail
{
    Task EnviarAsync(
        string destinatario,
        string assunto,
        string corpo,
        CancellationToken cancellationToken = default);
}
