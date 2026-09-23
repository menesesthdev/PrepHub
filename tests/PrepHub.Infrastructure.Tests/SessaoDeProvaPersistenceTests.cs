using PrepHub.Application.Abstractions;
using PrepHub.Application.Contracts;
using PrepHub.Application.Observabilidade;
using PrepHub.Application.Sessoes;
using PrepHub.Application.Sorteios;
using PrepHub.Domain.Entidades;
using PrepHub.Domain.Enums;
using PrepHub.Infrastructure.Persistence;
using PrepHub.Infrastructure.Persistence.Repositories;
using PrepHub.Infrastructure.Time;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace PrepHub.Infrastructure.Tests;

/// <summary>
/// Testes de integração contra SQLite real, usando um novo DbContext por operação para
/// reproduzir o ciclo request-por-request da web. É aqui que se pega o bug de gravação de
/// respostas (INSERT x UPDATE com chave gerada no domínio).
/// </summary>
public sealed class SessaoDeProvaPersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IClock _clock = new SystemClock();

    public SessaoDeProvaPersistenceTests()
    {
        // Connection in-memory mantida aberta => o banco sobrevive entre múltiplos DbContexts.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var ctx = CreateContext();
        ctx.Database.EnsureCreated();
        PrepHubDbSeeder.SemearAsync(ctx).GetAwaiter().GetResult();

        // A tentativa tem FK obrigatória para Users, e o SQLite valida FK — o dono
        // precisa existir antes de qualquer tentativa ser gravada.
        ctx.Users.Add(new Usuario(
            ProvedorDeLogin.Google,
            providerKey: "provider-key-de-teste",
            name: "Candidato de Teste",
            email: "teste@example.com",
            avatarUrl: null,
            createdAt: _clock.UtcNow,
            id: _usuarioId));
        ctx.SaveChanges();
    }

    private PrepHubDbContext CreateContext()
        => new(new DbContextOptionsBuilder<PrepHubDbContext>().UseSqlite(_connection).Options);

    // Cada "request" recebe seu próprio contexto e serviço (como no ciclo scoped da web).
    // Todas as "requests" do teste representam a mesma pessoa logada.
    private static readonly Guid _usuarioId = Guid.NewGuid();
    private readonly IUsuarioAtual _usuario = new FixedUsuarioAtual(_usuarioId);

    private (SessaoDeProvaService service, PrepHubDbContext ctx) NewRequest()
    {
        var ctx = CreateContext();
        var exames = new ExameRepository(ctx);
        var tentativas = new TentativaDeProvaRepository(ctx);
        return (new SessaoDeProvaService(
            exames,
            tentativas,
            new SorteadorDeQuestoesService(exames, tentativas, new SementeFixa()),
            ctx,
            _clock,
            _usuario,
            MetricasDeNegocioSilenciosas.Instancia), ctx);
    }

    private sealed class FixedUsuarioAtual(Guid id) : IUsuarioAtual
    {
        public Guid? Id { get; } = id;
    }

    /// <summary>Semente fixa: a mesma prova é sorteada em toda execução da suíte.</summary>
    private sealed class SementeFixa : IGeradorDeAleatoriedade
    {
        public Random Criar() => new(20260726);
    }

    /// <summary>
    /// O exame AZ-900, explicitamente. Esta classe é sobre o banco de questões dele.
    /// </summary>
    /// <remarks>
    /// Era <c>FirstAsync()</c> sem filtro, o que funcionava por acidente enquanto havia um exame
    /// só. Com o seed multi-exame, qual linha vem primeiro passa a depender da ordem de inserção —
    /// e o dia em que viesse o AZ-104 estes testes falhariam por um motivo que não tem nada a ver
    /// com o que eles verificam (ele está em construção e recusa tentativa).
    /// </remarks>
    private async Task<Guid> GetSeededExamIdAsync()
    {
        using var ctx = CreateContext();
        return (await ctx.Exams.SingleAsync(e => e.Code == "AZ-900")).Id;
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Seed_CarregaBancoDeQuestoesDosArquivos()
    {
        using var ctx = CreateContext();

        // Por código, não SingleAsync(): o seed passou a semear vários exames, e este teste é
        // sobre o banco de questões do AZ-900 especificamente.
        var exam = await ctx.Exams
            .Include(e => e.SkillAreas)
            .Include(e => e.Questions).ThenInclude(q => q.Options)
            .SingleAsync(e => e.Code == "AZ-900");

        Assert.Equal("AZ-900", exam.Code);
        Assert.Equal(3, exam.SkillAreas.Count);

        // TotalQuestions é o tamanho da PROVA; o banco tem que ser bem maior que isso, senão o
        // sorteio não tem de onde variar entre tentativas.
        Assert.Equal(40, exam.TotalQuestions);
        Assert.True(exam.Questions.Count >= 100, $"banco com apenas {exam.Questions.Count} questões");

        Assert.All(exam.Questions, q => Assert.True(q.Options.Count >= 2));
        Assert.All(exam.Questions, q => Assert.Contains(q.Options, o => o.IsCorrect));
        Assert.All(exam.Questions, q => Assert.False(string.IsNullOrWhiteSpace(q.ExternalId)));

        // Todo domínio precisa de questões suficientes para preencher sua cota na prova.
        foreach (var area in exam.SkillAreas)
        {
            var doDominio = exam.Questions.Count(q => q.SkillAreaId == area.Id);
            var cota = (int)Math.Ceiling(exam.TotalQuestions * area.WeightPercent / 100m);
            Assert.True(doDominio >= cota, $"{area.Key}: {doDominio} questões para uma cota de {cota}");
        }
    }

    [Fact]
    public async Task Seed_RodadoDuasVezes_NaoDuplicaQuestoes()
    {
        int antes;
        using (var ctx = CreateContext())
        {
            antes = await ctx.Questions.CountAsync();
        }

        using (var ctx = CreateContext())
        {
            await PrepHubDbSeeder.SemearAsync(ctx);
        }

        using (var ctx = CreateContext())
        {
            // Idempotência pelo ExternalId: reimportar os lotes atualiza no lugar. Sem isso, cada
            // restart da aplicação multiplicaria o banco de questões.
            Assert.Equal(antes, await ctx.Questions.CountAsync());
        }
    }

    [Fact]
    public async Task IniciarTentativa_GravaComposicaoSorteadaComTamanhoDaProva()
    {
        var examId = await GetSeededExamIdAsync();

        Guid attemptId;
        {
            var (service, ctx) = NewRequest();
            using (ctx) attemptId = await service.IniciarTentativaAsync(examId);
        }

        using var verificacao = CreateContext();
        var sorteadas = await verificacao.ExamAttemptQuestions
            .Where(q => q.ExamAttemptId == attemptId)
            .OrderBy(q => q.OrderIndex)
            .ToListAsync();

        Assert.Equal(40, sorteadas.Count);
        Assert.Equal(Enumerable.Range(0, 40), sorteadas.Select(q => q.OrderIndex));
        Assert.Equal(40, sorteadas.Select(q => q.QuestionId).Distinct().Count());
    }

    [Fact]
    public async Task DuasTentativasSeguidas_QuaseNaoRepetemQuestoes()
    {
        var examId = await GetSeededExamIdAsync();

        var primeira = await IniciarEObterQuestoesAsync(examId);
        var segunda = await IniciarEObterQuestoesAsync(examId);

        var repetidas = primeira.Intersect(segunda).Count();

        // O teto da política é 15% (6 de 40). É esse número que o usuário sente: fazer dois
        // simulados seguidos não pode devolver a mesma prova. Note que a semente é FIXA nesta
        // suíte — as duas provas só diferem porque o histórico entrou na conta, não por sorte.
        Assert.True(repetidas <= 6, $"{repetidas} questões repetidas entre tentativas consecutivas");
    }

    [Fact]
    public async Task SegundaTentativa_SoRepeteQuestaoQueOUsuarioErrou()
    {
        var examId = await GetSeededExamIdAsync();
        var gabarito = await ObterGabaritoAsync();

        var primeira = await IniciarEObterQuestoesAsync(examId);

        // Erra as de posição par, acerta as ímpares — assim as duas metades disputam o reforço.
        var erradas = primeira.Where((_, i) => i % 2 == 0).ToHashSet();
        var acertadas = primeira.Where(id => !erradas.Contains(id)).ToHashSet();
        await ResponderEFinalizarAsync(primeira, erradas, gabarito);

        var segunda = await IniciarEObterQuestoesAsync(examId);
        var repetidas = segunda.Intersect(primeira).ToList();

        // O valor pedagógico do histórico é reencontrar o ERRO. Repetir o que a pessoa acertou é
        // gastar vaga de prova sem ensinar nada — e o teste antigo, que só contava repetições,
        // passaria igual se o sorteio devolvesse os acertos.
        Assert.NotEmpty(repetidas);
        Assert.True(repetidas.Count <= 6, $"{repetidas.Count} repetidas — acima do teto de 15%");
        Assert.All(repetidas, id => Assert.Contains(id, erradas));
        Assert.Empty(segunda.Intersect(acertadas));
    }

    [Fact]
    public async Task TentativaAbandonadaSemResponder_NaoEncheOReforcoDeQuestoesNuncaLidas()
    {
        var examId = await GetSeededExamIdAsync();

        // Abre a prova e some: as 40 questões foram apresentadas, nenhuma foi lida.
        var abandonada = await IniciarEObterQuestoesAsync(examId);

        var seguinte = await IniciarEObterQuestoesAsync(examId);

        // Regressão: item em branco era contado como erro sem olhar se a prova tinha sido
        // encerrada, então uma tentativa fechada no item 1 despejava 40 questões nunca lidas na
        // fila de reforço — empurrando para fora dela os erros de verdade.
        Assert.Empty(seguinte.Intersect(abandonada));
    }

    [Fact]
    public async Task Seed_QuestaoForaDosArquivos_EAposentadaMasContinuaNoBanco()
    {
        var examId = await GetSeededExamIdAsync();
        var forasteira = Guid.NewGuid();

        using (var ctx = CreateContext())
        {
            var areaId = (await ctx.SkillAreas.FirstAsync(a => a.ExamId == examId)).Id;
            var questao = new Questao(
                examId, areaId, "az900-sem-arquivo-de-origem",
                "Questão que não existe em nenhum lote JSON.",
                TipoDeQuestao.EscolhaUnica,
                "Explicação qualquer, longa o bastante para o validador não ter o que dizer sobre ela.",
                id: forasteira);
            ctx.Questions.Add(questao);
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            await PrepHubDbSeeder.SemearAsync(ctx);
        }

        using (var ctx = CreateContext())
        {
            // Aposentada, nunca apagada: as tentativas que já a receberam continuam íntegras.
            var questao = await ctx.Questions.SingleAsync(q => q.Id == forasteira);
            Assert.False(questao.IsActive);

            var pool = await new ExameRepository(ctx).ObterPoolAsync(examId);
            Assert.DoesNotContain(pool, q => q.QuestaoId == forasteira);
        }
    }

    [Fact]
    public async Task Seed_QuestaoAposentadaQueVoltaAosArquivos_EReativada()
    {
        var examId = await GetSeededExamIdAsync();
        Guid alvo;

        using (var ctx = CreateContext())
        {
            var questao = await ctx.Questions.FirstAsync(q => q.ExamId == examId);
            questao.Aposentar();
            alvo = questao.Id;
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            await PrepHubDbSeeder.SemearAsync(ctx);
        }

        using (var ctx = CreateContext())
        {
            Assert.True((await ctx.Questions.SingleAsync(q => q.Id == alvo)).IsActive);
        }
    }

    [Fact]
    public async Task Seed_CorrigeODominioDeQuestaoMalClassificada()
    {
        var examId = await GetSeededExamIdAsync();
        Guid alvo;
        Guid areaCorreta;

        using (var ctx = CreateContext())
        {
            var questao = await ctx.Questions.FirstAsync(q => q.ExamId == examId);
            alvo = questao.Id;
            areaCorreta = questao.SkillAreaId;

            // Simula a classificação errada que o arquivo de seed corrige.
            var outraArea = await ctx.SkillAreas.FirstAsync(a => a.ExamId == examId && a.Id != areaCorreta);
            ctx.Entry(questao).Property(q => q.SkillAreaId).CurrentValue = outraArea.Id;
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            await PrepHubDbSeeder.SemearAsync(ctx);
        }

        using (var ctx = CreateContext())
        {
            // Regressão: o seed reescrevia enunciado, explicação e tópico, mas não o domínio —
            // então mover a questão de arquivo não corrigia nada e ela seguia contando para a
            // cota do domínio errado, furando a fidelidade ao blueprint.
            Assert.Equal(areaCorreta, (await ctx.Questions.SingleAsync(q => q.Id == alvo)).SkillAreaId);
        }
    }

    private async Task<Dictionary<Guid, List<Guid>>> ObterGabaritoAsync()
    {
        using var ctx = CreateContext();
        return (await ctx.Questions.Include(q => q.Options).ToListAsync())
            .ToDictionary(q => q.Id, q => q.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToList());
    }

    /// <summary>
    /// Responde todas as questões da prova — errando as de <paramref name="errar"/> — e encerra.
    /// Errar é marcar a primeira alternativa INCORRETA, que todo tipo de questão tem (o validador
    /// do catálogo garante que nenhuma questão tem só alternativas corretas).
    /// </summary>
    private async Task ResponderEFinalizarAsync(
        IReadOnlyList<Guid> questoes,
        IReadOnlySet<Guid> errar,
        IReadOnlyDictionary<Guid, List<Guid>> gabarito)
    {
        Dictionary<Guid, Guid> primeiraIncorreta;
        using (var ctx = CreateContext())
        {
            primeiraIncorreta = (await ctx.Questions
                    .Include(q => q.Options)
                    .Where(q => questoes.Contains(q.Id))
                    .ToListAsync())
                .ToDictionary(q => q.Id, q => q.Options.First(o => !o.IsCorrect).Id);
        }

        var attemptId = await ObterTentativaEmAbertoAsync();

        foreach (var questaoId in questoes)
        {
            var selecao = errar.Contains(questaoId)
                ? new List<Guid> { primeiraIncorreta[questaoId] }
                : gabarito[questaoId];

            var (service, ctx) = NewRequest();
            using (ctx)
            {
                await service.SalvarRespostaAsync(new SalvarRespostaRequest(attemptId, questaoId, selecao, false, 5));
            }
        }

        {
            var (service, ctx) = NewRequest();
            using (ctx) await service.FinalizarTentativaAsync(attemptId);
        }
    }

    private async Task<Guid> ObterTentativaEmAbertoAsync()
    {
        using var ctx = CreateContext();
        return (await ctx.ExamAttempts
            .Where(a => a.FinishedAt == null)
            .OrderByDescending(a => a.StartedAt)
            .FirstAsync()).Id;
    }

    private async Task<IReadOnlyList<Guid>> IniciarEObterQuestoesAsync(Guid examId)
    {
        Guid attemptId;
        {
            var (service, ctx) = NewRequest();
            using (ctx) attemptId = await service.IniciarTentativaAsync(examId);
        }

        using var ctxLeitura = CreateContext();
        return await ctxLeitura.ExamAttemptQuestions
            .Where(q => q.ExamAttemptId == attemptId)
            .Select(q => q.QuestionId)
            .ToListAsync();
    }

    [Fact]
    public async Task SaveAnswer_NewThenUpdate_PersistsSingleRowAcrossContexts()
    {
        var examId = await GetSeededExamIdAsync();

        // 1) Iniciar tentativa (contexto próprio).
        Guid attemptId;
        {
            var (service, ctx) = NewRequest();
            using (ctx) attemptId = await service.IniciarTentativaAsync(examId);
        }

        // 2) Carregar a questão 1 e capturar duas opções.
        Guid questionId, firstOption, secondOption;
        {
            var (service, ctx) = NewRequest();
            using (ctx)
            {
                var q = await service.ObterQuestaoAsync(attemptId, 1);
                Assert.NotNull(q);
                questionId = q!.Id;
                firstOption = q.Options[0].Id;
                secondOption = q.Options[1].Id;
            }
        }

        // 3) Primeira gravação (INSERT) — cenário que disparava o erro de concorrência.
        {
            var (service, ctx) = NewRequest();
            using (ctx)
            {
                await service.SalvarRespostaAsync(new SalvarRespostaRequest(
                    attemptId, questionId, new[] { firstOption }, IsFlaggedForReview: false, TimeSpentSeconds: 10));
            }
        }

        // 4) Segunda gravação da MESMA questão (UPDATE): muda seleção e marca para revisão.
        {
            var (service, ctx) = NewRequest();
            using (ctx)
            {
                await service.SalvarRespostaAsync(new SalvarRespostaRequest(
                    attemptId, questionId, new[] { secondOption }, IsFlaggedForReview: true, TimeSpentSeconds: 5));
            }
        }

        // 5) Verificar no banco: exatamente uma resposta, com a seleção final e a flag.
        using (var ctx = CreateContext())
        {
            var answers = await ctx.ExamAttemptAnswers
                .Where(a => a.ExamAttemptId == attemptId)
                .ToListAsync();

            var answer = Assert.Single(answers);
            Assert.Equal(new[] { secondOption }, answer.SelectedOptionIds);
            Assert.True(answer.IsFlaggedForReview);
            Assert.Equal(15, answer.TimeSpentSeconds); // 10 + 5 acumulados
        }
    }

    [Fact]
    public async Task FullAttempt_AnswerAllCorrectly_ScoresHundredAndPasses()
    {
        var examId = await GetSeededExamIdAsync();

        // Gabarito: opções corretas por questão (direto do banco).
        Dictionary<Guid, List<Guid>> correctByQuestion;
        using (var ctx = CreateContext())
        {
            correctByQuestion = (await ctx.Questions
                    .Include(q => q.Options)
                    .ToListAsync())
                .ToDictionary(q => q.Id, q => q.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToList());
        }

        Guid attemptId;
        {
            var (service, ctx) = NewRequest();
            using (ctx) attemptId = await service.IniciarTentativaAsync(examId);
        }

        // Responde cada questão da prova sorteada corretamente (uma request por questão).
        int totalDaProva;
        {
            var (service, ctx) = NewRequest();
            using (ctx) totalDaProva = (await service.ObterEstadoAsync(attemptId))!.Questions.Count;
        }

        for (var n = 1; n <= totalDaProva; n++)
        {
            var (service, ctx) = NewRequest();
            using (ctx)
            {
                var q = await service.ObterQuestaoAsync(attemptId, n);
                await service.SalvarRespostaAsync(new SalvarRespostaRequest(
                    attemptId, q!.Id, correctByQuestion[q.Id], false, 8));
            }
        }

        ResultadoDaProvaDto? result;
        {
            var (service, ctx) = NewRequest();
            using (ctx) result = await service.FinalizarTentativaAsync(attemptId);
        }

        Assert.NotNull(result);
        Assert.Equal(100m, result!.ScorePercent);
        Assert.True(result.Passed);
        Assert.Equal(result.TotalQuestions, result.CorrectAnswers);
    }
}
