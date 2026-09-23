using PrepHub.Application.Autenticacao;
using PrepHub.Application.Contracts;
using PrepHub.Application.Tests.Fakes;
using PrepHub.Domain.Autenticacao;
using PrepHub.Domain.Enums;

namespace PrepHub.Application.Tests;

public class AutenticacaoServiceTests
{
    private static readonly DateTime Agora = new(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);

    private static (AutenticacaoService service, InMemoryUsuarioRepository repo, FixedClock clock) Build()
    {
        var (service, repo, clock, _) = BuildComHasher();
        return (service, repo, clock);
    }

    private static (AutenticacaoService service, InMemoryUsuarioRepository repo, FixedClock clock, FakeHasherDeSenha hasher)
        BuildComHasher()
    {
        var (service, repo, clock, hasher, _) = BuildCompleto();
        return (service, repo, clock, hasher);
    }

    private static (
        AutenticacaoService service,
        InMemoryUsuarioRepository repo,
        FixedClock clock,
        FakeHasherDeSenha hasher,
        FakeGeradorDeTokenSeguro tokens) BuildCompleto()
    {
        var h = AutenticacaoServiceHarness.Novo(Agora);
        return (h.Service, h.Repo, h.Clock, h.Hasher, h.Tokens);
    }

    private static LoginExternoRequest Login(
        ProvedorDeLogin provider = ProvedorDeLogin.Google,
        string key = "123",
        string name = "Ana",
        string? email = "ana@example.com",
        string? avatar = "https://cdn/ana.png")
        => new(provider, key, name, email, avatar);

    [Fact]
    public async Task PrimeiroLogin_CriaUsuario()
    {
        var (service, repo, _) = Build();

        var dto = await service.ObterOuCriarAsync(Login());

        Assert.Single(repo.Todos);
        Assert.Equal("Ana", dto.Name);
        Assert.Equal("ana@example.com", dto.Email);
        Assert.Equal(ProvedorDeLogin.Google, dto.Provider);
    }

    [Fact]
    public async Task SegundoLogin_ReaproveitaOMesmoUsuario()
    {
        var (service, repo, _) = Build();

        var primeiro = await service.ObterOuCriarAsync(Login());
        var segundo = await service.ObterOuCriarAsync(Login());

        Assert.Single(repo.Todos);
        Assert.Equal(primeiro.Id, segundo.Id);
    }

    // A identidade é o par (provedor, chave): o mesmo e-mail em provedores diferentes
    // são pessoas diferentes para nós, porque vincular contas é outro recurso.
    [Fact]
    public async Task MesmoEmailEmProvedoresDiferentes_GeraUsuariosDistintos()
    {
        var (service, repo, _) = Build();

        var google = await service.ObterOuCriarAsync(Login(ProvedorDeLogin.Google, key: "g-1"));
        var github = await service.ObterOuCriarAsync(Login(ProvedorDeLogin.GitHub, key: "gh-1"));

        Assert.Equal(2, repo.Todos.Count);
        Assert.NotEqual(google.Id, github.Id);
    }

    [Fact]
    public async Task LoginPosterior_AtualizaPerfilEUltimoAcesso()
    {
        var (service, repo, clock) = Build();
        await service.ObterOuCriarAsync(Login(name: "Ana", avatar: "https://cdn/antigo.png"));

        clock.Advance(TimeSpan.FromDays(3));
        var dto = await service.ObterOuCriarAsync(Login(name: "Ana Silva", avatar: "https://cdn/novo.png"));

        Assert.Equal("Ana Silva", dto.Name);
        Assert.Equal("https://cdn/novo.png", dto.AvatarUrl);
        Assert.Equal(Agora.AddDays(3), repo.Todos[0].LastLoginAt);
        Assert.Equal(Agora, repo.Todos[0].CreatedAt);
    }

    // O GitHub pode não devolver e-mail nem foto; um login desses não deve apagar
    // o que já sabíamos da pessoa.
    [Fact]
    public async Task CamposVazios_NaoSobrescrevemOPerfilExistente()
    {
        var (service, repo, _) = Build();
        await service.ObterOuCriarAsync(Login(name: "Ana", email: "ana@example.com", avatar: "https://cdn/ana.png"));

        var dto = await service.ObterOuCriarAsync(Login(name: "", email: null, avatar: null));

        Assert.Equal("Ana", dto.Name);
        Assert.Equal("ana@example.com", dto.Email);
        Assert.Equal("https://cdn/ana.png", dto.AvatarUrl);
    }

