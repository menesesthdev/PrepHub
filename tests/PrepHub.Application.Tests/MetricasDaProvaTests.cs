using PrepHub.Application.Abstractions;
using PrepHub.Application.Sessoes;
using PrepHub.Application.Tests.Fakes;
using PrepHub.Domain.Entidades;
using PrepHub.Domain.Enums;

namespace PrepHub.Application.Tests;

/// <summary>
/// O que a sessão de prova publica nas métricas.
/// </summary>
/// <remarks>
/// O caso que justifica a suíte inteira é o encerramento por tempo esgotado. Ele acontece num
/// caminho separado do "Encerrar prova" — o servidor fecha a tentativa sozinho quando alguém
/// reabre a página depois do prazo — e é fácil instrumentar só o clique. Se isso acontecesse, o
/// painel perderia justamente as provas de quem não terminou a tempo, e o número restante pareceria
/// correto: só as pessoas mais rápidas apareceriam, empurrando a taxa de aprovação para cima sem
/// nenhum sinal de que faltava metade dos dados.
/// </remarks>
public class MetricasDaProvaTests
{
    private static readonly DateTime Iniciar = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private const int LimiteEmMinutos = 45;

    private sealed record Cenario(
        SessaoDeProvaService Sessao,
        Exame Exame,
        FixedClock Clock,
        FakeMetricasDeNegocio Metricas);

    private static Cenario Montar()
    {
        var exame = MontarExame();
        var clock = new FixedClock(Iniciar);
        var exames = new InMemoryExamRepository(exame);
        var tentativas = new InMemoryExamAttemptRepository();
        var metricas = new FakeMetricasDeNegocio();

        return new Cenario(
            new SessaoDeProvaService(
                exames,
                tentativas,
                new FakeSorteadorDeQuestoes(exames),
                new FakeUnitOfWork(),
                clock,
                new FakeUsuarioAtual(),
                metricas),
            exame,
            clock,
            metricas);
    }

    private static Exame MontarExame()
    {
        var exame = new Exame("AZ-900", "Azure Fundamentals", LimiteEmMinutos, passingScorePercent: 70, totalQuestions: 2);
        var area = exame.AdicionarAreaDeHabilidade("conceitos-de-nuvem", "Conceitos de nuvem", 100m);

        foreach (var i in Enumerable.Range(0, 2))
        {
            var questao = exame.AdicionarQuestao(area.Id, $"questao-{i}", $"Enunciado {i}", TipoDeQuestao.EscolhaUnica, "Explicação.");
            questao.AdicionarOpcao("Correta", true, 0);
            questao.AdicionarOpcao("Errada", false, 1);
        }

        return exame;
    }

    [Fact]
    public async Task IniciarTentativa_RegistraOCodigoDoExame()
    {
        var c = Montar();

        await c.Sessao.IniciarTentativaAsync(c.Exame.Id);

        Assert.Equal(["AZ-900"], c.Metricas.ProvasIniciadas);
        Assert.Empty(c.Metricas.ProvasConcluidas);
    }

    [Fact]
    public async Task EncerramentoManual_RegistraNotaDuracaoEMotivo()
    {
        var c = Montar();
        var tentativa = await c.Sessao.IniciarTentativaAsync(c.Exame.Id);

        c.Clock.Advance(TimeSpan.FromMinutes(20));
        await c.Sessao.FinalizarTentativaAsync(tentativa);

        var registro = Assert.Single(c.Metricas.ProvasConcluidas);
        Assert.Equal("AZ-900", registro.Exame);
        Assert.Equal(MotivoDeEncerramento.Manual, registro.Motivo);
        Assert.Equal(TimeSpan.FromMinutes(20), registro.Duracao);

        // Prova em branco: reprovada, e a nota é a da escala 1–1000, nunca o percentual.
        Assert.False(registro.Aprovado);
        Assert.InRange(registro.Nota, 1, 1000);
    }

    [Fact]
    public async Task EncerramentoPorTempo_RegistraComoTempoEsgotado()
    {
        var c = Montar();
        var tentativa = await c.Sessao.IniciarTentativaAsync(c.Exame.Id);

        // Volta só depois do prazo: quem fecha a tentativa é o servidor, não um clique.
        c.Clock.Advance(TimeSpan.FromMinutes(LimiteEmMinutos + 30));
        await c.Sessao.ObterEstadoAsync(tentativa);

        var registro = Assert.Single(c.Metricas.ProvasConcluidas);
        Assert.Equal(MotivoDeEncerramento.TempoEsgotado, registro.Motivo);

        // A duração é o limite da prova, não o tempo até a pessoa reabrir a página — senão uma
        // prova largada por dias entraria no histograma como uma prova de dias.
        Assert.Equal(TimeSpan.FromMinutes(LimiteEmMinutos), registro.Duracao);
    }

    /// <summary>
    /// Encerrar é idempotente (o primeiro fechamento vale), e a métrica tem de acompanhar: contar
    /// de novo a cada visita ao score report multiplicaria as provas por quantas vezes alguém
    /// reabriu a própria nota.
    /// </summary>
    [Fact]
    public async Task ReabrirOResultado_NaoContaAProvaDeNovo()
    {
        var c = Montar();
        var tentativa = await c.Sessao.IniciarTentativaAsync(c.Exame.Id);

        await c.Sessao.FinalizarTentativaAsync(tentativa);
        await c.Sessao.FinalizarTentativaAsync(tentativa);
        await c.Sessao.ObterResultadoAsync(tentativa);

        Assert.Single(c.Metricas.ProvasConcluidas);
    }

    [Fact]
    public async Task ProvaDeOutroUsuario_NaoRegistraNada()
    {
        var c = Montar();

        var resultado = await c.Sessao.FinalizarTentativaAsync(Guid.NewGuid());

        Assert.Null(resultado);
        Assert.Empty(c.Metricas.ProvasConcluidas);
    }
}
