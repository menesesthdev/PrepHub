using AspNet.Security.OAuth.GitHub;
using AspNet.Security.OAuth.LinkedIn;
using PrepHub.Application.Abstractions;
using PrepHub.Domain.Enums;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;

namespace PrepHub.Web.Autenticacao;

/// <summary>Configura o login social (sem senha local) e a política que exige autenticação.</summary>
public static class AutenticacaoSetup
{
    /// <summary>Mapeia o nome do esquema para o provedor do domínio.</summary>
    public static ProvedorDeLogin? ProvedorDoEsquema(string scheme) => scheme switch
    {
        GoogleDefaults.AuthenticationScheme => ProvedorDeLogin.Google,
        GitHubAuthenticationDefaults.AuthenticationScheme => ProvedorDeLogin.GitHub,
        LinkedInAuthenticationDefaults.AuthenticationScheme => ProvedorDeLogin.LinkedIn,
        _ => null
    };

    public static IServiceCollection AddAutenticacaoSocial(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IUsuarioAtual, UsuarioAtual>();

        var auth = services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = EsquemasDeAutenticacao.Aplicacao;
                options.DefaultChallengeScheme = EsquemasDeAutenticacao.Aplicacao;
            })
            .AddCookie(EsquemasDeAutenticacao.Aplicacao, options =>
            {
                options.LoginPath = "/conta/login";
                options.LogoutPath = "/conta/sair";
                options.AccessDeniedPath = "/conta/login";
                options.ExpireTimeSpan = TimeSpan.FromDays(30);
                options.SlidingExpiration = true;
                options.Cookie.Name = "prephub.auth";
                options.Cookie.HttpOnly = true;
                // Lax (e não Strict) porque o retorno do provedor OAuth é uma navegação
                // vinda de outro site — com Strict o cookie não acompanharia o callback.
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            })
            // Cookie efêmero: só vive entre o callback do provedor e a criação da sessão.
            .AddCookie(EsquemasDeAutenticacao.Externo, options =>
            {
                options.Cookie.Name = "prephub.external";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
            });

        // Cada provedor só é registrado se tiver credencial configurada — assim a app sobe
        // e a tela de login funciona mesmo com apenas um deles cadastrado.
        var google = configuration.GetSection("Authentication:Google");
        if (Configurado(google))
        {
            auth.AddGoogle(options =>
            {
                options.ClientId = google["ClientId"]!;
                options.ClientSecret = google["ClientSecret"]!;
                AplicarComum(options);
            });
        }

        var github = configuration.GetSection("Authentication:GitHub");
        if (Configurado(github))
        {
            auth.AddGitHub(options =>
            {
                options.ClientId = github["ClientId"]!;
                options.ClientSecret = github["ClientSecret"]!;
                AplicarComum(options);
                // Sem este escopo o GitHub não devolve e-mail algum.
                options.Scope.Add("user:email");
            });
        }

        var linkedIn = configuration.GetSection("Authentication:LinkedIn");
        if (Configurado(linkedIn))
        {
            // O provedor usa o "Sign In with LinkedIn using OpenID Connect" (escopos
            // openid/profile/email, userinfo em /v2/userinfo) — é preciso habilitar esse
            // produto no app do LinkedIn, senão o callback volta com "unauthorized_scope".
            auth.AddLinkedIn(options =>
            {
                options.ClientId = linkedIn["ClientId"]!;
                options.ClientSecret = linkedIn["ClientSecret"]!;
                AplicarComum(options);
            });
        }

        // Tudo exige login por padrão; o que é público leva [AllowAnonymous] explícito.
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());

        return services;
    }

    /// <summary>
    /// Ajustes que valem para todo provedor externo — ficam num só lugar porque um deles
    /// divergir dos outros é exatamente o tipo de bug que só aparece em runtime.
    /// </summary>
    private static void AplicarComum(RemoteAuthenticationOptions options)
    {
        options.SignInScheme = EsquemasDeAutenticacao.Externo;

        // O padrão do framework para o cookie de correlação é SameSite=None, que o browser
        // só aceita acompanhado de Secure — e Secure só vale sobre HTTPS. Rodando em
        // http://localhost o cookie é descartado silenciosamente e TODO callback falha com
        // "Correlation failed", sem pista nenhuma na tela.
        //
        // Lax resolve porque não exige Secure e ainda acompanha o retorno do provedor: o
        // callback é uma navegação de topo por GET, que Lax permite. É o mesmo raciocínio
        // já aplicado ao cookie da aplicação, logo acima.
        //
        // ⚠️ Se algum provedor passar a responder com response_mode=form_post (callback por
        // POST), Lax deixa de enviar o cookie — aí a saída é voltar para None e exigir HTTPS.
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;

        // O default é Always, que marca o cookie como Secure mesmo sobre http:// — e aí o
        // browser descarta de novo, pelo mesmo motivo. SameAsRequest mantém Secure em
        // produção (HTTPS) sem quebrar o desenvolvimento local, e é a mesma política já
        // usada nos dois cookies de sessão acima.
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    }

    private static bool Configurado(IConfigurationSection section)
        => !string.IsNullOrWhiteSpace(section["ClientId"])
           && !string.IsNullOrWhiteSpace(section["ClientSecret"]);
}
