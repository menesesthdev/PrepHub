using PrepHub.Application.Contracts;
using PrepHub.Application.Historico;
using PrepHub.Application.Observabilidade;
using PrepHub.Application.Sessoes;
using PrepHub.Application.Tests.Fakes;
using PrepHub.Domain.Correcao;
using PrepHub.Domain.Entidades;
using PrepHub.Domain.Enums;

namespace PrepHub.Application.Tests;

public class HistoricoDeProvasServiceTests
{
    private static readonly DateTime Inicio = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    // Exame de 4 questões single-choice, corte em 70% — o mesmo formato dos testes de sessão.
    private static Exame BuildExam()
    {
        var exam = new Exame("AZ-900", "Azure Fundamentals", timeLimitMinutes: 45, passingScorePercent: 70, totalQuestions: 4);
        var area = exam.AdicionarAreaDeHabilidade("conceitos-de-nuvem", "Conceitos de nuvem", 100m);

        foreach (var i in Enumerable.Range(0, 4))
        {
            var q = exam.AdicionarQuestao(area.Id, $"questao-{i}", $"Questão {i}", TipoDeQuestao.EscolhaUnica, "Explicação.");
            q.AdicionarOpcao("Correta", true, 0);
            q.AdicionarOpcao("Errada", false, 1);
        }

        return exam;
    }

    /// <summary>
    /// Sessão e histórico compartilham repositório, relógio e usuário atual — é assim que o
    /// teste pode fazer uma prova de verdade e depois conferir o que o histórico mostra dela.
    /// </summary>
    private sealed record Cenario(
        SessaoDeProvaService Sessao,
        HistoricoDeProvasService Historico,
        Exame Exame,
        FixedClock Clock,
        FakeUsuarioAtual Usuario);

    private static Cenario Montar()
    {
        var exam = BuildExam();
        var clock = new FixedClock(Inicio);
        var usuario = new FakeUsuarioAtual();
        var exames = new InMemoryExamRepository(exam);
        var tentativas = new InMemoryExamAttemptRepository();

        return new Cenario(
            new SessaoDeProvaService(
                exames,
                tentativas,
                new FakeSorteadorDeQuestoes(exames),
                new FakeUnitOfWork(),
                clock,
                usuario,
                MetricasDeNegocioSilenciosas.Instancia),
            new HistoricoDeProvasService(tentativas, exames, usuario),
            exam,
            clock,
            usuario);
    }

    /// <summary>Faz uma tentativa completa acertando <paramref name="acertos"/> das 4 questões.</summary>
    private static async Task<Guid> Realizar(Cenario c, int acertos)
    {
        var attemptId = await c.Sessao.IniciarTentativaAsync(c.Exame.Id);

        for (var numero = 1; numero <= 4; numero++)
        {
            var questao = await c.Sessao.ObterQuestaoAsync(attemptId, numero);
            // Pelo texto, não pela posição: a ordem das alternativas é embaralhada por tentativa.
            var opcao = questao!.Options.Single(o => o.Text == (numero <= acertos ? "Correta" : "Errada")).Id;

            await c.Sessao.SalvarRespostaAsync(
                new SalvarRespostaRequest(attemptId, questao.Id, [opcao], false, 0));
        }

        await c.Sessao.FinalizarTentativaAsync(attemptId);
        return attemptId;
    }

    [Fact]
    public async Task SemTentativas_DevolveHistoricoVazioESemDesempenho()
    {
        var c = Montar();

        var historico = await c.Historico.ObterHistoricoAsync();

        Assert.Empty(historico.EmAndamento);
        Assert.Empty(historico.Concluidas);
        // Null, e não zeros: "ainda não há nota" é diferente de "melhor nota = 0".
        Assert.Null(historico.Desempenho);
    }

    [Fact]
    public async Task TentativaConcluida_ApareceComNotaEscaladaEVeredito()
    {
        var c = Montar();
        await Realizar(c, acertos: 4);

        var historico = await c.Historico.ObterHistoricoAsync();

        var item = Assert.Single(historico.Concluidas);
        Assert.True(item.Concluida);
        Assert.Equal("AZ-900", item.ExamCode);
        Assert.Equal(4, item.QuestoesRespondidas);
        Assert.Equal(4, item.TotalDeQuestoes);
        // 100% de acertos com corte em 70% = topo da escala.
        Assert.Equal(EscalaDeNota.NotaMaxima, item.NotaEscalada);
        Assert.True(item.Aprovado);
    }

    // Nota reprovada tem de ficar abaixo do corte: é o invariante que garante que nota exibida e
    // veredito nunca se contradigam na tela.
    [Fact]
    public async Task TentativaReprovada_TemNotaAbaixoDoCorte()
    {
        var c = Montar();
        await Realizar(c, acertos: 1);

        var item = Assert.Single((await c.Historico.ObterHistoricoAsync()).Concluidas);

        Assert.False(item.Aprovado);
        Assert.True(item.NotaEscalada < EscalaDeNota.NotaDeCorte);
    }

