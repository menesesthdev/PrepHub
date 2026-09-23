namespace PrepHub.Application.Contracts;

/// <summary>Dados do cadastro com e-mail e senha. A senha chega em texto e sai daqui como hash.</summary>
public sealed record CadastroLocalRequest(string Name, string Email, string Password);

/// <summary>Credenciais de login da conta local.</summary>
public sealed record LoginLocalRequest(string Email, string Password);

/// <summary>Nova senha, apresentada junto com o token que veio no link do e-mail.</summary>
public sealed record RedefinicaoDeSenhaRequest(string Token, string NovaSenha);

/// <summary>
/// Token recém-emitido, para o Web montar o link e enviar o e-mail. O valor em texto existe
/// só aqui e no e-mail — no banco fica apenas o hash.
/// </summary>
public sealed record TokenDeRedefinicaoDto(string Token, string Email, string Nome);

/// <summary>
/// Link de confirmação recém-emitido. Mesma regra do token de redefinição: o valor em texto
/// existe só nesta requisição e no e-mail.
/// </summary>
public sealed record TokenDeConfirmacaoDto(string Token, string Email, string Nome);

/// <summary>Motivo pelo qual cadastro ou login não passou.</summary>
public enum FalhaDeAutenticacao
{
    /// <summary>
    /// Login recusado. Deliberadamente não distingue "e-mail inexistente" de "senha errada"
    /// nem de "essa conta é social": qualquer diferença aí vira oráculo para descobrir quem
    /// tem conta no sistema.
    /// </summary>
    CredenciaisInvalidas = 1,

    /// <summary>Já existe conta local com esse e-mail.</summary>
    EmailJaCadastrado = 2,

    /// <summary>Senha fora da <see cref="Domain.Autenticacao.PoliticaDeSenha"/>.</summary>
    SenhaInaceitavel = 3,

    /// <summary>
    /// Link de redefinição inexistente, já usado ou vencido. Os três casos viram um só na
    /// tela — distinguir "não existe" de "expirou" ajudaria a sondar tokens.
    /// </summary>
    TokenInvalido = 4,

    /// <summary>
    /// Credenciais certas, mas o e-mail nunca foi confirmado.
    /// </summary>
    /// <remarks>
    /// ⚠️ Esta é a ÚNICA falha de login que a tela distingue de <see cref="CredenciaisInvalidas"/>,
    /// e só não vira oráculo por causa da ordem em que é verificada: o hash da senha é conferido
    /// ANTES, então quem vê esta mensagem já provou saber a senha da conta — não descobriu nada
    /// que não soubesse. Checar a confirmação antes da senha transformaria o login num consultor
    /// de "este e-mail tem cadastro aqui", que é o que o resto do fluxo se esforça para impedir.
    /// </remarks>
    EmailNaoConfirmado = 5
}

/// <summary>Desfecho de abrir um link de confirmação de e-mail.</summary>
public enum ResultadoDeConfirmacao
{
    /// <summary>Endereço confirmado agora — a conta passa a entrar.</summary>
    Confirmado = 1,

    /// <summary>
    /// O link já tinha sido usado e a conta já está confirmada. Separado de
    /// <see cref="Confirmado"/> só para a tela não dizer "pronto, confirmamos" a quem
    /// simplesmente clicou duas vezes; para efeito de acesso, os dois são sucesso.
    /// </summary>
    JaConfirmado = 2,

    /// <summary>Link inexistente, vencido, ou já invalidado por um reenvio.</summary>
    TokenInvalido = 3
}

/// <summary>
/// Saída do cadastro. Separada de <see cref="ResultadoDeAutenticacao"/> porque cadastrar deixou
/// de autenticar: a conta nasce sem acesso e carrega um link de confirmação a enviar, e reusar
/// um tipo cuja propriedade se chama <c>Autenticou</c> faria o controller mentir sobre o que
/// acabou de acontecer.
/// </summary>
public sealed record ResultadoDeCadastro
{
    private ResultadoDeCadastro(UsuarioDto? usuario, TokenDeConfirmacaoDto? confirmacao, FalhaDeAutenticacao? falha)
    {
        Usuario = usuario;
        Confirmacao = confirmacao;
        Falha = falha;
    }

    public UsuarioDto? Usuario { get; }

    /// <summary>Link a enviar por e-mail. Presente sempre que o cadastro deu certo.</summary>
    public TokenDeConfirmacaoDto? Confirmacao { get; }

    public FalhaDeAutenticacao? Falha { get; }

    public bool Cadastrou => Usuario is not null;

    public static ResultadoDeCadastro Sucesso(UsuarioDto usuario, TokenDeConfirmacaoDto confirmacao)
        => new(usuario, confirmacao, null);

    public static ResultadoDeCadastro Recusado(FalhaDeAutenticacao falha) => new(null, null, falha);
}

/// <summary>
/// Saída de cadastro/login. Falha de credencial é resultado esperado do caso de uso, não
/// excepcional — devolver em vez de lançar deixa o controller tratar sem capturar exceção.
/// </summary>
public sealed record ResultadoDeAutenticacao
{
    private ResultadoDeAutenticacao(UsuarioDto? usuario, FalhaDeAutenticacao? falha)
    {
        Usuario = usuario;
        Falha = falha;
    }

    public UsuarioDto? Usuario { get; }

    public FalhaDeAutenticacao? Falha { get; }

    public bool Autenticou => Usuario is not null;

    public static ResultadoDeAutenticacao Sucesso(UsuarioDto usuario) => new(usuario, null);

    public static ResultadoDeAutenticacao Recusado(FalhaDeAutenticacao falha) => new(null, falha);
}
