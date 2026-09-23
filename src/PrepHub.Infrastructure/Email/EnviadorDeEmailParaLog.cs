using PrepHub.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace PrepHub.Infrastructure.Email;

/// <summary>
/// Substituto de desenvolvimento: escreve o e-mail no log em vez de enviar.
/// </summary>
/// <remarks>
/// É o que permite testar "esqueci minha senha" de ponta a ponta sem servidor de e-mail — o
/// link aparece no console e você abre no navegador. O aviso é em nível Warning de propósito:
/// se isso rodar em produção por configuração faltando, tem de doer no log, porque significa
/// que ninguém está recebendo link de redefinição. O <paramref name="motivo"/> viaja junto para
/// a mensagem dizer exatamente qual chave falta, em vez de deixar a pessoa adivinhando.
/// </remarks>
public sealed class EnviadorDeEmailParaLog : IEnviadorDeEmail
{
    private readonly ILogger<EnviadorDeEmailParaLog> _logger;
    private readonly string _motivo;

    public EnviadorDeEmailParaLog(ILogger<EnviadorDeEmailParaLog> logger, string motivo)
    {
        _logger = logger;
        _motivo = motivo;
    }

    public Task EnviarAsync(
        string destinatario,
        string assunto,
        string corpo,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "E-mail NÃO enviado ({Motivo}) — conteúdo abaixo.\n"
            + "Para: {Destinatario}\nAssunto: {Assunto}\n{Corpo}",
            _motivo,
            destinatario,
            assunto,
            corpo);

        return Task.CompletedTask;
    }
}
