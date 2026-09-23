namespace PrepHub.Infrastructure.Observabilidade;

/// <summary>Seção <c>Observabilidade</c> da configuração.</summary>
public sealed class OpcoesDeObservabilidade
{
    public const string Secao = "Observabilidade";

    /// <summary>
    /// Porta em que <c>/metrics</c> é servido. O padrão é uma porta SEPARADA da aplicação
    /// (9464, a convencionada pelo OpenTelemetry) e que o compose não publica no host: assim o
    /// Prometheus alcança o endpoint pela rede interna do Docker e ninguém de fora alcança.
    /// </summary>
    /// <remarks>
    /// A porta precisa estar entre as que o Kestrel escuta — no container isso vem de
    /// <c>ASPNETCORE_HTTP_PORTS=8080;9464</c>. Se não estiver, a aplicação avisa no log em vez de
    /// deixar o endpoint calado. <c>0</c> desliga o isolamento e serve <c>/metrics</c> na porta
    /// normal: é o que <c>appsettings.Development.json</c> faz, porque em desenvolvimento não há
    /// segunda porta nem rede interna, e o que se quer é abrir a URL no navegador.
    /// </remarks>
    public int PortaDeMetricas { get; init; } = 9464;

    /// <summary>
    /// De quanto em quanto tempo os números que vêm do banco (usuários cadastrados, provas em
    /// andamento) são recontados.
    /// </summary>
    /// <remarks>
    /// Esses valores não podem ser lidos na hora do scrape: a leitura do medidor é síncrona e
    /// bloquearia o Prometheus numa consulta ao SQLite. Recontar em segundo plano desacopla as
    /// duas coisas — o preço é o painel enxergar o banco com até um intervalo de atraso, que para
    /// "quantas contas existem" é irrelevante.
    /// </remarks>
    public TimeSpan IntervaloDeColeta { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Sem porta dedicada, <c>/metrics</c> responde na mesma porta da aplicação.</summary>
    public bool ServeNaPortaDaAplicacao => PortaDeMetricas <= 0;
}