    [Fact]
    public async Task SemNomeNoPrimeiroLogin_UsaRotuloPadrao()
    {
        var (service, _, _) = Build();

        var dto = await service.ObterOuCriarAsync(Login(name: "  ", email: null, avatar: null));

        Assert.Equal("Candidato", dto.Name);
    }

    [Fact]
    public async Task ObterPorId_DevolveNullQuandoNaoExiste()
    {
        var (service, _, _) = Build();

        Assert.Null(await service.ObterPorIdAsync(Guid.NewGuid()));
    }

    // ---- Conta local: cadastro -------------------------------------------

    private static CadastroLocalRequest Cadastro(
        string nome = "Ana",
        string email = "ana@example.com",
        string senha = "senha-longa-o-suficiente")
        => new(nome, email, senha);

    /// <summary>
    /// Cadastra e já abre o link de confirmação — o estado em que quase todo teste de login e de
    /// redefinição quer começar, porque é o estado de uma conta normal em uso.
    /// </summary>
    /// <remarks>
    /// Fica como helper explícito do teste, e não escondido dentro do serviço, justamente porque
    /// vários testes precisam do estado OPOSTO: cadastrada e ainda não confirmada.
    /// </remarks>
    private static async Task<ResultadoDeCadastro> CadastrarConfirmadoAsync(
        AutenticacaoService service,
        CadastroLocalRequest request)
    {
        var resultado = await service.CadastrarComSenhaAsync(request);

        if (resultado.Cadastrou)
        {
            await service.ConfirmarEmailAsync(resultado.Confirmacao!.Token);
        }

        return resultado;
    }

    [Fact]
    public async Task Cadastro_CriaContaLocalPendenteDeConfirmacao()
    {
        var (service, repo, _, _) = BuildComHasher();

        var resultado = await service.CadastrarComSenhaAsync(Cadastro());

        Assert.True(resultado.Cadastrou);
        Assert.Equal("Ana", resultado.Usuario!.Name);
        Assert.Equal(ProvedorDeLogin.Local, resultado.Usuario.Provider);
        Assert.Single(repo.Todos);
        Assert.True(repo.Todos[0].EhContaLocal);

        // A conta nasce SEM acesso: é este falso que impede um endereço digitado errado de
        // virar conta que ninguém alcança.
        Assert.False(repo.Todos[0].EmailConfirmado);
        Assert.NotNull(resultado.Confirmacao);
    }

    // O que nunca pode acontecer: a senha digitada ir direto para o campo persistido. O fake
    // é determinístico, então o valor esperado prova que a senha passou PELO hasher — que o
    // resultado seja irreversível é responsabilidade do PBKDF2, coberta na Infrastructure.
    [Fact]
    public async Task Cadastro_PersisteOHashEmVezDaSenha()
    {
        var (service, repo, _, hasher) = BuildComHasher();

        await service.CadastrarComSenhaAsync(Cadastro(senha: "senha-secreta-123"));

        var persistido = repo.Todos[0].PasswordHash;
        Assert.NotEqual("senha-secreta-123", persistido);
        Assert.Equal(hasher.Hash("senha-secreta-123"), persistido);
    }

    // Sem normalizar, "Ana@Example.com " viraria uma segunda conta e o login passaria a
    // depender de como a pessoa digitou.
    [Fact]
    public async Task Cadastro_NormalizaOEmailComoChaveDaIdentidade()
    {
        var (service, repo, _, _) = BuildComHasher();

        await service.CadastrarComSenhaAsync(Cadastro(email: "  Ana@Example.COM  "));

        Assert.Equal("ana@example.com", repo.Todos[0].Email);
        Assert.Equal("ana@example.com", repo.Todos[0].ProviderKey);
    }

    [Fact]
    public async Task Cadastro_ComEmailJaCadastrado_Recusa()
    {
        var (service, repo, _, _) = BuildComHasher();
        await service.CadastrarComSenhaAsync(Cadastro(email: "ana@example.com"));

        // Caixa diferente é o mesmo e-mail — a duplicata tem de ser barrada aqui também.
        var resultado = await service.CadastrarComSenhaAsync(Cadastro(email: "ANA@example.com"));

        Assert.False(resultado.Cadastrou);
        Assert.Equal(FalhaDeAutenticacao.EmailJaCadastrado, resultado.Falha);
        Assert.Single(repo.Todos);
    }

