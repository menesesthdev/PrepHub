using PrepHub.Application.Abstractions;
using PrepHub.Domain.Enums;
using PrepHub.Infrastructure.Observabilidade;
using PrepHub.Infrastructure.Time;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

namespace PrepHub.Infrastructure.Tests;

/// <summary>
/// O que sai dos instrumentos: nomes, rótulos e valores.
/// </summary>
/// <remarks>
/// <para>
/// Nome de métrica e valor de rótulo são <b>contrato com os dashboards</b>, e um contrato que o
/// compilador não vê. Renomear um membro de enum ou trocar o nome de um instrumento compila,
/// sobe e passa em todo o resto da suíte — o que quebra é um painel do Grafana, dias depois,
/// mostrando uma linha reta que ninguém sabe dizer se é falta de movimento ou falta de dado.
/// </para>
/// <para>
/// Por isso os valores esperados aqui estão escritos como literais, e não derivados do enum: um
/// teste que calculasse o rótulo com a mesma regra da produção acompanharia a mudança em silêncio
/// e não protegeria nada.
/// </para>
/// </remarks>
public sealed class MetricasDoPrepHubTests : IDisposable
{
    private readonly MetricasDoPrepHub _metricas = new(new SystemClock());

    public void Dispose() => _metricas.Dispose();

    private MetricCollector<T> Coletar<T>(string instrumento) where T : struct
        => new(_metricas, MetricasDoPrepHub.NomeDoMeter, instrumento);

    [Fact]
    public void ContaCriada_UsaONomeEOsRotulosEsperados()
    {
        using var coletor = Coletar<long>("prephub.contas.criadas");

        _metricas.ContaCriada(ProvedorDeLogin.Google);
        _metricas.ContaCriada(ProvedorDeLogin.Local);

        var medidas = coletor.GetMeasurementSnapshot();
        Assert.Equal(2, medidas.Count);
        Assert.Equal(1, medidas[0].Value);
        Assert.Equal("google", medidas[0].Tags["provedor"]);
        Assert.Equal("local", medidas[1].Tags["provedor"]);
    }

    /// <summary>
    /// Marca é palavra só: <c>LinkedIn</c> é <c>linkedin</c>, não <c>linked_in</c>. A regra geral
    /// de rótulo separa por maiúscula do meio (para <c>SenhaIncorreta</c> virar
    /// <c>senha_incorreta</c>), e sem a exceção o provedor sairia partido ao meio.
    /// </summary>
    [Theory]
    [InlineData(ProvedorDeLogin.LinkedIn, "linkedin")]
    [InlineData(ProvedorDeLogin.GitHub, "github")]
    [InlineData(ProvedorDeLogin.Google, "google")]
    [InlineData(ProvedorDeLogin.Local, "local")]
    public void ProvedorNaoEPartidoAoMeio(ProvedorDeLogin provedor, string esperado)
    {
        using var coletor = Coletar<long>("prephub.contas.criadas");

        _metricas.ContaCriada(provedor);

        Assert.Equal(esperado, Assert.Single(coletor.GetMeasurementSnapshot()).Tags["provedor"]);
    }

    [Theory]
    [InlineData(ResultadoDeLogin.Sucesso, "sucesso")]
    [InlineData(ResultadoDeLogin.ContaInexistente, "conta_inexistente")]
    [InlineData(ResultadoDeLogin.SenhaIncorreta, "senha_incorreta")]
    [InlineData(ResultadoDeLogin.ContaBloqueada, "conta_bloqueada")]
    [InlineData(ResultadoDeLogin.PedidoInvalido, "pedido_invalido")]
    [InlineData(ResultadoDeLogin.EmailNaoConfirmado, "email_nao_confirmado")]
    public void DesfechoDoLogin_ViraRotuloEmSnakeCase(ResultadoDeLogin resultado, string esperado)
    {
        using var coletor = Coletar<long>("prephub.logins");

        _metricas.LoginRegistrado(ProvedorDeLogin.Local, resultado);

        var medida = Assert.Single(coletor.GetMeasurementSnapshot());
        Assert.Equal(esperado, medida.Tags["resultado"]);
        Assert.Equal("local", medida.Tags["provedor"]);
    }

    [Theory]
    [InlineData(EtapaDeConfirmacaoDeEmail.LinkEmitido, "link_emitido")]
    [InlineData(EtapaDeConfirmacaoDeEmail.Concluida, "concluida")]
    [InlineData(EtapaDeConfirmacaoDeEmail.ReenvioSolicitado, "reenvio_solicitado")]
    [InlineData(EtapaDeConfirmacaoDeEmail.TokenRecusado, "token_recusado")]
    public void EtapaDaConfirmacao_ViraRotuloEmSnakeCase(EtapaDeConfirmacaoDeEmail etapa, string esperado)
    {
        using var coletor = Coletar<long>("prephub.confirmacoes.email");

        _metricas.ConfirmacaoDeEmail(etapa);

        Assert.Equal(esperado, Assert.Single(coletor.GetMeasurementSnapshot()).Tags["etapa"]);
    }

