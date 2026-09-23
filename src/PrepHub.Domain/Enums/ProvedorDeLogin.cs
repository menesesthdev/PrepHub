namespace PrepHub.Domain.Enums;

/// <summary>
/// Origem da identidade usada no login. O par (provedor, chave) é o que identifica a pessoa —
/// nos provedores sociais a chave é o id externo; em <see cref="Local"/> é o próprio e-mail
/// normalizado, e só nesse caso existe senha guardada (como hash).
/// </summary>
public enum ProvedorDeLogin
{
    Google = 1,
    LinkedIn = 2,
    GitHub = 3,

    /// <summary>Conta criada aqui mesmo, com e-mail e senha — sem provedor externo.</summary>
    Local = 4
}
