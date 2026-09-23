using System.Security.Claims;
using PrepHub.Application.Abstractions;
using PrepHub.Application.Autenticacao;
using PrepHub.Application.Contracts;
using PrepHub.Domain.Autenticacao;
using PrepHub.Web.Autenticacao;
using PrepHub.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace PrepHub.Web.Controllers;

[Route("conta")]
public class ContaController : Controller
{
    /// <summary>
    /// Chave sob a qual o provedor escolhido atravessa o fluxo OAuth. É necessária porque
    /// no callback autenticamos contra o COOKIE externo, e o ticket dele se identifica como
    /// "PrepHub.External" — o nome do provedor não sobrevive por ali. AuthenticationProperties,
    /// em compensação, são serializadas no "state" e devolvidas intactas pelo provedor.
    /// </summary>
    private const string ChaveDoProvedor = "prephub:provedor";

    private readonly IAutenticacaoService _autenticacao;
    private readonly IAuthenticationSchemeProvider _schemes;
    private readonly IEnviadorDeEmail _email;
    private readonly ILogger<ContaController> _logger;

    public ContaController(
        IAutenticacaoService autenticacao,
        IAuthenticationSchemeProvider schemes,
        IEnviadorDeEmail email,
        ILogger<ContaController> logger)
    {
        _autenticacao = autenticacao;
        _schemes = schemes;
        _email = email;
        _logger = logger;
    }

