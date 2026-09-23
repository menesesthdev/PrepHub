using PrepHub.Application.Abstractions;
using PrepHub.Application.Contracts;
using PrepHub.Application.Observabilidade;
using PrepHub.Application.Sessoes;
using PrepHub.Application.Tests.Fakes;
using PrepHub.Domain.Entidades;
using PrepHub.Domain.Enums;

namespace PrepHub.Application.Tests;

public class SessaoDeProvaServiceTests
{
    private static readonly DateTime Iniciar = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    // Exame de 4 questões single-choice em 1 skill area; corte em 70%.
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

    private static (SessaoDeProvaService service, Exame exam, FixedClock clock) BuildService()
    {
        var (service, exam, clock, _) = BuildServiceComUsuario();
        return (service, exam, clock);
    }

    private static (SessaoDeProvaService service, Exame exam, FixedClock clock, FakeUsuarioAtual usuario) BuildServiceComUsuario()
    {
        var (service, exam, clock, usuario, _) = BuildServiceCompleto();
        return (service, exam, clock, usuario);
    }

    /// <summary>
    /// Igual aos outros, mas devolve também o repositório de tentativas: os testes de validação
    /// precisam afirmar que uma resposta recusada não deixou linha nenhuma para trás, e isso não
    /// aparece em nenhum DTO (o estado só enumera a composição sorteada).
    /// </summary>
    /// <param name="metricas">
    /// Opcional porque quase nenhum teste daqui se importa com instrumentação — o padrão
    /// silencioso evita que todos tenham de montar um coletor só para ignorá-lo. Quem afirma
    /// sobre métrica passa o próprio.
    /// </param>
    private static (SessaoDeProvaService service, Exame exam, FixedClock clock, FakeUsuarioAtual usuario, InMemoryExamAttemptRepository tentativas) BuildServiceCompleto(
        Exame? exame = null,
        IMetricasDeNegocio? metricas = null)
    {
        var exam = exame ?? BuildExam();
        var clock = new FixedClock(Iniciar);
        var usuario = new FakeUsuarioAtual();
        var exames = new InMemoryExamRepository(exam);
        var tentativas = new InMemoryExamAttemptRepository();
        var service = new SessaoDeProvaService(
            exames,
            tentativas,
            new FakeSorteadorDeQuestoes(exames),
            new FakeUnitOfWork(),
            clock,
            usuario,
            metricas ?? MetricasDeNegocioSilenciosas.Instancia);
        return (service, exam, clock, usuario, tentativas);
    }

    // Pelo TEXTO, nunca pela posição: a ordem das alternativas é embaralhada por tentativa
    // (OrdemDasOpcoes), então "a primeira é a correta" deixou de valer — inclusive na tela.
    private static Guid CorrectOption(QuestaoDto q) => q.Options.Single(o => o.Text == "Correta").Id;

    private static Guid WrongOption(QuestaoDto q) => q.Options.Single(o => o.Text == "Errada").Id;

    [Fact]
    public async Task StartAttempt_CreatesAttemptWithFreshState()
    {
        var (service, exam, _) = BuildService();

        var attemptId = await service.IniciarTentativaAsync(exam.Id);
        var state = await service.ObterEstadoAsync(attemptId);

        Assert.NotNull(state);
        Assert.Equal("AZ-900", state!.ExamCode);
        Assert.Equal(4, state.Questions.Count);
        Assert.All(state.Questions, q => Assert.False(q.IsAnswered));
        Assert.Equal(45 * 60, state.RemainingSeconds);
    }

    [Fact]
    public async Task GetQuestion_OutOfRange_ReturnsNull()
    {
        var (service, exam, _) = BuildService();
        var attemptId = await service.IniciarTentativaAsync(exam.Id);

        Assert.Null(await service.ObterQuestaoAsync(attemptId, 0));
        Assert.Null(await service.ObterQuestaoAsync(attemptId, 5));
        Assert.NotNull(await service.ObterQuestaoAsync(attemptId, 1));
    }

