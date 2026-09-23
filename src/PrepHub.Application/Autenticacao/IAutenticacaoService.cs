using PrepHub.Application.Contracts;

namespace PrepHub.Application.Autenticacao;

public interface IAutenticacaoService
{
    /// <summary>
    /// Resolve o usuário local a partir do perfil externo: cria no primeiro login e,
    /// nos seguintes, atualiza o perfil e carimba a data de acesso.
    /// </summary>
    Task<UsuarioDto> ObterOuCriarAsync(LoginExternoRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cria a conta local (e-mail + senha) SEM autenticar, e devolve junto o link de
    /// confirmação que o Web deve enviar por e-mail. A conta só passa a entrar depois que esse
    /// link for aberto.
    /// </summary>
    Task<ResultadoDeCadastro> CadastrarComSenhaAsync(
        CadastroLocalRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Valida e-mail e senha de uma conta local. Recusa com
    /// <see cref="FalhaDeAutenticacao.EmailNaoConfirmado"/> quando as credenciais estão certas
    /// mas o endereço nunca foi confirmado — nessa ordem, nunca antes de conferir a senha.
    /// </summary>
    Task<ResultadoDeAutenticacao> AutenticarComSenhaAsync(
        LoginLocalRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Emite um link de confirmação novo para a conta local do e-mail informado, invalidando o
    /// anterior. Devolve <c>null</c> quando não existe conta local pendente com esse e-mail — e
    /// quem chama deve responder igual nos dois casos, senão a tela vira consulta de cadastro.
    /// </summary>
    Task<TokenDeConfirmacaoDto?> ReenviarConfirmacaoDeEmailAsync(
        string email,
        CancellationToken cancellationToken = default);

    /// <summary>Consome o link de confirmação e libera o acesso da conta.</summary>
    Task<ResultadoDeConfirmacao> ConfirmarEmailAsync(
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Emite um token de redefinição para a conta local do e-mail informado. Devolve
    /// <c>null</c> quando não existe conta local com esse e-mail — e quem chama deve responder
    /// exatamente igual nos dois casos, senão a tela vira consulta de quem tem conta aqui.
    /// </summary>
    Task<TokenDeRedefinicaoDto?> SolicitarRedefinicaoDeSenhaAsync(
        string email,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Diz se o link ainda serve, sem consumi-lo. Existe para a tela recusar link vencido
    /// ANTES de a pessoa digitar a senha nova.
    /// </summary>
    Task<bool> TokenDeRedefinicaoEstaValidoAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Troca a senha a partir do token recebido por e-mail.</summary>
    Task<ResultadoDeAutenticacao> RedefinirSenhaAsync(
        RedefinicaoDeSenhaRequest request,
        CancellationToken cancellationToken = default);

    Task<UsuarioDto?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default);
}
