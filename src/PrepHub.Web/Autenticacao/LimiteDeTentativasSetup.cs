using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace PrepHub.Web.Autenticacao;

/// <summary>
/// Limite de requisições por IP nos endpoints de autenticação.
/// </summary>
/// <remarks>
/// <para>
/// É a primeira das duas camadas contra força bruta. Esta segura o caso comum — um cliente
/// tentando muitas senhas, ou muitas contas — e vale para quem nem tem conta aqui, então
/// protege também o cadastro e o pedido de redefinição (que custam PBKDF2 e envio de e-mail).
/// A segunda camada é o bloqueio por conta em <c>PoliticaDeTentativasDeLogin</c>, que cobre o
/// que esta não cobre: ataque distribuído em que cada IP fica dentro da cota.
/// </para>
/// <para>
/// ⚠️ O contador é <b>em memória</b>, por instância. Combina com o SQLite de hoje (instância
/// única); em deploy multi-instância cada réplica passa a ter sua própria cota, e o limite
/// efetivo se multiplica pelo número de réplicas. É o mesmo gatilho que move o banco para
/// PostgreSQL: aí o limitador precisa de armazenamento compartilhado.
/// </para>
/// <para>
/// Chave por IP e não por sessão: cookie e sessão são escolhidos por quem ataca, então
/// limitar por eles não limita nada. Sem IP (o que não deve acontecer em HTTP real) todas as
/// requisições caem num balde único, que é o lado seguro do erro.
/// </para>
/// </remarks>
public static class LimiteDeTentativasSetup
{
    /// <summary>Nome da política aplicada com <c>[EnableRateLimiting]</c> nas ações.</summary>
    public const string PoliticaDeAutenticacao = "autenticacao";

    /// <summary>Tentativas por IP dentro da janela.</summary>
    private const int LimiteDaJanela = 10;

    private static readonly TimeSpan Janela = TimeSpan.FromMinutes(5);

    public static IServiceCollection AddLimiteDeTentativas(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.AddPolicy(PoliticaDeAutenticacao, contexto =>
            {
                var chave = contexto.Connection.RemoteIpAddress?.ToString() ?? "sem-ip";

                // Janela fixa: previsível de explicar ("10 tentativas a cada 5 minutos") e
                // barata. Sem fila (QueueLimit 0) porque enfileirar tentativa de login só
                // atrasaria a resposta em vez de recusá-la.
                return RateLimitPartition.GetFixedWindowLimiter(chave, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = LimiteDaJanela,
                    Window = Janela,
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
            });

            options.OnRejected = (contexto, _) =>
            {
                var resposta = contexto.HttpContext.Response;

                // Retry-After é o cabeçalho padrão para isso: cliente honesto (e nossa própria
                // tela) sabe quanto esperar sem ficar tentando.
                if (contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    resposta.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(NumberFormatInfo.InvariantInfo);
                }

                // Estes endpoints são formulários de navegador, não API: devolver um corpo 429
                // cru trocaria a tela da pessoa por uma página de erro sem contexto. O redirect
                // leva de volta ao login com o aviso, mantendo a tela reconhecível.
                resposta.Redirect("/conta/login?limite=1");

                contexto.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger(typeof(LimiteDeTentativasSetup))
                    .LogWarning(
                        "Limite de tentativas de autenticação atingido por {Ip} em {Caminho}.",
                        contexto.HttpContext.Connection.RemoteIpAddress,
                        contexto.HttpContext.Request.Path);

                return ValueTask.CompletedTask;
            };
        });

        return services;
    }
}
