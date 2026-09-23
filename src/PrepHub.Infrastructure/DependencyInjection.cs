using System.Globalization;
using PrepHub.Application.Abstractions;
using PrepHub.Infrastructure.Aleatoriedade;
using PrepHub.Infrastructure.Doacao;
using PrepHub.Infrastructure.Email;
using PrepHub.Infrastructure.Observabilidade;
using PrepHub.Infrastructure.Persistence;
using PrepHub.Infrastructure.Persistence.Repositories;
using PrepHub.Infrastructure.Seguranca;
using PrepHub.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PrepHub.Infrastructure;

/// <summary>Registra o acesso a dados (EF Core + SQLite) e os serviços de infraestrutura.</summary>
public static class DependencyInjection
{
    public const string ConnectionStringName = "Default";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
                               ?? "Data Source=App_Data/prephub.db";

        services.AddDbContext<PrepHubDbContext>(options => options.UseSqlite(connectionString));

        // O mesmo DbContext (scoped) cumpre o papel de Unit of Work.
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<PrepHubDbContext>());

        services.AddScoped<IExameRepository, ExameRepository>();
        services.AddScoped<ITentativaDeProvaRepository, TentativaDeProvaRepository>();
        services.AddScoped<IUsuarioRepository, UsuarioRepository>();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IGeradorDeAleatoriedade, GeradorDeAleatoriedade>();

        // Sem estado entre chamadas (salt é sorteado a cada hash), então singleton basta.
        services.AddSingleton<IHasherDeSenha, HasherDeSenhaPbkdf2>();
        services.AddSingleton<IGeradorDeTokenSeguro, GeradorDeTokenSeguro>();

        AdicionarEnvioDeEmail(services, configuration);
        AdicionarDoacao(services, configuration);
        AdicionarObservabilidade(services, configuration);

        return services;
    }

    /// <summary>
    /// Registra o medidor de negócio e o coletor que reconta o banco. O que NÃO está aqui é o
    /// exportador: transformar isso em <c>/metrics</c> é papel do Web, que é quem tem pipeline
    /// HTTP — a Infrastructure não deve saber que Prometheus existe.
    /// </summary>
    private static void AdicionarObservabilidade(IServiceCollection services, IConfiguration configuration)
    {
        var secao = configuration.GetSection(OpcoesDeObservabilidade.Secao);
        var opcoes = new OpcoesDeObservabilidade
        {
            PortaDeMetricas = int.TryParse(secao["PortaDeMetricas"], out var porta) ? porta : 9464,

            // Piso de 5s: um intervalo minúsculo (ou zero, digitado por engano) transformaria o
            // coletor num laço de consultas ao SQLite disputando o banco com quem está em prova.
            IntervaloDeColeta = int.TryParse(secao["IntervaloDeColetaSegundos"], out var segundos) && segundos > 0
                ? TimeSpan.FromSeconds(Math.Max(5, segundos))
                : TimeSpan.FromSeconds(30)
        };

        services.AddSingleton(opcoes);

        // Singleton obrigatório: é o Meter que o OpenTelemetry assina uma única vez no startup.
        services.AddSingleton<MetricasDoPrepHub>();
        services.AddSingleton<IMetricasDeNegocio>(sp => sp.GetRequiredService<MetricasDoPrepHub>());

        // Scoped porque depende do DbContext, que é scoped — o coletor abre um escopo por volta.
        services.AddScoped<LeitorDoRetratoDoBanco>();
        services.AddHostedService<ColetorDeMetricasDoBanco>();
    }

    /// <summary>
    /// Lê a seção <c>Doacao</c> e registra o gerador de QR. As opções são registradas sempre,
    /// configuradas ou não: quem decide se a página de apoio existe é
    /// <see cref="OpcoesDeDoacao.EstaConfigurado"/>, consultado pelo Web em um lugar só.
    /// </summary>
    private static void AdicionarDoacao(IServiceCollection services, IConfiguration configuration)
    {
        var secao = configuration.GetSection(OpcoesDeDoacao.Secao);
        var opcoes = new OpcoesDeDoacao
        {
            ChavePix = secao["ChavePix"],
            NomeDoRecebedor = secao["NomeDoRecebedor"] ?? "PrepHub",
            Cidade = secao["Cidade"] ?? "SAO PAULO",
            CustoMensal = secao["CustoMensal"],
            LinkExterno = secao["LinkExterno"],
            LinkExternoRotulo = secao["LinkExternoRotulo"] ?? "Doar de fora do Brasil"
        };

        // Lista vem como "Doacao:ValoresSugeridos:0", "…:1" — o valor inválido é ignorado em vez
        // de derrubar a aplicação no startup por causa de um botão de doação.
        var valores = secao.GetSection("ValoresSugeridos").GetChildren()
            .Select(item => decimal.TryParse(item.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : 0m)
            .Where(v => v > 0m)
            .ToArray();

        if (valores.Length > 0)
        {
            opcoes.ValoresSugeridos = valores;
        }

        services.AddSingleton(opcoes);

        // Sem estado por chamada e com cache interno: singleton é o registro certo.
        services.AddSingleton<IGeradorDeQrCode, GeradorDeQrCodeSvg>();
    }

    /// <summary>
    /// Registra o envio real só se houver SMTP configurado; sem isso, o enviador de log. Mesma
    /// disciplina dos provedores OAuth: a app sobe e o fluxo de "esqueci minha senha" funciona
    /// em desenvolvimento sem servidor de e-mail, com o link aparecendo no console.
    /// </summary>
    private static void AdicionarEnvioDeEmail(IServiceCollection services, IConfiguration configuration)
    {
        // Leitura chave a chave, como já é feito com as credenciais OAuth no Web: evita trazer
        // o pacote de binder só por isso e deixa explícito o que a seção aceita.
        var secao = configuration.GetSection(OpcoesDeEmail.Secao);
        var opcoes = new OpcoesDeEmail
        {
            SmtpHost = secao["SmtpHost"],
            SmtpPort = int.TryParse(secao["SmtpPort"], out var porta) ? porta : 587,
            UsarSsl = !bool.TryParse(secao["UsarSsl"], out var ssl) || ssl,
            Usuario = secao["Usuario"],
            Senha = secao["Senha"],
            RemetenteEndereco = secao["RemetenteEndereco"] ?? "nao-responda@prephub.local",
            RemetenteNome = secao["RemetenteNome"] ?? "PrepHub"
        };

        services.AddSingleton(opcoes);

        if (opcoes.EstaConfigurado)
        {
            services.AddSingleton<IEnviadorDeEmail, EnviadorDeEmailSmtp>();
        }
        else
        {
            // O motivo é calculado aqui, onde a configuração está à mão, e viaja até a mensagem
            // de log — assim quem for buscar o link no console já lê qual chave está faltando.
            var motivo = opcoes.MotivoDeNaoEnviar();
            services.AddSingleton<IEnviadorDeEmail>(sp => new EnviadorDeEmailParaLog(
                sp.GetRequiredService<ILogger<EnviadorDeEmailParaLog>>(),
                motivo));
        }
    }
}