    [Theory]
    [InlineData("1234567")]  // um caractere abaixo do mínimo
    [InlineData("")]
    public async Task Cadastro_ComSenhaCurta_Recusa(string senha)
    {
        var (service, repo, _, _) = BuildComHasher();

        var resultado = await service.CadastrarComSenhaAsync(Cadastro(senha: senha));

        Assert.Equal(FalhaDeAutenticacao.SenhaInaceitavel, resultado.Falha);
        Assert.Empty(repo.Todos);
    }

    [Fact]
    public async Task Cadastro_ComSenhaAcimaDoTeto_Recusa()
    {
        var (service, repo, _, _) = BuildComHasher();
        var gigante = new string('x', PoliticaDeSenha.TamanhoMaximo + 1);

        var resultado = await service.CadastrarComSenhaAsync(Cadastro(senha: gigante));

        Assert.Equal(FalhaDeAutenticacao.SenhaInaceitavel, resultado.Falha);
        Assert.Empty(repo.Todos);
    }

    // Decisão do modelo: a identidade é (provedor, chave), então o mesmo e-mail numa conta
    // Google e numa conta local são dois usuários — como já vale entre Google e GitHub.
    [Fact]
    public async Task Cadastro_ComEmailDeContaSocial_CriaUsuarioSeparado()
    {
        var (service, repo, _, _) = BuildComHasher();
        await service.ObterOuCriarAsync(Login(email: "ana@example.com"));

        var resultado = await service.CadastrarComSenhaAsync(Cadastro(email: "ana@example.com"));

        Assert.True(resultado.Cadastrou);
        Assert.Equal(2, repo.Todos.Count);
    }

    // ---- Conta local: login ----------------------------------------------

    [Fact]
    public async Task LoginLocal_ComSenhaCorreta_Autentica()
    {
        var (service, repo, clock, _) = BuildComHasher();
        var cadastro = await CadastrarConfirmadoAsync(service, Cadastro(senha: "senha-correta-1"));

        clock.Advance(TimeSpan.FromDays(2));
        var resultado = await service.AutenticarComSenhaAsync(
            new LoginLocalRequest("ana@example.com", "senha-correta-1"));

        Assert.True(resultado.Autenticou);
        Assert.Equal(cadastro.Usuario!.Id, resultado.Usuario!.Id);
        Assert.Equal(Agora.AddDays(2), repo.Todos[0].LastLoginAt);
    }

    [Fact]
    public async Task LoginLocal_IgnoraCaixaDoEmail()
    {
        var (service, _, _, _) = BuildComHasher();
        await CadastrarConfirmadoAsync(service, Cadastro(email: "ana@example.com", senha: "senha-correta-1"));

        var resultado = await service.AutenticarComSenhaAsync(
            new LoginLocalRequest("ANA@Example.com", "senha-correta-1"));

        Assert.True(resultado.Autenticou);
    }

    [Fact]
    public async Task LoginLocal_ComSenhaErrada_Recusa()
    {
        var (service, _, _, _) = BuildComHasher();
        await CadastrarConfirmadoAsync(service, Cadastro(senha: "senha-correta-1"));

        var resultado = await service.AutenticarComSenhaAsync(
            new LoginLocalRequest("ana@example.com", "senha-errada-1"));

        Assert.False(resultado.Autenticou);
        Assert.Equal(FalhaDeAutenticacao.CredenciaisInvalidas, resultado.Falha);
    }

    // E-mail inexistente tem de devolver o MESMO motivo de senha errada, e ainda gastar uma
    // verificação de hash: responder na hora entregaria, pelo tempo, quem tem conta aqui.
    [Fact]
    public async Task LoginLocal_ComEmailInexistente_RecusaPeloMesmoMotivoEVerificaHashDeReferencia()
    {
        var (service, _, _, hasher) = BuildComHasher();

        var resultado = await service.AutenticarComSenhaAsync(
            new LoginLocalRequest("ninguem@example.com", "senha-qualquer-1"));

        Assert.Equal(FalhaDeAutenticacao.CredenciaisInvalidas, resultado.Falha);
        Assert.Equal(1, hasher.ChamadasDeVerificacao);
    }

    // Quem entrou por Google não tem senha nossa; tentar senha nessa conta é indistinguível
    // de e-mail inexistente, de propósito.
    [Fact]
    public async Task LoginLocal_EmContaSocial_Recusa()
    {
        var (service, _, _, _) = BuildComHasher();
        await service.ObterOuCriarAsync(Login(email: "ana@example.com"));

        var resultado = await service.AutenticarComSenhaAsync(
            new LoginLocalRequest("ana@example.com", "qualquer-senha-1"));

        Assert.False(resultado.Autenticou);
        Assert.Equal(FalhaDeAutenticacao.CredenciaisInvalidas, resultado.Falha);
    }

