using PrepHub.Domain.Autenticacao;
using PrepHub.Domain.Common;
using PrepHub.Domain.Enums;

namespace PrepHub.Domain.Entidades;

/// <summary>
/// Pessoa que usa o simulado. A identidade pode vir de um provedor externo (Google, LinkedIn,
/// GitHub) ou de uma conta local com e-mail e senha — neste caso guardamos apenas o
/// <see cref="PasswordHash"/>, nunca a senha em si.
/// </summary>
/// <remarks>
/// A chave natural é o par <see cref="Provider"/> + <see cref="ProviderKey"/>, não o e-mail:
/// a mesma pessoa pode ter o mesmo e-mail em dois provedores, e o GitHub pode nem devolver
/// e-mail (o usuário escolhe mantê-lo privado). Na conta local a chave é o próprio e-mail
/// normalizado, o que dá "uma conta local por e-mail" pelo mesmo índice único, sem regra nova.
/// Vincular contas de provedores diferentes (ou somar senha a uma conta social) é um recurso
/// à parte — hoje cada par gera um usuário.
/// </remarks>
public class Usuario : Entity
{
    // Construtor exigido pelo EF Core (materialização).
    private Usuario()
    {
    }

    /// <summary>Conta vinda de provedor externo — sem senha nossa para guardar.</summary>
    public Usuario(
        ProvedorDeLogin provider,
        string providerKey,
        string name,
        string? email,
        string? avatarUrl,
        DateTime createdAt,
        Guid? id = null)
        : base(id ?? Guid.NewGuid())
    {
        if (provider == ProvedorDeLogin.Local)
        {
            throw new ArgumentException(
                $"Conta local deve ser criada por {nameof(CriarComSenha)}, que exige senha.",
                nameof(provider));
        }

        Provider = provider;
        ProviderKey = Guard.NotNullOrWhiteSpace(providerKey, nameof(providerKey));
        Name = Guard.NotNullOrWhiteSpace(name, nameof(name));
        Email = email;
        AvatarUrl = avatarUrl;
        CreatedAt = createdAt;
        LastLoginAt = createdAt;

        // Conta social já nasce confirmada: o provedor verificou o endereço antes de nos
        // entregar, e pedir confirmação de novo seria atrito sem ganho. Não é um detalhe de
        // conveniência — o GitHub pode devolver Email nulo, e exigir confirmação de um endereço
        // que não temos trancaria o login por um dado que nunca vai chegar.
        EmailConfirmedAt = createdAt;
    }

    /// <summary>
    /// Conta local. Recebe o hash já calculado: derivar senha é responsabilidade da
    /// infraestrutura (custo, algoritmo, parâmetros), e o domínio segue sem dependências.
    /// </summary>
    public static Usuario CriarComSenha(
        string name,
        string email,
        string passwordHash,
        DateTime createdAt,
        Guid? id = null)
        => new(name, email, passwordHash, createdAt, id);

    private Usuario(string name, string email, string passwordHash, DateTime createdAt, Guid? id)
        : base(id ?? Guid.NewGuid())
    {
        var normalizado = NormalizarEmail(email);

        Provider = ProvedorDeLogin.Local;
        // Chave natural = e-mail normalizado, então o índice único (Provider, ProviderKey)
        // já impede duas contas locais para o mesmo e-mail.
        ProviderKey = normalizado;
        Email = normalizado;
        Name = Guard.NotNullOrWhiteSpace(name, nameof(name));
        PasswordHash = Guard.NotNullOrWhiteSpace(passwordHash, nameof(passwordHash));
        CreatedAt = createdAt;
        LastLoginAt = createdAt;

        // EmailConfirmedAt fica nulo: aqui o endereço é só o que a pessoa digitou, e ninguém
        // provou nada ainda. É esse nulo que impede o login até o link do e-mail ser aberto.
    }

    /// <summary>
    /// Forma canônica do e-mail para comparação: sem espaços nas pontas e em minúsculas.
    /// Sem isso "Ana@x.com" e "ana@x.com" criariam duas contas e o login falharia conforme
    /// como a pessoa digitou.
    /// </summary>
    public static string NormalizarEmail(string email)
        => Guard.NotNullOrWhiteSpace(email, nameof(email)).ToLowerInvariant();

    public ProvedorDeLogin Provider { get; private set; }

