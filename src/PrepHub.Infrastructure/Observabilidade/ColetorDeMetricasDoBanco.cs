using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PrepHub.Infrastructure.Observabilidade;

/// <summary>
/// Reconta periodicamente o que só o banco sabe e entrega o resultado ao
/// <see cref="MetricasDoPrepHub"/>.
/// </summary>
/// <remarks>
/// <para>
/// Existe porque a leitura de um medidor observável é síncrona e acontece dentro do scrape: ler o
/// SQLite ali dentro deixaria o Prometheus esperando por I/O e, num banco travado, fazendo a
/// coleta inteira expirar. Aqui a consulta acontece fora do caminho do scrape, e o pior caso é o
/// painel ficar um intervalo desatualizado.
/// </para>
/// <para>
/// O escopo é aberto a cada volta porque o <c>DbContext</c> é scoped e um serviço em segundo plano
/// é singleton — guardar o contexto num campo daria o clássico contexto eterno, acumulando todas
/// as entidades já materializadas até o processo morrer.
/// </para>
/// </remarks>
public sealed class ColetorDeMetricasDoBanco : BackgroundService
{
    private readonly IServiceScopeFactory _escopos;
    private readonly MetricasDoPrepHub _metricas;
    private readonly OpcoesDeObservabilidade _opcoes;
    private readonly ILogger<ColetorDeMetricasDoBanco> _logger;

    public ColetorDeMetricasDoBanco(
        IServiceScopeFactory escopos,
        MetricasDoPrepHub metricas,
        OpcoesDeObservabilidade opcoes,
        ILogger<ColetorDeMetricasDoBanco> logger)
    {
        _escopos = escopos;
        _metricas = metricas;
        _opcoes = opcoes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var relogio = new PeriodicTimer(_opcoes.IntervaloDeColeta);

        // A primeira coleta é imediata: esperar um intervalo inteiro deixaria os painéis vazios
        // logo depois de um deploy, justamente quando alguém está olhando para eles.
        do
        {
            await ColetarAsync(stoppingToken);
        }
        while (await EsperarProximaVoltaAsync(relogio, stoppingToken));
    }

    private async Task ColetarAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var escopo = _escopos.CreateScope();
            var leitor = escopo.ServiceProvider.GetRequiredService<LeitorDoRetratoDoBanco>();

            _metricas.AtualizarRetrato(await leitor.LerAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Desligamento em curso — não é falha.
        }
        catch (Exception excecao)
        {
            // Engolida de propósito: a partir do .NET 6 uma exceção que escapa de um
            // BackgroundService derruba o processo inteiro. Derrubar a aplicação porque a
            // CONTAGEM de usuários falhou seria trocar um painel defasado por um site fora do ar.
            // Quem denuncia a defasagem é o medidor prephub.coleta.idade, que continua subindo.
            _logger.LogError(excecao, "Falha ao coletar as métricas do banco. Os medidores ficam com o último valor conhecido.");
        }
    }

    private static async Task<bool> EsperarProximaVoltaAsync(PeriodicTimer relogio, CancellationToken cancellationToken)
    {
        try
        {
            return await relogio.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