    // Senha acima do teto é recusada ANTES de derivar hash — senão um POST com megabytes de
    // "senha" viraria custo de CPU controlado por quem envia.
    [Fact]
    public async Task LoginLocal_ComSenhaAcimaDoTeto_RecusaSemHashear()
    {
        var (service, _, _, hasher) = BuildComHasher();
        await CadastrarConfirmadoAsync(service, Cadastro(senha: "senha-correta-1"));

        var resultado = await service.AutenticarComSenhaAsync(
            new LoginLocalRequest("ana@example.com", new string('x', PoliticaDeSenha.TamanhoMaximo + 1)));

        Assert.Equal(FalhaDeAutenticacao.CredenciaisInvalidas, resultado.Falha);
        Assert.Equal(0, hasher.ChamadasDeVerificacao);
    }

    [Theory]
    [InlineData("", "senha-correta-1")]
    [InlineData("ana@example.com", "")]
    public async Task LoginLocal_ComCampoVazio_Recusa(string email, string senha)
    {
        var (service, _, _, _) = BuildComHasher();
        await CadastrarConfirmadoAsync(service, Cadastro(senha: "senha-correta-1"));

        var resultado = await service.AutenticarComSenhaAsync(new LoginLocalRequest(email, senha));

        Assert.Equal(FalhaDeAutenticacao.CredenciaisInvalidas, resultado.Falha);
    }

    // ---- Bloqueio por tentativas -----------------------------------------

    private static async Task ErrarSenha(AutenticacaoService service, int vezes)
    {
        for (var i = 0; i < vezes; i++)
        {
            await service.AutenticarComSenhaAsync(new LoginLocalRequest("ana@example.com", "errada-000"));
        }
    }

    [Fact]
    public async Task LoginLocal_ContaFalhas_ESoBloqueiaNoLimite()
    {
        var (service, repo, _, _) = BuildComHasher();
        await CadastrarConfirmadoAsync(service, Cadastro(senha: "senha-correta-1"));

        await ErrarSenha(service, PoliticaDeTentativasDeLogin.MaximoDeFalhas - 1);

        // Uma falha antes do limite a conta ainda aceita a senha certa.
        Assert.False(repo.Todos[0].EstaBloqueada(Agora));
        Assert.True((await service.AutenticarComSenhaAsync(
            new LoginLocalRequest("ana@example.com", "senha-correta-1"))).Autenticou);
    }

    [Fact]
    public async Task LoginLocal_AoAtingirOLimite_BloqueiaAteAJanelaPassar()
    {
        var (service, repo, clock, _) = BuildComHasher();
        await CadastrarConfirmadoAsync(service, Cadastro(senha: "senha-correta-1"));

        await ErrarSenha(service, PoliticaDeTentativasDeLogin.MaximoDeFalhas);

        Assert.True(repo.Todos[0].EstaBloqueada(clock.UtcNow));

        // Nem a senha CORRETA entra durante o bloqueio — é o que o bloqueio significa.
        var durante = await service.AutenticarComSenhaAsync(
            new LoginLocalRequest("ana@example.com", "senha-correta-1"));
        Assert.False(durante.Autenticou);
        Assert.Equal(FalhaDeAutenticacao.CredenciaisInvalidas, durante.Falha);

        // Expira sozinho: bloqueio permanente viraria arma para trancar a conta de outra pessoa.
        clock.Advance(PoliticaDeTentativasDeLogin.DuracaoDoBloqueio + TimeSpan.FromMinutes(1));
        Assert.True((await service.AutenticarComSenhaAsync(
            new LoginLocalRequest("ana@example.com", "senha-correta-1"))).Autenticou);
    }

    // O limite é de falhas CONSECUTIVAS: quem erra, acerta e erra de novo não deve chegar ao
    // bloqueio somando erros de meses diferentes.
    [Fact]
    public async Task LoginLocal_AcertoZeraOContadorDeFalhas()
    {
        var (service, repo, _, _) = BuildComHasher();
        await CadastrarConfirmadoAsync(service, Cadastro(senha: "senha-correta-1"));

        await ErrarSenha(service, PoliticaDeTentativasDeLogin.MaximoDeFalhas - 1);
        await service.AutenticarComSenhaAsync(new LoginLocalRequest("ana@example.com", "senha-correta-1"));
        await ErrarSenha(service, PoliticaDeTentativasDeLogin.MaximoDeFalhas - 1);

        Assert.False(repo.Todos[0].EstaBloqueada(Agora));
    }