    /// <summary>
    /// Identificador da pessoa dentro do provedor (claim NameIdentifier). Na conta local,
    /// o e-mail normalizado.
    /// </summary>
    public string ProviderKey { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    /// <summary>Opcional: o GitHub só devolve e-mail se a pessoa o tornar público.</summary>
    public string? Email { get; private set; }

    public string? AvatarUrl { get; private set; }

    /// <summary>
    /// Hash da senha, só em conta local — <c>null</c> em conta social, e é justamente esse
    /// <c>null</c> que impede tentar validar senha contra quem nunca cadastrou uma.
    /// </summary>
    public string? PasswordHash { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime LastLoginAt { get; private set; }

    /// <summary>Falhas de senha consecutivas. Zera a cada login bem-sucedido.</summary>
    public int FailedLoginAttempts { get; private set; }

    /// <summary>
    /// Instante até o qual a conta não aceita login. <c>null</c> quando não há bloqueio.
    /// </summary>
    public DateTime? LockoutEndsAt { get; private set; }

    /// <summary>
    /// Quando a posse do endereço foi provada. <c>null</c> em conta local ainda não confirmada;
    /// em conta social, o próprio instante da criação.
    /// </summary>
    public DateTime? EmailConfirmedAt { get; private set; }

    /// <summary>Conta com senha nossa, em oposição às que dependem de provedor externo.</summary>
    public bool EhContaLocal => Provider == ProvedorDeLogin.Local;

    /// <summary>
    /// O endereço já foi provado. Enquanto for <c>false</c> a conta existe mas não entra —
    /// é o que impede um e-mail digitado errado de virar conta sem dono alcançável, com a
    /// pessoa descobrindo o problema só na hora de recuperar a senha, quando já é tarde.
    /// </summary>
    public bool EmailConfirmado => EmailConfirmedAt is not null;

    public bool EstaBloqueada(DateTime agora) => LockoutEndsAt is { } fim && agora < fim;

    /// <summary>
    /// Reaplica o perfil vindo do provedor a cada login — nome, foto e e-mail mudam lá fora
    /// e a nossa cópia é só um cache. Campos vazios não sobrescrevem o que já temos.
    /// </summary>
    public void RegistrarLogin(string? name, string? email, string? avatarUrl, DateTime at)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            Name = name;
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            Email = email;
        }

        if (!string.IsNullOrWhiteSpace(avatarUrl))
        {
            AvatarUrl = avatarUrl;
        }

        LastLoginAt = at;
    }

    /// <summary>
    /// Carimba o acesso da conta local e limpa o histórico de falhas. Não há perfil a
    /// re-sincronizar: nome e e-mail só mudam se a própria pessoa editar.
    /// </summary>
    public void RegistrarLoginLocal(DateTime at)
    {
        LastLoginAt = at;

        // Acerto zera o contador: o limite é de falhas CONSECUTIVAS. Sem isso, quem erra a
        // senha de vez em quando ao longo de meses acabaria bloqueado sem nunca ter sofrido
        // ataque nenhum.
        FailedLoginAttempts = 0;
        LockoutEndsAt = null;
    }

    /// <summary>
    /// Contabiliza uma senha errada e bloqueia a conta ao atingir
    /// <see cref="PoliticaDeTentativasDeLogin.MaximoDeFalhas"/>.
    /// </summary>
    public void RegistrarFalhaDeLogin(DateTime at)
    {
        FailedLoginAttempts++;

        if (FailedLoginAttempts >= PoliticaDeTentativasDeLogin.MaximoDeFalhas)
        {
            LockoutEndsAt = at.Add(PoliticaDeTentativasDeLogin.DuracaoDoBloqueio);

            // Zera junto: o bloqueio já pagou por estas falhas. Se não zerasse, a primeira
            // falha depois do bloqueio expirar bloquearia de novo na hora.
            FailedLoginAttempts = 0;
        }
    }

    /// <summary>
    /// Registra que o endereço foi provado, ao abrir o link enviado por e-mail. Idempotente:
    /// confirmar de novo não reescreve a data, porque o que interessa é quando a posse foi
    /// provada pela primeira vez.
    /// </summary>
    public void ConfirmarEmail(DateTime at)
    {
        if (!EhContaLocal)
        {
            // Conta social já nasce confirmada; chegar aqui significa que o token de confirmação
            // foi emitido no caminho errado, e seguir em frente esconderia o defeito.
            throw new InvalidOperationException("Conta de provedor externo já tem o e-mail verificado na origem.");
        }

        EmailConfirmedAt ??= at;
    }

    /// <summary>
    /// Troca a senha (redefinição por link) e devolve a conta ao estado limpo — inclusive
    /// liberando bloqueio, porque quem provou controlar o e-mail é o dono e não deve ficar
    /// preso pelas tentativas de quem o atacou.
    /// </summary>
    public void DefinirNovaSenha(string passwordHash, DateTime at)
    {
        if (!EhContaLocal)
        {
            // Definir senha numa conta social a converteria em local pela porta de trás,
            // sem que a pessoa tenha pedido — e o (Provider, ProviderKey) dela nem casa
            // com o de uma conta local.
            throw new InvalidOperationException("Conta de provedor externo não tem senha para redefinir.");
        }

        PasswordHash = Guard.NotNullOrWhiteSpace(passwordHash, nameof(passwordHash));
        FailedLoginAttempts = 0;
        LockoutEndsAt = null;
        LastLoginAt = at;

        // Redefinir a senha também confirma o endereço, e não é atalho: o link de redefinição
        // chegou naquela caixa de entrada, que é exatamente a prova que a confirmação pede. Sem
        // isto, quem se cadastrou, perdeu o e-mail de confirmação e recuperou a senha entraria
        // no ciclo absurdo de provar o endereço duas vezes e continuar barrado.
        EmailConfirmedAt ??= at;
    }
}
