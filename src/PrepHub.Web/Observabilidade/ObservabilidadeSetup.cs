using PrepHub.Infrastructure.Observabilidade;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace PrepHub.Web.Observabilidade;

/// <summary>
/// Monta a coleta de métricas e expõe <c>/metrics</c> no formato que o Prometheus lê.
/// </summary>
/// <remarks>
/// <para>
/// A escolha de fundo é OpenTelemetry sobre a API de métricas da própria BCL, e não uma biblioteca
/// que fale Prometheus direto: os instrumentos ficam sendo <c>System.Diagnostics.Metrics</c>, que é
/// o que o ASP.NET Core e o runtime .NET já emitem por conta própria. Isso dá de graça latência,
/// throughput e erro por rota, além de GC e memória, sem escrever uma linha — e deixa o Prometheus
/// como um detalhe do exportador, trocável por OTLP mudando só este arquivo.
/// </para>
/// </remarks>
public static class ObservabilidadeSetup
{
    /// <summary>
    /// Faixas do histograma de notas. Concentradas em volta de 700 porque é ali que a distribuição
    /// tem significado: a diferença entre 650 e 750 é aprovar ou não, e a diferença entre 100 e 200
    /// não é nada. As faixas padrão do OpenTelemetry (0, 5, 10, 25…) foram desenhadas para
    /// milissegundos e deixariam quase toda a distribuição num balde só.
    /// </summary>
    private static readonly double[] FaixasDeNota = [300, 400, 500, 600, 650, 700, 750, 800, 900, 1000];

    /// <summary>
    /// Faixas de duração da prova, em segundos. O limite do AZ-900 é de 45 minutos (2700s), então
    /// as faixas se adensam perto do fim — é lá que mora a informação: quem termina no estouro do
    /// tempo é um perfil diferente de quem entrega em vinte minutos.
    /// </summary>
    private static readonly double[] FaixasDeDuracao = [60, 300, 600, 900, 1200, 1500, 1800, 2100, 2400, 2700];

    public static IServiceCollection AddObservabilidade(this IServiceCollection services)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(recurso => recurso.AddService(
                serviceName: "prephub",
                serviceVersion: typeof(ObservabilidadeSetup).Assembly.GetName().Version?.ToString() ?? "0.0.0"))
            .WithMetrics(metricas => metricas
                // As métricas de produto. O nome tem de casar com o Meter da Infrastructure —
                // errar aqui não quebra nada, só apaga todos os painéis de negócio em silêncio.
                .AddMeter(MetricasDoPrepHub.NomeDoMeter)

                // Requisições, latência e status por rota — vem do próprio ASP.NET Core.
                // O scrape do /metrics não entra na conta; quem o exclui é DisableHttpMetrics,
                // aplicado no próprio endpoint mais abaixo.
                .AddAspNetCoreInstrumentation()

                // GC, heap, threads, exceções: o que responde "por que está lento" quando o
                // gráfico de latência sobe.
                .AddRuntimeInstrumentation()

                .AddView("prephub.provas.nota", new ExplicitBucketHistogramConfiguration { Boundaries = FaixasDeNota })
                .AddView("prephub.provas.duracao", new ExplicitBucketHistogramConfiguration { Boundaries = FaixasDeDuracao })

                .AddPrometheusExporter());