    // Durante o bloqueio a verificação de referência é gastada de propósito: responder mais
    // rápido avisaria, pelo tempo, que aquela conta existe e está travada.
    [Fact]
    public async Task LoginLocal_DuranteBloqueio_NaoConfereOHashRealMasGastaOTempo()
    {
        var (service, _, _, hasher) = BuildComHasher();
        await CadastrarConfirmadoAsync(service, Cadastro(senha: "senha-correta-1"));
        await ErrarSenha(service, PoliticaDeTentativasDeLogin.MaximoDeFalhas);

        var antes = hasher.ChamadasDeVerificacao;
        await service.AutenticarComSenhaAsync(new LoginLocalRequest("ana@example.com", "senha-correta-1"));

        Assert.Equal(antes + 1, hasher.ChamadasDeVerificacao);
    }

    // ---- Redefinição de senha --------------------------------------------

    [Fact]
    public async Task Redefinicao_ComTokenValido_TrocaASenha()
    {
        var (service, _, _, _, _) = BuildCompleto();
        await CadastrarConfirmadoAsync(service, Cadastro(senha: "senha-antiga-1"));

        var emitido = await service.SolicitarRedefinicaoDeSenhaAsync("ana@example.com");
        Assert.NotNull(emitido);

        var resultado = await service.RedefinirSenhaAsync(
            new RedefinicaoDeSenhaRequest(emitido!.Token, "senha-nova-123"));

        Assert.True(resultado.Autenticou);
        Assert.True((await service.AutenticarComSenhaAsync(
            new LoginLocalRequest("ana@example.com", "senha-nova-123"))).Autenticou);
        Assert.False((await service.AutenticarComSenhaAsync(
            new LoginLocalRequest("ana@example.com", "senha-antiga-1"))).Autenticou);
    }

    // O que viaja no e-mail não pode estar no banco: quem lê um dump não redefine senha alheia.
    [Fact]
    public async Task Redefinicao_GuardaApenasOHashDoToken()
    {
        var (service, repo, _, _, tokens) = BuildCompleto();
        await CadastrarConfirmadoAsync(service, Cadastro());

        var emitido = await service.SolicitarRedefinicaoDeSenhaAsync("ana@example.com");

        Assert.Equal(tokens.UltimoGerado, emitido!.Token);
        Assert.Single(repo.Tokens);
        Assert.NotEqual(emitido.Token, repo.Tokens[0].TokenHash);
        Assert.Equal(tokens.Hash(emitido.Token), repo.Tokens[0].TokenHash);
    }

    [Fact]
    public async Task Redefinicao_TokenServeUmaVezSo()
    {
        var (service, _, _, _, _) = BuildCompleto();
        await CadastrarConfirmadoAsync(service, Cadastro());
        var emitido = await service.SolicitarRedefinicaoDeSenhaAsync("ana@example.com");

        await service.RedefinirSenhaAsync(new RedefinicaoDeSenhaRequest(emitido!.Token, "senha-nova-123"));
        var segunda = await service.RedefinirSenhaAsync(
            new RedefinicaoDeSenhaRequest(emitido.Token, "outra-senha-456"));

        Assert.Equal(FalhaDeAutenticacao.TokenInvalido, segunda.Falha);
        Assert.True((await service.AutenticarComSenhaAsync(
            new LoginLocalRequest("ana@example.com", "senha-nova-123"))).Autenticou);
    }

    [Fact]
    public async Task Redefinicao_TokenVencido_NaoServe()
    {
        var (service, _, clock, _, _) = BuildCompleto();
        await CadastrarConfirmadoAsync(service, Cadastro());
        var emitido = await service.SolicitarRedefinicaoDeSenhaAsync("ana@example.com");

        clock.Advance(PoliticaDeRedefinicaoDeSenha.Validade + TimeSpan.FromMinutes(1));

        var resultado = await service.RedefinirSenhaAsync(
            new RedefinicaoDeSenhaRequest(emitido!.Token, "senha-nova-123"));

        Assert.Equal(FalhaDeAutenticacao.TokenInvalido, resultado.Falha);
        Assert.False(await service.TokenDeRedefinicaoEstaValidoAsync(emitido.Token));
    }