    [Fact]
    public void ProvaConcluida_AlimentaContador_Nota_EDuracao()
    {
        using var contador = Coletar<long>("prephub.provas.concluidas");
        using var notas = Coletar<int>("prephub.provas.nota");
        using var duracoes = Coletar<double>("prephub.provas.duracao");

        _metricas.ProvaConcluida("AZ-900", aprovado: true, notaEscalada: 812, TimeSpan.FromMinutes(31), MotivoDeEncerramento.TempoEsgotado);

        var registro = Assert.Single(contador.GetMeasurementSnapshot());
        Assert.Equal("AZ-900", registro.Tags["exame"]);
        Assert.Equal("aprovado", registro.Tags["resultado"]);
        Assert.Equal("tempo_esgotado", registro.Tags["motivo"]);

        Assert.Equal(812, Assert.Single(notas.GetMeasurementSnapshot()).Value);
        Assert.Equal(1860d, Assert.Single(duracoes.GetMeasurementSnapshot()).Value);
    }

    /// <summary>
    /// Relógio ajustado para trás entre o início e o fim da prova produz duração negativa. Num
    /// histograma isso não é só um ponto estranho: a soma acumulada fica errada para sempre, sem
    /// jeito de remover a amostra depois.
    /// </summary>
    [Fact]
    public void DuracaoNegativa_EGravadaComoZero()
    {
        using var duracoes = Coletar<double>("prephub.provas.duracao");

        _metricas.ProvaConcluida("AZ-900", aprovado: false, notaEscalada: 300, TimeSpan.FromMinutes(-5), MotivoDeEncerramento.Manual);

        Assert.Equal(0d, Assert.Single(duracoes.GetMeasurementSnapshot()).Value);
    }

    /// <summary>
    /// Antes da primeira coleta os medidores não publicam NADA. Publicar zero seria pior do que
    /// ficar calado: o painel diria "nenhuma conta cadastrada" com toda a confiança, quando a
    /// resposta certa é "ainda não sei".
    /// </summary>
    [Fact]
    public void SemRetrato_OsMedidoresNaoPublicamNada()
    {
        using var cadastrados = Coletar<long>("prephub.usuarios.cadastrados");
        using var emAndamento = Coletar<long>("prephub.provas.em_andamento");

        cadastrados.RecordObservableInstruments();
        emAndamento.RecordObservableInstruments();

        Assert.Empty(cadastrados.GetMeasurementSnapshot());
        Assert.Empty(emAndamento.GetMeasurementSnapshot());
    }

    [Fact]
    public void ComRetrato_OsMedidoresPublicamUmaSeriePorDimensao()
    {
        using var cadastrados = Coletar<long>("prephub.usuarios.cadastrados");
        using var ativos = Coletar<long>("prephub.usuarios.ativos");
        using var realizadas = Coletar<long>("prephub.provas.realizadas");
        using var emAndamento = Coletar<long>("prephub.provas.em_andamento");

        _metricas.AtualizarRetrato(new RetratoDoBanco(
            DateTime.UtcNow,
            [new ContagemPorProvedor(ProvedorDeLogin.Local, 7), new ContagemPorProvedor(ProvedorDeLogin.GitHub, 3)],
            [new ContagemPorJanela("24h", 2), new ContagemPorJanela("7d", 5), new ContagemPorJanela("30d", 9)],
            ProvasEmAndamento: 4,
            [new ContagemDeProvasRealizadas("AZ-900", Aprovado: true, 11), new ContagemDeProvasRealizadas("AZ-900", Aprovado: false, 6)]));

        cadastrados.RecordObservableInstruments();
        ativos.RecordObservableInstruments();
        realizadas.RecordObservableInstruments();
        emAndamento.RecordObservableInstruments();

        var porProvedor = cadastrados.GetMeasurementSnapshot()
            .ToDictionary(m => (string)m.Tags["provedor"]!, m => m.Value);
        Assert.Equal(7, porProvedor["local"]);
        Assert.Equal(3, porProvedor["github"]);

        var porJanela = ativos.GetMeasurementSnapshot()
            .ToDictionary(m => (string)m.Tags["janela"]!, m => m.Value);
        Assert.Equal(3, porJanela.Count);
        Assert.Equal(2, porJanela["24h"]);
        Assert.Equal(5, porJanela["7d"]);
        Assert.Equal(9, porJanela["30d"]);

        Assert.Equal(4, Assert.Single(emAndamento.GetMeasurementSnapshot()).Value);

        var aprovadas = realizadas.GetMeasurementSnapshot().Single(m => (string)m.Tags["resultado"]! == "aprovado");
        Assert.Equal(11, aprovadas.Value);
        Assert.Equal("AZ-900", aprovadas.Tags["exame"]);
    }

    /// <summary>
    /// A idade do retrato é o que denuncia coletor parado — sem ela, medidores congelados
    /// continuariam mostrando o último valor conhecido com cara de valor atual.
    /// </summary>
    [Fact]
    public void IdadeDoRetrato_CresceComOTempo()
    {
        var relogio = new RelogioControlavel(new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc));
        using var metricas = new MetricasDoPrepHub(relogio);
        using var coletor = new MetricCollector<double>(metricas, MetricasDoPrepHub.NomeDoMeter, "prephub.coleta.idade");

        metricas.AtualizarRetrato(new RetratoDoBanco(relogio.UtcNow, [], [], 0, []));
        relogio.UtcNow = relogio.UtcNow.AddSeconds(90);

        coletor.RecordObservableInstruments();

        Assert.Equal(90d, Assert.Single(coletor.GetMeasurementSnapshot()).Value);
    }

    private sealed class RelogioControlavel(DateTime inicio) : IClock
    {
        public DateTime UtcNow { get; set; } = inicio;
    }
}