    [Fact]
    public async Task TentativaAberta_VaiParaEmAndamentoComProgressoESemNota()
    {
        var c = Montar();
        var attemptId = await c.Sessao.IniciarTentativaAsync(c.Exame.Id);
        var questao = await c.Sessao.ObterQuestaoAsync(attemptId, 1);
        await c.Sessao.SalvarRespostaAsync(
            new SalvarRespostaRequest(attemptId, questao!.Id, [questao.Options.Single(o => o.Text == "Correta").Id], false, 0));

        var historico = await c.Historico.ObterHistoricoAsync();

        Assert.Empty(historico.Concluidas);
        var item = Assert.Single(historico.EmAndamento);
        Assert.False(item.Concluida);
        Assert.Equal(1, item.QuestoesRespondidas);
        // Sem correção não há nota: converter placar parcial daria um número sem significado.
        Assert.Null(item.NotaEscalada);
        Assert.Null(item.Aprovado);
        Assert.Null(item.Duracao);
    }

    // Item marcado para revisão mas sem alternativa escolhida NÃO conta como respondido — é a
    // mesma regra de "Incompleto" da tela de revisão da prova.
    [Fact]
    public async Task QuestaoApenasMarcada_NaoContaComoRespondida()
    {
        var c = Montar();
        var attemptId = await c.Sessao.IniciarTentativaAsync(c.Exame.Id);
        var questao = await c.Sessao.ObterQuestaoAsync(attemptId, 1);

        await c.Sessao.SalvarRespostaAsync(
            new SalvarRespostaRequest(attemptId, questao!.Id, [], true, 0));

        var item = Assert.Single((await c.Historico.ObterHistoricoAsync()).EmAndamento);
        Assert.Equal(0, item.QuestoesRespondidas);
    }

    [Fact]
    public async Task Historico_ListaMaisRecentePrimeiro()
    {
        var c = Montar();
        await Realizar(c, acertos: 1);

        c.Clock.Advance(TimeSpan.FromDays(1));
        await Realizar(c, acertos: 4);

        var concluidas = (await c.Historico.ObterHistoricoAsync()).Concluidas;

        Assert.Equal(2, concluidas.Count);
        Assert.Equal(Inicio.AddDays(1), concluidas[0].StartedAt);
        Assert.Equal(Inicio, concluidas[1].StartedAt);
    }

    [Fact]
    public async Task Desempenho_ConsolidaMelhorNotaAprovacoesEVariacao()
    {
        var c = Montar();
        await Realizar(c, acertos: 4);   // aprovado, nota máxima

        c.Clock.Advance(TimeSpan.FromDays(1));
        await Realizar(c, acertos: 1);   // reprovado, nota baixa — e é a mais recente

        var d = (await c.Historico.ObterHistoricoAsync()).Desempenho;

        Assert.NotNull(d);
        Assert.Equal(2, d!.TotalConcluidas);
        Assert.Equal(1, d.Aprovacoes);
        Assert.Equal(EscalaDeNota.NotaMaxima, d.MelhorNota);
        Assert.True(d.NotaMaisRecente < EscalaDeNota.NotaDeCorte);
        // Piorou: a variação é negativa e vale exatamente a diferença entre as duas.
        Assert.Equal(d.NotaMaisRecente - EscalaDeNota.NotaMaxima, d.VariacaoDesdeAAnterior);
    }

    [Fact]
    public async Task Desempenho_ComUmaSoTentativa_NaoTemVariacao()
    {
        var c = Montar();
        await Realizar(c, acertos: 4);

        var d = (await c.Historico.ObterHistoricoAsync()).Desempenho;

        Assert.Equal(1, d!.TotalConcluidas);
        Assert.Null(d.VariacaoDesdeAAnterior);
    }

    // O ponto central do gerenciamento de usuários: o histórico é por conta. Trocar o usuário
    // logado tem de trocar completamente o que se vê.
    [Fact]
    public async Task Historico_NaoVazaTentativaDeOutroUsuario()
    {
        var c = Montar();
        await Realizar(c, acertos: 4);

        c.Usuario.Id = Guid.NewGuid();  // outra pessoa na mesma sessão
        var deOutro = await c.Historico.ObterHistoricoAsync();

        Assert.Empty(deOutro.Concluidas);
        Assert.Empty(deOutro.EmAndamento);
        Assert.Null(deOutro.Desempenho);
    }

    // Sessão anônima não deve estourar exceção: a política de autorização já barra antes, e
    // lista vazia é degradação melhor que erro 500 se alguma tela nova esquecer o [Authorize].
    [Fact]
    public async Task SemUsuarioLogado_DevolveVazio()
    {
        var c = Montar();
        await Realizar(c, acertos: 4);

        c.Usuario.Id = null;

        var historico = await c.Historico.ObterHistoricoAsync();
        Assert.Empty(historico.Concluidas);
        Assert.Null(historico.Desempenho);
    }
}