    // Pedir um link novo mata o anterior: dois links vivos dobram a janela de exposição.
    [Fact]
    public async Task Redefinicao_NovoPedidoInvalidaOLinkAnterior()
    {
        var (service, _, _, _, _) = BuildCompleto();
        await CadastrarConfirmadoAsync(service, Cadastro());

        var primeiro = await service.SolicitarRedefinicaoDeSenhaAsync("ana@example.com");
        var segundo = await service.SolicitarRedefinicaoDeSenhaAsync("ana@example.com");

        Assert.False(await service.TokenDeRedefinicaoEstaValidoAsync(primeiro!.Token));
        Assert.True(await service.TokenDeRedefinicaoEstaValidoAsync(segundo!.Token));
    }

    [Fact]
    public async Task Redefinicao_ComSenhaCurta_RecusaESeguraOToken()
    {
        var (service, _, _, _, _) = BuildCompleto();
        await CadastrarConfirmadoAsync(service, Cadastro(senha: "senha-antiga-1"));
        var emitido = await service.SolicitarRedefinicaoDeSenhaAsync("ana@example.com");

        var resultado = await service.RedefinirSenhaAsync(
            new RedefinicaoDeSenhaRequest(emitido!.Token, "curta"));

        Assert.Equal(FalhaDeAutenticacao.SenhaInaceitavel, resultado.Falha);

        // O link continua valendo: errar o tamanho da senha não deve custar um novo e-mail.
        Assert.True(await service.TokenDeRedefinicaoEstaValidoAsync(emitido.Token));
    }

    [Theory]
    [InlineData("token-que-nao-existe")]
    [InlineData("")]
    public async Task Redefinicao_ComTokenDesconhecido_Recusa(string token)
    {
        var (service, _, _, _, _) = BuildCompleto();

        var resultado = await service.RedefinirSenhaAsync(
            new RedefinicaoDeSenhaRequest(token, "senha-nova-123"));

        Assert.Equal(FalhaDeAutenticacao.TokenInvalido, resultado.Falha);
    }

    // Redefinir é prova de que a pessoa controla o e-mail — o dono não deve continuar preso
    // pelas tentativas de quem o atacou.
    [Fact]
    public async Task Redefinicao_LiberaContaBloqueada()
    {
        var (service, repo, clock, _, _) = BuildCompleto();
        await CadastrarConfirmadoAsync(service, Cadastro(senha: "senha-antiga-1"));
        await ErrarSenha(service, PoliticaDeTentativasDeLogin.MaximoDeFalhas);
        Assert.True(repo.Todos[0].EstaBloqueada(clock.UtcNow));

        var emitido = await service.SolicitarRedefinicaoDeSenhaAsync("ana@example.com");
        await service.RedefinirSenhaAsync(new RedefinicaoDeSenhaRequest(emitido!.Token, "senha-nova-123"));

        Assert.False(repo.Todos[0].EstaBloqueada(clock.UtcNow));
        Assert.True((await service.AutenticarComSenhaAsync(
            new LoginLocalRequest("ana@example.com", "senha-nova-123"))).Autenticou);
    }

    // Não emitir token para e-mail sem conta é o que sustenta a resposta única da tela: se
    // emitisse, o envio (ou a falta dele) diria quem está cadastrado.
    [Theory]
    [InlineData("ninguem@example.com")]
    [InlineData("")]
    public async Task Solicitacao_ParaEmailSemContaLocal_NaoEmiteToken(string email)
    {
        var (service, repo, _, _, _) = BuildCompleto();
        await CadastrarConfirmadoAsync(service, Cadastro(email: "ana@example.com"));

        Assert.Null(await service.SolicitarRedefinicaoDeSenhaAsync(email));
        Assert.Empty(repo.Tokens);
    }

    [Fact]
    public async Task Solicitacao_ParaContaSocial_NaoEmiteToken()
    {
        var (service, repo, _, _, _) = BuildCompleto();
        await service.ObterOuCriarAsync(Login(email: "ana@example.com"));

        Assert.Null(await service.SolicitarRedefinicaoDeSenhaAsync("ana@example.com"));
        Assert.Empty(repo.Tokens);
    }

    [Fact]
    public async Task Solicitacao_IgnoraCaixaDoEmail()
    {
        var (service, _, _, _, _) = BuildCompleto();
        await CadastrarConfirmadoAsync(service, Cadastro(email: "ana@example.com"));

        Assert.NotNull(await service.SolicitarRedefinicaoDeSenhaAsync("ANA@Example.com"));
    }

