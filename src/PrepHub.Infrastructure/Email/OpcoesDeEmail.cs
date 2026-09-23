namespace PrepHub.Infrastructure.Email;

/// <summary>
/// Configuração do envio de e-mail, lida da seção <c>Email</c>. Segue o mesmo padrão dos
/// provedores OAuth: sem <see cref="SmtpHost"/> configurado o envio real não é registrado e a
/// app usa o enviador de log — assim o projeto sobe e o fluxo de "esqueci minha senha"
/// funciona em desenvolvimento sem servidor de e-mail nenhum.
/// </summary>
public sealed class OpcoesDeEmail
{
    public const string Secao = "Email";

    public string? SmtpHost { get; set; }

    public int SmtpPort { get; set; } = 587;

    /// <summary>STARTTLS. Só desligar em servidor local de teste.</summary>
    public bool UsarSsl { get; set; } = true;

    public string? Usuario { get; set; }

    public string? Senha { get; set; }

    /// <summary>Remetente. Muitos provedores recusam envio se não casar com a conta autenticada.</summary>
    public string RemetenteEndereco { get; set; } = "nao-responda@prephub.local";

    public string RemetenteNome { get; set; } = "PrepHub";

    public bool TemHost => !string.IsNullOrWhiteSpace(SmtpHost);

    /// <summary>
    /// Usuário sem senha (ou senha sem usuário) — configuração pela metade, o caso típico de
    /// quem apontou para o Gmail mas ainda não colou a senha de app.
    /// </summary>
    public bool TemCredenciaisPelaMetade
        => string.IsNullOrWhiteSpace(Usuario) != string.IsNullOrWhiteSpace(Senha);

    /// <summary>
    /// Só considera o SMTP utilizável quando há host e as credenciais não estão pela metade.
    /// </summary>
    /// <remarks>
    /// Credencial NENHUMA é válida de propósito: é o caso do servidor SMTP local de teste
    /// (<c>tools/smtp-de-teste.py</c>), que não autentica. O que não pode passar é meia
    /// credencial, porque aí o envio falha e o link de redefinição desaparece — nem chega ao
    /// destinatário, nem sobra no log. Melhor cair para o log e dizer o que falta.
    /// </remarks>
    public bool EstaConfigurado => TemHost && !TemCredenciaisPelaMetade;

    /// <summary>Explica, para o log, por que o envio real não está em uso.</summary>
    public string MotivoDeNaoEnviar()
    {
        if (!TemHost)
        {
            return "Email:SmtpHost não está definido";
        }

        return string.IsNullOrWhiteSpace(Senha)
            ? "Email:Senha está vazio (com Email:Usuario definido)"
            : "Email:Usuario está vazio (com Email:Senha definido)";
    }
}