    [Fact]
    public async Task SaveAnswer_MarksQuestionAnsweredInState()
    {
        var (service, exam, _) = BuildService();
        var attemptId = await service.IniciarTentativaAsync(exam.Id);
        var q1 = await service.ObterQuestaoAsync(attemptId, 1);

        await service.SalvarRespostaAsync(new SalvarRespostaRequest(
            attemptId, q1!.Id, new[] { CorrectOption(q1) }, IsFlaggedForReview: true, TimeSpentSeconds: 30));

        var state = await service.ObterEstadoAsync(attemptId);
        var status = state!.Questions.Single(s => s.QuestionId == q1.Id);
        Assert.True(status.IsAnswered);
        Assert.True(status.IsFlaggedForReview);
    }

    [Fact]
    public async Task RemainingSeconds_DecreasesAsTimePasses()
    {
        var (service, exam, clock) = BuildService();
        var attemptId = await service.IniciarTentativaAsync(exam.Id);

        clock.Advance(TimeSpan.FromMinutes(10));

        var state = await service.ObterEstadoAsync(attemptId);
        Assert.Equal(35 * 60, state!.RemainingSeconds);
    }

    [Fact]
    public async Task FinishAttempt_AllCorrect_PassesAndBuildsReview()
    {
        var (service, exam, _) = BuildService();
        var attemptId = await service.IniciarTentativaAsync(exam.Id);

        for (var n = 1; n <= 4; n++)
        {
            var q = await service.ObterQuestaoAsync(attemptId, n);
            await service.SalvarRespostaAsync(new SalvarRespostaRequest(
                attemptId, q!.Id, new[] { CorrectOption(q) }, false, 10));
        }

        var result = await service.FinalizarTentativaAsync(attemptId);

        Assert.NotNull(result);
        Assert.Equal(100m, result!.ScorePercent);
        Assert.True(result.Passed);
        Assert.Equal(4, result.CorrectAnswers);
        Assert.Equal(4, result.Questions.Count);
        Assert.All(result.Questions, q => Assert.True(q.WasCorrect));
        Assert.Single(result.SkillAreas);
    }

    [Fact]
    public async Task FinishAttempt_HalfCorrect_Fails()
    {
        var (service, exam, _) = BuildService();
        var attemptId = await service.IniciarTentativaAsync(exam.Id);

        for (var n = 1; n <= 4; n++)
        {
            var q = await service.ObterQuestaoAsync(attemptId, n);
            // Alterna correta/errada: 2 certas, 2 erradas => 50%.
            var optionId = n % 2 == 1 ? CorrectOption(q!) : WrongOption(q!);
            await service.SalvarRespostaAsync(new SalvarRespostaRequest(attemptId, q!.Id, new[] { optionId }, false, 10));
        }

        var result = await service.FinalizarTentativaAsync(attemptId);

        Assert.Equal(50m, result!.ScorePercent);
        Assert.False(result.Passed);
    }

    [Fact]
    public async Task SaveAnswer_AfterFinish_IsIgnored()
    {
        var (service, exam, _) = BuildService();
        var attemptId = await service.IniciarTentativaAsync(exam.Id);
        var q1 = await service.ObterQuestaoAsync(attemptId, 1);

        await service.FinalizarTentativaAsync(attemptId);

        // Não deve lançar nem alterar nada após finalizado.
        await service.SalvarRespostaAsync(new SalvarRespostaRequest(
            attemptId, q1!.Id, new[] { CorrectOption(q1) }, false, 10));

        var result = await service.ObterResultadoAsync(attemptId);
        Assert.NotNull(result);
        Assert.Equal(0m, result!.ScorePercent); // nenhuma resposta contou
    }

    [Fact]
    public async Task GetResult_WhileInProgress_ReturnsNull()
    {
        var (service, exam, _) = BuildService();
        var attemptId = await service.IniciarTentativaAsync(exam.Id);

        Assert.Null(await service.ObterResultadoAsync(attemptId));
    }