    // ---- Confirmação de e-mail -------------------------------------------

    // O teste que define a funcionalidade: senha certa e conta existente NÃO bastam.
    [Fact]
    public async Task LoginLocal_SemConfirmarOEmail_Recusa()
    {
        var (service, repo, _, _) = BuildComHasher();
        await service.CadastrarComSenhaAsync(Cadastro(senha: "senha-correta-1"));

        var resultado = await service.AutenticarComSenhaAsync(
            new LoginLocalRequest("ana@example.com", "senha-correta-1"));

        Assert.False(resultado.Autenticou);
        Assert.Equal(FalhaDeAutenticacao.EmailNaoConfirmado, resultado.Falha);

        // E não conta como acesso: quem nunca entrou não pode aparecer como usuário ativo.
        Assert.Equal(Agora, repo.Todos[0].LastLoginAt);
    }

    // ⚠️ O teste que protege a ORDEM das checagens. EmailNaoConfirmado é a única falha de login
    // que a tela nomeia, e só é segura porque vem DEPOIS do hash. Se alguém trocar a ordem, o
    // login vira um oráculo de "este e-mail tem cadastro" — e nada mais quebraria para avisar.
    [Fact]
    public async Task LoginLocal_SemConfirmarMasComSenhaErrada_NaoRevelaQueAContaExiste()
    {
        var (service, _, _, _) = BuildComHasher();
        await service.CadastrarComSenhaAsync(Cadastro(senha: "senha-correta-1"));

        var resultado = await service.AutenticarComSenhaAsync(
            new LoginLocalRequest("ana@example.com", "senha-errada-999"));

        // Idêntico ao que um e-mail inexistente devolve.
        Assert.Equal(FalhaDeAutenticacao.CredenciaisInvalidas, resultado.Falha);
    }

    [Fact]
    public async Task Confirmacao_ComTokenValido_LiberaOLogin()
    {
        var (service, repo, _, _) = BuildComHasher();
        var cadastro = await service.CadastrarComSenhaAsync(Cadastro(senha: "senha-correta-1"));

        var resultado = await service.ConfirmarEmailAsync(cadastro.Confirmacao!.Token);

        Assert.Equal(ResultadoDeConfirmacao.Confirmado, resultado);
        Assert.True(repo.Todos[0].EmailConfirmado);
        Assert.True((await service.AutenticarComSenhaAsync(
            new LoginLocalRequest("ana@example.com", "senha-correta-1"))).Autenticou);
    }

    // O que viaja no e-mail não pode estar no banco — mesma regra do token de redefinição.
    [Fact]
    public async Task Confirmacao_GuardaApenasOHashDoToken()
    {
        var (service, repo, _, _, tokens) = BuildCompleto();

        var cadastro = await service.CadastrarComSenhaAsync(Cadastro());

        Assert.Single(repo.Confirmacoes);
        Assert.NotEqual(cadastro.Confirmacao!.Token, repo.Confirmacoes[0].TokenHash);
        Assert.Equal(tokens.Hash(cadastro.Confirmacao.Token), repo.Confirmacoes[0].TokenHash);
    }

    // Clique duplo e varredor de links do provedor de destino abrem a mesma URL duas vezes. O
    // segundo acesso não pode mandar para a tela de erro quem já está pronto para entrar.
    [Fact]
    public async Task Confirmacao_RepetidaEhSucesso_NaoErro()
    {
        var (service, _, _, _) = BuildComHasher();
        var cadastro = await service.CadastrarComSenhaAsync(Cadastro(senha: "senha-correta-1"));
        await service.ConfirmarEmailAsync(cadastro.Confirmacao!.Token);

        var segunda = await service.ConfirmarEmailAsync(cadastro.Confirmacao.Token);

        Assert.Equal(ResultadoDeConfirmacao.JaConfirmado, segunda);
        Assert.True((await service.AutenticarComSenhaAsync(
            new LoginLocalRequest("ana@example.com", "senha-correta-1"))).Autenticou);
    }

    [Fact]
    public async Task Confirmacao_ComTokenVencido_Recusa()
    {
        var (service, repo, clock, _) = BuildComHasher();
        var cadastro = await service.CadastrarComSenhaAsync(Cadastro());

        clock.Advance(PoliticaDeConfirmacaoDeEmail.Validade + TimeSpan.FromMinutes(1));
        var resultado = await service.ConfirmarEmailAsync(cadastro.Confirmacao!.Token);

        Assert.Equal(ResultadoDeConfirmacao.TokenInvalido, resultado);
        Assert.False(repo.Todos[0].EmailConfirmado);
    }

