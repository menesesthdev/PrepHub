using System.Net;
using System.Net.Mail;
using PrepHub.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace PrepHub.Infrastructure.Email;

/// <summary>Envio real por SMTP, registrado só quando <c>Email:SmtpHost</c> está configurado.</summary>
/// <remarks>
/// <para>
/// Usa <see cref="SmtpClient"/> da BCL para não trazer dependência nova. A Microsoft recomenda
/// MailKit para cenários modernos (o SmtpClient não suporta, por exemplo, OAuth no SMTP); se um
/// dia isso apertar, a troca é só esta classe — quem chama conhece apenas
/// <see cref="IEnviadorDeEmail"/>.
/// </para>
/// <para>
/// Falha de envio é registrada e engolida de propósito: o pedido de redefinição já foi gravado,
/// e propagar a exceção mudaria a resposta da tela justamente no caminho em que o e-mail
/// existe — o que denunciaria quais e-mails têm conta.
/// </para>
/// </remarks>
public sealed class EnviadorDeEmailSmtp : IEnviadorDeEmail
{
    private readonly OpcoesDeEmail _opcoes;
    private readonly ILogger<EnviadorDeEmailSmtp> _logger;

    public EnviadorDeEmailSmtp(OpcoesDeEmail opcoes, ILogger<EnviadorDeEmailSmtp> logger)
    {
        _opcoes = opcoes;
        _logger = logger;
    }

    public async Task EnviarAsync(
        string destinatario,
        string assunto,
        string corpo,
        CancellationToken cancellationToken = default)
    {
        using var cliente = new SmtpClient(_opcoes.SmtpHost, _opcoes.SmtpPort)
        {
            EnableSsl = _opcoes.UsarSsl
        };

        if (!string.IsNullOrWhiteSpace(_opcoes.Usuario))
        {
            cliente.Credentials = new NetworkCredential(_opcoes.Usuario, _opcoes.Senha);
        }

        using var mensagem = new MailMessage
        {
            From = new MailAddress(_opcoes.RemetenteEndereco, _opcoes.RemetenteNome),
            Subject = assunto,
            Body = corpo,
            IsBodyHtml = false
        };
        mensagem.To.Add(destinatario);

        try
        {
            await cliente.SendMailAsync(mensagem, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Requisição abortada por quem chamou (pessoa fechou a aba). Não é falha de envio e
            // não vira erro no log — é a lista curta de ERRO que faz alguém olhar para ela. A
            // guarda no token importa: o timeout do SmtpClient também chega como cancelamento,
            // e esse SIM é falha de envio, então cai no catch de baixo.
            throw;
        }
        catch (Exception ex)
        {
            // ⚠️ Captura ampla de propósito, e não a lista de tipos que estava aqui antes. O que
            // escapava dela era justamente o que acontece contra servidor real: AuthenticationException
            // no handshake TLS, SocketException com host errado, TaskCanceledException no timeout do
            // SmtpClient. Qualquer uma subiria até o controller e estouraria a página SÓ no caminho em
            // que a conta existe — e aí a tela de "esqueci minha senha", que responde igual para todo
            // mundo justamente para não dizer quem tem cadastro, passaria a dizer.
            _logger.LogError(ex, "Falha ao enviar e-mail por SMTP para {Destinatario}.", destinatario);
        }
    }
}