        return services;
    }

    /// <summary>
    /// Publica <c>/metrics</c> — por padrão numa porta separada da aplicação, que o compose não
    /// expõe no host.
    /// </summary>
    /// <remarks>
    /// <para>
    /// O endpoint é anônimo por obrigação (o Prometheus não faz login) e o que ele devolve não é
    /// inócuo: quantas contas existem, quantas provas rodaram, quais rotas recebem tráfego. Deixá-lo
    /// aberto na mesma porta do site publicaria o painel inteiro para qualquer visitante.
    /// </para>
    /// <para>
    /// A defesa é de rede, não de senha: só a porta 8080 é publicada, então a 9464 existe apenas
    /// dentro da rede do Docker, onde só os outros contêineres chegam. Um token seria pior —
    /// o arquivo de configuração do Prometheus não interpola variável de ambiente, e o segredo
    /// acabaria versionado em texto para não quebrar o <c>docker compose up</c>.
    /// </para>
    /// <para>
    /// ⚠️ O isolamento vale enquanto a porta não for publicada. Quem colocar um proxy reverso na
    /// frente precisa mantê-la fora dele — é o mesmo cuidado já registrado para
    /// <c>UseForwardedHeaders</c>.
    /// </para>
    /// </remarks>
    public static void MapMetricas(this WebApplication app)
    {
        var opcoes = app.Services.GetRequiredService<OpcoesDeObservabilidade>();

        var endpoint = app.MapPrometheusScrapingEndpoint();
        endpoint.AllowAnonymous();

        // O scrape não conta como tráfego do site. Sem isso, a cada 15 segundos apareceria uma
        // requisição que ninguém fez — e num projeto com pouco movimento /metrics seria a rota
        // MAIS acessada do painel, a coleta virando o assunto principal do gráfico que deveria
        // falar do produto.
        endpoint.DisableHttpMetrics();

        if (!opcoes.ServeNaPortaDaAplicacao)
        {
            // RequireHost compara com o cabeçalho Host da requisição; "*:9464" aceita qualquer
            // nome desde que a porta seja a de métricas. Um GET /metrics chegando na 8080 não casa
            // com endpoint nenhum e cai na política padrão (que exige autenticação): a resposta é
            // um redirect para o login, byte a byte igual ao de qualquer caminho inexistente —
            // nem os números vazam, nem se confirma que o endpoint existe em algum lugar.
            endpoint.RequireHost($"*:{opcoes.PortaDeMetricas}");
        }

        AvisarSeAPortaDeMetricasNaoEstiverEscutando(app, opcoes);
    }

    /// <summary>
    /// Confere, depois de o servidor subir, se a porta de métricas está entre as que o Kestrel
    /// realmente abriu.
    /// </summary>
    /// <remarks>
    /// Sem esta checagem a falha é muda: quem esquecer de incluir a porta em
    /// <c>ASPNETCORE_HTTP_PORTS</c> tem uma aplicação que sobe perfeitamente, um Prometheus que
    /// marca o alvo como "down" e um Grafana vazio, sem nenhuma pista de onde olhar.
    /// </remarks>
    private static void AvisarSeAPortaDeMetricasNaoEstiverEscutando(WebApplication app, OpcoesDeObservabilidade opcoes)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("PrepHub.Observabilidade");

        app.Lifetime.ApplicationStarted.Register(() =>
        {
            if (opcoes.ServeNaPortaDaAplicacao)
            {
                logger.LogInformation(
                    "Métricas em /metrics na mesma porta da aplicação (Observabilidade:PortaDeMetricas = 0). "
                    + "Sem isolamento de porta — apropriado para desenvolvimento, não para o que estiver exposto na internet.");
                return;
            }

            var enderecos = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()?.Addresses
                ?? (ICollection<string>)Array.Empty<string>();

            var escutando = enderecos.Any(endereco =>
                Uri.TryCreate(endereco, UriKind.Absolute, out var uri) && uri.Port == opcoes.PortaDeMetricas);

            if (escutando)
            {
                logger.LogInformation(
                    "Métricas em /metrics na porta {Porta}, separada da aplicação.",
                    opcoes.PortaDeMetricas);
            }
            else
            {
                logger.LogWarning(
                    "Observabilidade:PortaDeMetricas = {Porta}, mas o servidor não escuta nessa porta ({Enderecos}). "
                    + "/metrics não vai responder em lugar nenhum e o Prometheus vai marcar o alvo como fora do ar. "
                    + "Inclua a porta em ASPNETCORE_HTTP_PORTS (ex.: 8080;{Porta}) ou defina PortaDeMetricas como 0 "
                    + "para servir na porta da aplicação.",
                    opcoes.PortaDeMetricas,
                    string.Join(", ", enderecos),
                    opcoes.PortaDeMetricas);
            }
        });
    }
}