    [Theory]
    [InlineData("token-que-nao-existe")]
    [InlineData("")]
    public async Task Confirmacao_ComTokenDesconhecido_Recusa(string token)
    {
        var (service, _, _, _) = BuildComHasher();

        Assert.Equal(ResultadoDeConfirmacao.TokenInvalido, await service.ConfirmarEmailAsync(token));
    }

    [Fact]
    public async Task Reenvio_EmiteLinkNovoEInvalidaOAnterior()
    {
        var (service, repo, _, _) = BuildComHasher();
        var cadastro = await service.CadastrarComSenhaAsync(Cadastro());

        var reenviado = await service.ReenviarConfirmacaoDeEmailAsync("ana@example.com");

        Assert.NotNull(reenviado);
        Assert.NotEqual(cadastro.Confirmacao!.Token, reenviado!.Token);

        // O link antigo morre: quem abre o e-mail mais velho da caixa não confirma nada.
        Assert.Equal(
            ResultadoDeConfirmacao.TokenInvalido,
            await service.ConfirmarEmailAsync(cadastro.Confirmacao.Token));

        Assert.Equal(ResultadoDeConfirmacao.Confirmado, await service.ConfirmarEmailAsync(reenviado.Token));
        Assert.True(repo.Todos[0].EmailConfirmado);
    }

    // Reenviar para quem já confirmou não tem efeito nenhum — e, mais importante, não pode ter
    // efeito VISÍVEL, senão a tela distinguiria "conta pendente" de "conta pronta".
    [Theory]
    [InlineData("ana@example.com", true)]   // já confirmada
    [InlineData("ninguem@example.com", false)]
    [InlineData("", false)]
    public async Task Reenvio_SemContaPendente_NaoEmiteNada(string email, bool confirmarAntes)
    {
        var (service, repo, _, _) = BuildComHasher();

        if (confirmarAntes)
        {
            await CadastrarConfirmadoAsync(service, Cadastro(email: "ana@example.com"));
        }

        var antes = repo.Confirmacoes.Count;

        Assert.Null(await service.ReenviarConfirmacaoDeEmailAsync(email));
        Assert.Equal(antes, repo.Confirmacoes.Count);
    }

    [Fact]
    public async Task Reenvio_ParaContaSocial_NaoEmiteNada()
    {
        var (service, repo, _, _) = BuildComHasher();
        await service.ObterOuCriarAsync(Login(email: "ana@example.com"));

        Assert.Null(await service.ReenviarConfirmacaoDeEmailAsync("ana@example.com"));
        Assert.Empty(repo.Confirmacoes);
    }

    // Conta social entra sem nunca passar por confirmação: o provedor já verificou o endereço, e
    // o GitHub pode não devolver e-mail nenhum — exigir confirmação ali travaria o login por um
    // dado que jamais vai chegar.
    [Fact]
    public async Task ContaSocial_NasceConfirmada()
    {
        var (service, repo, _, _) = BuildComHasher();

        await service.ObterOuCriarAsync(Login(email: null));

        Assert.True(repo.Todos[0].EmailConfirmado);
    }

    // Quem recebeu o link de redefinição PROVOU controlar a caixa de entrada — que é exatamente
    // a prova que a confirmação pede. Sem isto, quem perdeu o e-mail de confirmação e recuperou
    // a senha continuaria barrado depois de provar a mesma coisa duas vezes.
    [Fact]
    public async Task Redefinicao_TambemConfirmaOEmail()
    {
        var (service, repo, _, _, _) = BuildCompleto();
        await service.CadastrarComSenhaAsync(Cadastro(senha: "senha-antiga-1"));
        Assert.False(repo.Todos[0].EmailConfirmado);

        var emitido = await service.SolicitarRedefinicaoDeSenhaAsync("ana@example.com");
        await service.RedefinirSenhaAsync(new RedefinicaoDeSenhaRequest(emitido!.Token, "senha-nova-123"));

        Assert.True(repo.Todos[0].EmailConfirmado);
        Assert.True((await service.AutenticarComSenhaAsync(
            new LoginLocalRequest("ana@example.com", "senha-nova-123"))).Autenticou);
    }
}