    [Fact]
    public async Task FinishAttempt_IsIdempotent()
    {
        var (service, exam, clock) = BuildService();
        var attemptId = await service.IniciarTentativaAsync(exam.Id);

        var first = await service.FinalizarTentativaAsync(attemptId);
        var finishedAt = first!.FinishedAt;

        clock.Advance(TimeSpan.FromMinutes(5));
        var second = await service.FinalizarTentativaAsync(attemptId);

        // Segundo finish não deve re-corrigir nem mudar o horário de finalização.
        Assert.Equal(finishedAt, second!.FinishedAt);
    }

    // ---- Isolamento entre usuários --------------------------------------
    // Ids de tentativa aparecem na URL. Sem a checagem de posse, trocar o Guid daria
    // acesso à prova de outra pessoa — inclusive para alterar respostas dela.

    [Fact]
    public async Task Attempt_DeOutroUsuario_NaoEhVisivel()
    {
        var (service, exam, _, usuario) = BuildServiceComUsuario();
        var attemptId = await service.IniciarTentativaAsync(exam.Id);

        usuario.Id = Guid.NewGuid(); // outra pessoa, mesma aplicação

        Assert.Null(await service.ObterEstadoAsync(attemptId));
        Assert.Null(await service.ObterQuestaoAsync(attemptId, 1));
        Assert.Null(await service.ObterResultadoAsync(attemptId));
    }