    [AllowAnonymous]
    [HttpGet("login")]
    public async Task<IActionResult> Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToLocal(returnUrl);
        }

        return View(new LoginViewModel
        {
            Provedores = await ProvedoresDisponiveisAsync(),
            ReturnUrl = returnUrl
        });
    }

    /// <summary>Login da conta local (e-mail + senha).</summary>
    [AllowAnonymous]
    [HttpPost("entrar-com-senha")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(LimiteDeTentativasSetup.PoliticaDeAutenticacao)]
    public async Task<IActionResult> EntrarComSenha(LoginViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return await ReexibirLoginAsync(model);
        }

        var resultado = await _autenticacao.AutenticarComSenhaAsync(
            new LoginLocalRequest(model.Email!, model.Senha!),
            cancellationToken);

        if (resultado.Falha == FalhaDeAutenticacao.EmailNaoConfirmado)
        {
            // Única falha de login que a tela nomeia. Só é segura porque a senha já foi
            // conferida antes lá no serviço: quem lê esta mensagem não descobriu que a conta
            // existe — ele já sabia a senha dela.
            return await ReexibirLoginAsync(model, emailNaoConfirmado: true);
        }

        if (!resultado.Autenticou)
        {
            // Mensagem única para e-mail inexistente, senha errada e conta que só existe num
            // provedor social — a diferença entre esses casos é o que permitiria descobrir
            // quem tem conta aqui. A dica sobre login social cabe porque vale para todos.
            ModelState.AddModelError(
                string.Empty,
                "E-mail ou senha incorretos. Se você criou sua conta com Google, LinkedIn ou "
                + "GitHub, entre por um desses botões.");

            return await ReexibirLoginAsync(model);
        }

        await EntrarNaAplicacaoAsync(resultado.Usuario!);
        return RedirectToLocal(model.ReturnUrl);
    }

    [AllowAnonymous]
    [HttpGet("cadastrar")]
    public IActionResult Cadastrar(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToLocal(returnUrl);
        }

        return View(new CadastroViewModel { ReturnUrl = returnUrl });
    }

    [AllowAnonymous]
    [HttpPost("cadastrar")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(LimiteDeTentativasSetup.PoliticaDeAutenticacao)]
    public async Task<IActionResult> Cadastrar(CadastroViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var resultado = await _autenticacao.CadastrarComSenhaAsync(
            new CadastroLocalRequest(model.Nome!, model.Email!, model.Senha!),
            cancellationToken);

        if (!resultado.Cadastrou)
        {
            switch (resultado.Falha)
            {
                case FalhaDeAutenticacao.EmailJaCadastrado:
                    // Aqui a existência da conta é inevitável (não há como criar a duplicata),
                    // então mais vale dizer o que fazer do que fingir um erro genérico.
                    ModelState.AddModelError(
                        nameof(model.Email),
                        "Já existe uma conta com este e-mail. Entre com sua senha na tela de login.");
                    break;

                case FalhaDeAutenticacao.SenhaInaceitavel:
                    ModelState.AddModelError(
                        nameof(model.Senha),
                        $"A senha deve ter entre {PoliticaDeSenha.TamanhoMinimo} "
                        + $"e {PoliticaDeSenha.TamanhoMaximo} caracteres.");
                    break;

                default:
                    ModelState.AddModelError(string.Empty, "Não foi possível criar a conta. Revise os dados.");
                    break;
            }

            return View(model);
        }

        // O cadastro NÃO autentica mais. A conta existe, mas não entra até o link ser aberto —
        // é isso que impede um endereço digitado errado (o "gmai.com" no lugar de "gmail.com")
        // de virar conta sem dono alcançável, cujo dono só descobre o problema quando precisa
        // recuperar a senha e o link não chega em lugar nenhum.
        await EnviarConfirmacaoAsync(resultado.Confirmacao!, cancellationToken);

        // Redirect, e não View: um F5 na tela de confirmação não pode reenviar o formulário de
        // cadastro. O e-mail vai na URL só para a frase "enviamos para …".
        return RedirectToAction(nameof(ConfirmeSeuEmail), new { email = resultado.Confirmacao!.Email });
    }

    // ---- Confirmação de e-mail -------------------------------------------

    /// <summary>Tela de espera pós-cadastro. Não decide nada: só explica o que fazer agora.</summary>
    [AllowAnonymous]
    [HttpGet("confirme-seu-email")]
    public IActionResult ConfirmeSeuEmail(string? email = null, bool reenviado = false)
        => View(new ConfirmeSeuEmailViewModel { Email = email, Reenviado = reenviado });

    /// <summary>
    /// Consome o link do e-mail e libera o acesso da conta.
    /// </summary>
    /// <remarks>
    /// É GET, e é o único caminho de mudança de estado do projeto que aceita ser um: link de
    /// e-mail não tem como ser POST sem uma página intermediária com botão, e essa página não
    /// protegeria de nada aqui. O risco real do GET é o varredor de links do provedor de destino
    /// abrir a URL antes da pessoa — e é por isso que confirmar NÃO autentica ninguém. O
    /// varredor, no pior caso, confirma o endereço, que é justamente o desfecho desejado; sessão
    /// nenhuma nasce de um clique que talvez não tenha sido humano. Quem confirma cai no login.
    /// </remarks>
    [AllowAnonymous]
    [HttpGet("confirmar-email")]
    public async Task<IActionResult> ConfirmarEmail(string? token, CancellationToken cancellationToken)
    {
        var resultado = await _autenticacao.ConfirmarEmailAsync(token ?? string.Empty, cancellationToken);

        return resultado switch
        {
            // Já confirmado é sucesso: clique duplo e "abrir de novo" do cliente de e-mail são
            // comuns demais para mandarem a pessoa a uma tela de erro quando já pode entrar.
            ResultadoDeConfirmacao.Confirmado or ResultadoDeConfirmacao.JaConfirmado
                => RedirectToAction(nameof(Login), new { emailConfirmado = true }),

            _ => View("ConfirmacaoInvalida")
        };
    }

    [AllowAnonymous]
    [HttpGet("reenviar-confirmacao")]
    public IActionResult ReenviarConfirmacao(string? email = null)
        => View(new ReenviarConfirmacaoViewModel { Email = email });

    /// <summary>
    /// Emite um link novo. Responde SEMPRE igual — exista conta pendente, já confirmada, social
    /// ou nenhuma. Sem isso a tela responderia a pergunta "quem tem cadastro aqui?", que é a
    /// mesma que <c>/conta/esqueci-senha</c> se recusa a responder.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("reenviar-confirmacao")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(LimiteDeTentativasSetup.PoliticaDeAutenticacao)]
    public async Task<IActionResult> ReenviarConfirmacao(
        ReenviarConfirmacaoViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var emitido = await _autenticacao.ReenviarConfirmacaoDeEmailAsync(model.Email!, cancellationToken);

        if (emitido is not null)
        {
            await EnviarConfirmacaoAsync(emitido, cancellationToken);
        }
        else
        {
            // Registrado porque a tela não distingue os casos — sem esta linha, "pedi o reenvio
            // e não veio nada" não teria onde ser investigado.
            _logger.LogInformation("Reenvio de confirmação pedido para e-mail sem conta local pendente.");
        }

        return View(new ReenviarConfirmacaoViewModel { Email = model.Email, Enviado = true });
    }

    // ---- Esqueci minha senha ---------------------------------------------

    [AllowAnonymous]
    [HttpGet("esqueci-senha")]
    public IActionResult EsqueciSenha() => View(new EsqueciSenhaViewModel());

    /// <summary>
    /// Emite o link de redefinição e o envia por e-mail. Responde SEMPRE a mesma coisa, exista
    /// ou não conta com o e-mail informado — a diferença entre os dois casos transformaria
    /// esta tela num consultor de "quem tem conta no PrepHub".
    /// </summary>
    [AllowAnonymous]
    [HttpPost("esqueci-senha")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(LimiteDeTentativasSetup.PoliticaDeAutenticacao)]
    public async Task<IActionResult> EsqueciSenha(EsqueciSenhaViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var emitido = await _autenticacao.SolicitarRedefinicaoDeSenhaAsync(model.Email!, cancellationToken);

        if (emitido is not null)
        {
            // O link é montado aqui porque só o Web sabe host, esquema e rota. O token em
            // texto existe nesta requisição e no e-mail — nunca no banco.
            var link = Url.Action(
                nameof(RedefinirSenha),
                "Conta",
                new { token = emitido.Token },
                Request.Scheme)!;

            await _email.EnviarAsync(
                emitido.Email,
                "Redefinição de senha · PrepHub",
                MontarEmailDeRedefinicao(emitido.Nome, link),
                cancellationToken);
        }
        else
        {
            // Registrado para não ficar invisível quem tenta e-mail que não existe, já que a
            // tela não distingue os casos.
            _logger.LogInformation("Pedido de redefinição para e-mail sem conta local.");
        }

        return View(new EsqueciSenhaViewModel { Email = model.Email, Enviado = true });
    }

    [AllowAnonymous]
    [HttpGet("redefinir-senha")]
    public async Task<IActionResult> RedefinirSenha(string? token, CancellationToken cancellationToken)
    {
        // Confere o link antes de mostrar o formulário: descobrir que o link venceu só depois
        // de digitar e confirmar a senha nova é trabalho jogado fora.
        var valido = token is not null
                     && await _autenticacao.TokenDeRedefinicaoEstaValidoAsync(token, cancellationToken);

        return View(new RedefinirSenhaViewModel { Token = token, TokenInvalido = !valido });
    }

    [AllowAnonymous]
    [HttpPost("redefinir-senha")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(LimiteDeTentativasSetup.PoliticaDeAutenticacao)]
    public async Task<IActionResult> RedefinirSenha(
        RedefinirSenhaViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var resultado = await _autenticacao.RedefinirSenhaAsync(
            new RedefinicaoDeSenhaRequest(model.Token ?? string.Empty, model.Senha!),
            cancellationToken);

        if (!resultado.Autenticou)
        {
            if (resultado.Falha == FalhaDeAutenticacao.SenhaInaceitavel)
            {
                ModelState.AddModelError(
                    nameof(model.Senha),
                    $"A senha deve ter entre {PoliticaDeSenha.TamanhoMinimo} "
                    + $"e {PoliticaDeSenha.TamanhoMaximo} caracteres.");

                return View(model);
            }

            // Token inválido: esconde o formulário em vez de deixar a pessoa tentar de novo
            // com o mesmo link morto.
            return View(new RedefinirSenhaViewModel { TokenInvalido = true });
        }

        // Não autentica automaticamente, ao contrário do cadastro: quem trocou a senha pode
        // estar fazendo isso porque suspeita de invasão, e a sessão antiga (em outro
        // dispositivo) continua valendo — o cookie não tem selo de segurança para invalidar.
        // Mandar para o login deixa claro que a senha nova é o que vale a partir daqui.
        return RedirectToAction(nameof(Login), new { senhaAlterada = true });
    }

    /// <summary>
    /// Dispara o fluxo OAuth. É POST com antiforgery de propósito: um GET permitiria que outro
    /// site iniciasse login por nós (login CSRF), prendendo a pessoa numa conta que não é dela.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("entrar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Entrar(string provider, string? returnUrl = null)
    {
        // Só aceita esquema que realmente registramos — nada de challenge arbitrário.
        if (AutenticacaoSetup.ProvedorDoEsquema(provider) is null
            || await _schemes.GetSchemeAsync(provider) is null)
        {
            return BadRequest();
        }

        var properties = new AuthenticationProperties
        {
            RedirectUri = Url.Action(nameof(Callback), new { returnUrl })
        };

        // Carimba quem originou o login para que o callback saiba de onde a identidade veio.
        properties.Items[ChaveDoProvedor] = provider;

        return Challenge(properties, provider);
    }

    [AllowAnonymous]
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(string? returnUrl = null, CancellationToken cancellationToken = default)
    {
        var result = await HttpContext.AuthenticateAsync(EsquemasDeAutenticacao.Externo);
        if (!result.Succeeded || result.Principal is null)
        {
            // A tela mostra só "tente novamente" de propósito — mas sem registrar a causa
            // real aqui ("Correlation failed", escopo negado, code expirado) qualquer
            // problema de OAuth vira adivinhação.
            _logger.LogWarning(
                result.Failure,
                "Callback de login externo falhou antes de identificar o provedor.");
            return RedirectToAction(nameof(Login), new { erro = true });
        }

        // Lê o carimbo deixado no Entrar. Não dá para usar result.Ticket.AuthenticationScheme:
        // o ticket é o do cookie externo, não o do provedor.
        string? scheme = null;
        result.Properties?.Items.TryGetValue(ChaveDoProvedor, out scheme);

        var provider = scheme is null ? null : AutenticacaoSetup.ProvedorDoEsquema(scheme);
        var providerKey = result.Principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (provider is null || string.IsNullOrWhiteSpace(providerKey))
        {
            // Autenticou no provedor mas veio sem identidade utilizável — normalmente
            // esquema inesperado ou claim NameIdentifier ausente.
            _logger.LogWarning(
                "Login externo sem identidade utilizável. Esquema: {Esquema}, temChave: {TemChave}.",
                scheme ?? "(ausente)",
                !string.IsNullOrWhiteSpace(providerKey));
            return RedirectToAction(nameof(Login), new { erro = true });
        }

        var usuario = await _autenticacao.ObterOuCriarAsync(
            new LoginExternoRequest(
                provider.Value,
                providerKey,
                NomeDe(result.Principal),
                result.Principal.FindFirstValue(ClaimTypes.Email),
                AvatarDe(result.Principal)),
            cancellationToken);

        await EntrarNaAplicacaoAsync(usuario);

        // O cookie externo já cumpriu seu papel — não deve sobreviver ao login.
        await HttpContext.SignOutAsync(EsquemasDeAutenticacao.Externo);

        return RedirectToLocal(returnUrl);
    }

    [HttpPost("sair")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Sair()
    {
        await HttpContext.SignOutAsync(EsquemasDeAutenticacao.Aplicacao);
        return RedirectToAction(nameof(Login));
    }

    // ---- Auxiliares ------------------------------------------------------

    /// <summary>
    /// Devolve a tela de login preservando o que foi digitado. Os provedores precisam ser
    /// recarregados porque vêm do servidor, não do formulário — sem isso os botões sociais
    /// desapareceriam justamente na tentativa que falhou.
    /// </summary>
    private async Task<IActionResult> ReexibirLoginAsync(LoginViewModel model, bool emailNaoConfirmado = false)
    {
        var recarregado = new LoginViewModel
        {
            Provedores = await ProvedoresDisponiveisAsync(),
            ReturnUrl = model.ReturnUrl,
            Email = model.Email,
            EmailNaoConfirmado = emailNaoConfirmado
            // Senha fica de fora: campo de senha não se repopula.
        };

        return View(nameof(Login), recarregado);
    }

    /// <summary>
    /// Monta o link de confirmação e o despacha. O token em texto existe nesta requisição e no
    /// e-mail — no banco ficou só o hash.
    /// </summary>
    private Task EnviarConfirmacaoAsync(TokenDeConfirmacaoDto emitido, CancellationToken cancellationToken)
    {
        var link = Url.Action(
            nameof(ConfirmarEmail),
            "Conta",
            new { token = emitido.Token },
            Request.Scheme)!;

        return _email.EnviarAsync(
            emitido.Email,
            "Confirme seu e-mail · PrepHub",
            MontarEmailDeConfirmacao(emitido.Nome, link),
            cancellationToken);
    }

    /// <summary>
    /// Corpo do e-mail de confirmação. Texto puro, pelas mesmas razões da redefinição: passa em
    /// qualquer cliente, não depende de imagem remota e não parece phishing por causa de HTML
    /// enfeitado — o que importa mais ainda aqui, que é a PRIMEIRA mensagem que a pessoa recebe
    /// de um remetente que ela ainda não conhece.
    /// </summary>
    private static string MontarEmailDeConfirmacao(string nome, string link)
    {
        var horas = (int)PoliticaDeConfirmacaoDeEmail.Validade.TotalHours;

        return $"""
            Olá, {nome}.

            Sua conta no PrepHub foi criada. Falta um passo: confirme que este endereço é seu
            abrindo o link abaixo.

            {link}

            O link vale por {horas} horas. Enquanto ele não for aberto, a conta não entra.

            Se não foi você que se cadastrou, ignore esta mensagem — sem a confirmação, a conta
            não é ativada.

            PrepHub
            """;
    }

    /// <summary>
    /// Corpo do e-mail de redefinição. Texto puro: passa em qualquer cliente, não depende de
    /// imagem remota e não parece phishing por causa de HTML enfeitado.
    /// </summary>
    private static string MontarEmailDeRedefinicao(string nome, string link)
    {
        var horas = (int)PoliticaDeRedefinicaoDeSenha.Validade.TotalHours;

        return $"""
            Olá, {nome}.

            Recebemos um pedido para redefinir a senha da sua conta no PrepHub.
            Para criar uma senha nova, abra o endereço abaixo:

            {link}

            O link vale por {horas} hora(s) e só pode ser usado uma vez.

            Se não foi você que pediu, ignore esta mensagem — sua senha atual continua valendo.

            PrepHub
            """;
    }

    private Task EntrarNaAplicacaoAsync(UsuarioDto usuario)
        => HttpContext.SignInAsync(EsquemasDeAutenticacao.Aplicacao, ConstruirPrincipal(usuario));

    private async Task<IReadOnlyList<string>> ProvedoresDisponiveisAsync()
    {
        var todos = await _schemes.GetAllSchemesAsync();
        return todos
            .Select(s => s.Name)
            .Where(name => AutenticacaoSetup.ProvedorDoEsquema(name) is not null)
            .ToList();
    }

    // O GitHub pode não expor nome real; nesse caso o login (handle) é o melhor rótulo.
    private static string NomeDe(ClaimsPrincipal principal)
        => principal.FindFirstValue(ClaimTypes.Name)
           ?? principal.FindFirstValue(ClaimTypes.GivenName)
           ?? principal.Identity?.Name
           ?? "Candidato";

    // Cada provedor nomeia a foto de um jeito; pegamos a primeira que existir.
    private static string? AvatarDe(ClaimsPrincipal principal)
        => principal.FindFirstValue("picture")
           ?? principal.FindFirstValue("urn:github:avatar")
           ?? principal.FindFirstValue("avatar_url");

    private static ClaimsPrincipal ConstruirPrincipal(UsuarioDto usuario)
    {
        // NameIdentifier passa a ser o Id LOCAL: daqui pra frente o app não usa mais a
        // identidade do provedor, e IUsuarioAtual lê exatamente esta claim.
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
            new(ClaimTypes.Name, usuario.Name),
            new("provider", usuario.Provider.ToString())
        };

        if (!string.IsNullOrWhiteSpace(usuario.Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, usuario.Email));
        }

        if (!string.IsNullOrWhiteSpace(usuario.AvatarUrl))
        {
            claims.Add(new Claim("avatar", usuario.AvatarUrl));
        }

        var identity = new ClaimsIdentity(claims, EsquemasDeAutenticacao.Aplicacao);
        return new ClaimsPrincipal(identity);
    }

    // Só redireciona para dentro da aplicação — bloqueia open redirect via ?returnUrl=.
    private IActionResult RedirectToLocal(string? returnUrl)
        => !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? Redirect(returnUrl)
            : RedirectToAction("Index", "Home");
}