    [Fact]
    public async Task SalvarResposta_EmAttemptDeOutroUsuario_Falha()
    {
        var (service, exam, _, usuario) = BuildServiceComUsuario();
        var attemptId = await service.IniciarTentativaAsync(exam.Id);
        var question = await service.ObterQuestaoAsync(attemptId, 1);

        usuario.Id = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SalvarRespostaAsync(new SalvarRespostaRequest(
                attemptId,
                question!.Id,
                new[] { CorrectOption(question) },
                false,
                10)));
    }

    [Fact]
    public async Task IniciarTentativa_SemUsuarioLogado_Falha()
    {
        var (service, exam, _, usuario) = BuildServiceComUsuario();
        usuario.Id = null;

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.IniciarTentativaAsync(exam.Id));
    }

    // ---- Fim do tempo imposto pelo servidor ------------------------------
    // O timer da tela é conveniência. Se o limite valesse só no JavaScript, bastaria desligá-lo
    // (ou editar o contador no navegador) para ter tempo infinito — e o limite de tempo é o
    // centro da fidelidade à prova real.

    [Fact]
    public async Task PrazoEsgotado_EncerraATentativaMesmoSemOClientePedir()
    {
        var (service, exam, clock) = BuildService();
        var attemptId = await service.IniciarTentativaAsync(exam.Id);

        clock.Advance(TimeSpan.FromMinutes(46)); // o limite do exame é 45

        var state = await service.ObterEstadoAsync(attemptId);

        Assert.True(state!.IsFinished);
        Assert.Equal(0, state.RemainingSeconds);
    }

    [Fact]
    public async Task PrazoEsgotado_CorrigeOQueFoiRespondidoEEntregaOResultado()
    {
        var (service, exam, clock) = BuildService();
        var attemptId = await service.IniciarTentativaAsync(exam.Id);

        for (var n = 1; n <= 4; n++)
        {
            var q = await service.ObterQuestaoAsync(attemptId, n);
            await service.SalvarRespostaAsync(new SalvarRespostaRequest(
                attemptId, q!.Id, new[] { CorrectOption(q) }, false, 10));
        }

        clock.Advance(TimeSpan.FromMinutes(46));

        // Ninguém chamou Finalizar: o resultado existe porque o servidor fechou a prova sozinho.
        var result = await service.ObterResultadoAsync(attemptId);

        Assert.NotNull(result);
        Assert.Equal(100m, result!.ScorePercent);
        Assert.True(result.Passed);
    }

    [Fact]
    public async Task PrazoEsgotado_RegistraOVencimentoComoTermino_NaoOMomentoDaVolta()
    {
        var (service, exam, clock) = BuildService();
        var attemptId = await service.IniciarTentativaAsync(exam.Id);

        // Quem fecha o navegador e volta dois dias depois não deve aparecer no histórico com
        // uma prova de dois dias de duração.
        clock.Advance(TimeSpan.FromDays(2));

        var result = await service.FinalizarTentativaAsync(attemptId);

        Assert.Equal(Iniciar.AddMinutes(45), result!.FinishedAt);
    }

    [Fact]
    public async Task SalvarResposta_ComPrazoEsgotado_NaoContaParaANota()
    {
        var (service, exam, clock) = BuildService();
        var attemptId = await service.IniciarTentativaAsync(exam.Id);
        var q1 = await service.ObterQuestaoAsync(attemptId, 1);

        clock.Advance(TimeSpan.FromMinutes(46));

        var resultado = await service.SalvarRespostaAsync(new SalvarRespostaRequest(
            attemptId, q1!.Id, new[] { CorrectOption(q1) }, false, 10));

        Assert.Equal(ResultadoDeSalvarResposta.TentativaEncerrada, resultado);
        Assert.Equal(0m, (await service.ObterResultadoAsync(attemptId))!.ScorePercent);
    }

    // ---- Validação do que o cliente envia -------------------------------
    // A questão tem de estar na composição sorteada, e a alternativa tem de ser da questão.
    // QuestionId não tem foreign key na tabela de respostas, então sem esta checagem um POST
    // direto grava linha órfã — e uma por Guid inventado, sem limite.

    [Fact]
    public async Task SalvarResposta_ComQuestaoForaDaComposicao_EhRecusadaENaoPersisteNada()
    {
        var (service, exam, _, _, tentativas) = BuildServiceCompleto();
        var attemptId = await service.IniciarTentativaAsync(exam.Id);

        var resultado = await service.SalvarRespostaAsync(new SalvarRespostaRequest(
            attemptId, Guid.NewGuid(), new[] { Guid.NewGuid() }, false, 10));

        Assert.Equal(ResultadoDeSalvarResposta.RespostaInvalida, resultado);
        Assert.Empty((await tentativas.ObterPorIdAsync(attemptId))!.Answers);
    }

    [Fact]
    public async Task SalvarResposta_ComAlternativaDeOutraQuestao_EhRecusada()
    {
        var (service, exam, _, _, tentativas) = BuildServiceCompleto();
        var attemptId = await service.IniciarTentativaAsync(exam.Id);
        var q1 = await service.ObterQuestaoAsync(attemptId, 1);
        var q2 = await service.ObterQuestaoAsync(attemptId, 2);

        // Questão válida, alternativa que pertence a outra questão da mesma prova.
        var resultado = await service.SalvarRespostaAsync(new SalvarRespostaRequest(
            attemptId, q1!.Id, new[] { CorrectOption(q2!) }, false, 10));

        Assert.Equal(ResultadoDeSalvarResposta.RespostaInvalida, resultado);
        Assert.Empty((await tentativas.ObterPorIdAsync(attemptId))!.Answers);
    }

    // ---- Ordem das alternativas -----------------------------------------
    // O banco de questões escreve a correta em primeiro (formato dos arquivos de seed). Entregar
    // nessa ordem ensinava a marcar a de cima: o candidato acertava sem entender o conceito, que é
    // exatamente o contrário do que o simulado se propõe a treinar.

    /// <summary>Exame de 1 questão com 4 alternativas — a correta em primeiro, como no seed.</summary>
    private static Exame ExameDeQuatroAlternativas()
    {
        var exam = new Exame("AZ-900", "Azure Fundamentals", 45, 70, totalQuestions: 1);
        var area = exam.AdicionarAreaDeHabilidade("conceitos-de-nuvem", "Conceitos de nuvem", 100m);
        var q = exam.AdicionarQuestao(area.Id, "questao-4-opcoes", "Questão", TipoDeQuestao.EscolhaUnica, "Explicação.");

        foreach (var (texto, correta) in new[] { ("A", true), ("B", false), ("C", false), ("D", false) })
        {
            q.AdicionarOpcao(texto, correta, q.Options.Count);
        }

        return exam;
    }

    [Fact]
    public async Task Alternativas_MudamDePosicaoEntreTentativas()
    {
        var exam = ExameDeQuatroAlternativas();
        var posicoesDaCorreta = new HashSet<int>();

        for (var i = 0; i < 40; i++)
        {
            var (service, _, _, _, _) = BuildServiceCompleto(exam);
            var attemptId = await service.IniciarTentativaAsync(exam.Id);
            var questao = await service.ObterQuestaoAsync(attemptId, 1);

            posicoesDaCorreta.Add(questao!.Options.ToList().FindIndex(o => o.Text == "A"));
        }

        // Antes disso o conjunto era { 0 } em qualquer número de tentativas.
        Assert.Equal(4, posicoesDaCorreta.Count);
    }

    [Fact]
    public async Task Alternativas_NaoTrocamDeLugarDentroDaMesmaTentativa()
    {
        var exam = ExameDeQuatroAlternativas();
        var (service, _, _, _, _) = BuildServiceCompleto(exam);
        var attemptId = await service.IniciarTentativaAsync(exam.Id);

        var primeira = (await service.ObterQuestaoAsync(attemptId, 1))!.Options.Select(o => o.Id).ToList();
        var segunda = (await service.ObterQuestaoAsync(attemptId, 1))!.Options.Select(o => o.Id).ToList();

        // Navegar para frente e voltar recarrega a questão: se a ordem mudasse, a alternativa
        // marcada apareceria em outro lugar da lista.
        Assert.Equal(primeira, segunda);

        await service.SalvarRespostaAsync(new SalvarRespostaRequest(
            attemptId, (await service.ObterQuestaoAsync(attemptId, 1))!.Id, new[] { primeira[0] }, false, 10));
        var resultado = await service.FinalizarTentativaAsync(attemptId);

        // O gabarito é releitura do que aconteceu: mesma ordem que estava na tela durante a prova.
        Assert.Equal(
            (await service.ObterQuestaoAsync(attemptId, 1))!.Options.Select(o => o.Text),
            resultado!.Questions.Single().Options.Select(o => o.Text));
    }

    [Fact]
    public async Task OrderIndexDaAlternativa_EhAPosicaoNaTela_NaoADoBanco()
    {
        var exam = ExameDeQuatroAlternativas();
        var (service, _, _, _, _) = BuildServiceCompleto(exam);
        var attemptId = await service.IniciarTentativaAsync(exam.Id);

        var opcoes = (await service.ObterQuestaoAsync(attemptId, 1))!.Options;

        // Expor o índice do arquivo aqui entregaria o gabarito para quem lesse o HTML.
        Assert.Equal(Enumerable.Range(0, 4), opcoes.Select(o => o.OrderIndex));
    }

    [Fact]
    public async Task SalvarResposta_TempoInformadoPeloCliente_NaoPassaDoTempoDaProva()
    {
        var (service, exam, _, _, tentativas) = BuildServiceCompleto();
        var attemptId = await service.IniciarTentativaAsync(exam.Id);
        var q1 = await service.ObterQuestaoAsync(attemptId, 1);

        // O tempo é acumulado a cada gravação: sem teto, envios forjados estouram o int em
        // silêncio, porque a aritmética é unchecked.
        var resultado = await service.SalvarRespostaAsync(new SalvarRespostaRequest(
            attemptId, q1!.Id, new[] { CorrectOption(q1) }, false, int.MaxValue));

        Assert.Equal(ResultadoDeSalvarResposta.Gravada, resultado);

        var answer = Assert.Single((await tentativas.ObterPorIdAsync(attemptId))!.Answers);
        Assert.Equal(45 * 60, answer.TimeSpentSeconds);
    }
}
