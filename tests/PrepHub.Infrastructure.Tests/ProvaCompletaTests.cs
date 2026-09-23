using PrepHub.Application.Abstractions;
using PrepHub.Application.Contracts;
using PrepHub.Application.Observabilidade;
using PrepHub.Application.Sessoes;
using PrepHub.Application.Sorteios;
using PrepHub.Domain.Correcao;
using PrepHub.Domain.Entidades;
using PrepHub.Domain.Enums;
using PrepHub.Infrastructure.Persistence;
using PrepHub.Infrastructure.Persistence.Repositories;
using PrepHub.Infrastructure.Persistence.Seed;
using PrepHub.Infrastructure.Time;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace PrepHub.Infrastructure.Tests;

/// <summary>
/// Exercita no pipeline de verdade — sorteio, apresentação, gravação, correção e score report —
/// <b>todo exame cujo banco já cobre os domínios declarados</b>, contra SQLite real e com uma
/// request por operação.
/// </summary>
/// <remarks>
/// Nasceu preso ao AZ-104, porque era o único exame completo. Generalizar não foi cosmético: a
/// pergunta que o teste responde não é "o AZ-104 funciona", e sim "um exame cujo banco está pronto
/// atravessa o pipeline". Com <see cref="ExamesCompletos"/> derivando a lista do catálogo, cada
/// exame novo passa a ser exercitado no dia em que fecha a cobertura, sem que ninguém lembre de
/// escrever teste — e um exame que regredir (domínio esvaziado) simplesmente sai da lista, o que
/// os testes de integridade do catálogo já denunciam por outro caminho.
///
/// Os exames são publicados <b>apenas dentro do banco deste teste</b>. As definições no seeder
/// seguem como estão, o que é o ponto: dá para validar o conteúdo no fluxo completo antes de
/// decidir publicá-lo para os usuários.
/// </remarks>
public sealed class ProvaCompletaTests : IDisposable
{
    /// <summary>
    /// Códigos de exame cujo catálogo cobre todos os domínios declarados e sustenta o tamanho da
    /// prova. É a mesma condição que os testes de integridade exigem para publicar.
    /// </summary>
    public static TheoryData<string> ExamesCompletos()
    {
        var porExameEArea = CatalogoDeQuestoesDeSeed.Carregar()
            .GroupBy(a => (Exame: a.ExameCode, a.Area))
            .ToDictionary(g => g.Key, g => g.Sum(a => a.Questoes.Count));

        var dados = new TheoryData<string>();

        foreach (var definicao in PrepHubDbSeeder.Exames)
        {
            var porArea = definicao.Areas
                .Select(a => porExameEArea.GetValueOrDefault((definicao.Code, a.Key)))
                .ToList();

            if (porArea.All(total => total > 0) && porArea.Sum() >= definicao.TotalQuestions)
            {
                dados.Add(definicao.Code);
            }
        }

        return dados;
    }

    private readonly SqliteConnection _connection;
    private readonly IClock _clock = new SystemClock();
    private static readonly Guid _usuarioId = Guid.NewGuid();

    public ProvaCompletaTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var ctx = CreateContext();
        ctx.Database.EnsureCreated();
        PrepHubDbSeeder.SemearAsync(ctx).GetAwaiter().GetResult();

        // Publica todos neste banco em memória — as definições no seeder não são tocadas.
        foreach (var exame in ctx.Exams.ToList())
        {
            exame.AtualizarDefinicao(
                exame.Name, exame.TimeLimitMinutes, exame.PassingScorePercent, exame.TotalQuestions,
                isPublished: true);
        }

        ctx.Users.Add(new Usuario(
            ProvedorDeLogin.Google, "provider-key-prova", "Candidato de Teste",
            "prova@example.com", null, _clock.UtcNow, _usuarioId));
        ctx.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    private PrepHubDbContext CreateContext()
        => new(new DbContextOptionsBuilder<PrepHubDbContext>().UseSqlite(_connection).Options);

    private (SessaoDeProvaService service, PrepHubDbContext ctx) NewRequest()
    {
        var ctx = CreateContext();
        var exames = new ExameRepository(ctx);
        var tentativas = new TentativaDeProvaRepository(ctx);
        return (new SessaoDeProvaService(
            exames, tentativas,
            new SorteadorDeQuestoesService(exames, tentativas, new SementeFixa()),
            ctx, _clock, new FixedUsuarioAtual(_usuarioId),
            MetricasDeNegocioSilenciosas.Instancia), ctx);
    }

    private sealed class FixedUsuarioAtual(Guid id) : IUsuarioAtual
    {
        public Guid? Id { get; } = id;
    }

    private sealed class SementeFixa : IGeradorDeAleatoriedade
    {
        public Random Criar() => new(20260825);
    }

    private async Task<Exame> ObterExameAsync(string codigo)
    {
        using var ctx = CreateContext();
        return await ctx.Exams.Include(e => e.SkillAreas).SingleAsync(e => e.Code == codigo);
    }

    private async Task<Guid> IniciarAsync(string codigo)
    {
        var exame = await ObterExameAsync(codigo);
        var (service, ctx) = NewRequest();
        using (ctx) return await service.IniciarTentativaAsync(exame.Id);
    }

    // ------------------------------------------------------------------ composição da prova

    [Theory]
    [MemberData(nameof(ExamesCompletos))]
    public async Task Prova_TemExatamenteOTamanhoDeclarado(string codigo)
    {
        var exame = await ObterExameAsync(codigo);
        var attemptId = await IniciarAsync(codigo);

        using var ctx = CreateContext();
        var sorteadas = await ctx.ExamAttemptQuestions
            .Where(q => q.ExamAttemptId == attemptId)
            .ToListAsync();

        Assert.Equal(exame.TotalQuestions, sorteadas.Count);
        Assert.Equal(sorteadas.Count, sorteadas.Select(q => q.QuestionId).Distinct().Count());
    }

    /// <summary>
    /// A repartição por domínio segue o peso do Skills Measured, não o tamanho do pool.
    /// </summary>
    /// <remarks>
    /// A tolerância de ±1 item existe porque a repartição é pelo método do maior resto e os pesos
    /// oficiais raramente somam 100 nos pontos médios, então a cota exata quase nunca é inteira.
    /// O que o teste garante é o que importa: nenhum domínio some, nenhum domina, e a distribuição
    /// acompanha o blueprint mesmo quando os domínios têm pools de tamanho parecido — que é
    /// exatamente a situação em que um sorteio proporcional ao pool passaria despercebido.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ExamesCompletos))]
    public async Task Prova_RepartePorDominioSegundoOPesoDoBlueprint(string codigo)
    {
        var exame = await ObterExameAsync(codigo);
        var attemptId = await IniciarAsync(codigo);

        using var ctx = CreateContext();
        var porArea = await ctx.ExamAttemptQuestions
            .Where(q => q.ExamAttemptId == attemptId)
            .Join(ctx.Questions, a => a.QuestionId, q => q.Id, (_, q) => q.SkillAreaId)
            .GroupBy(id => id)
            .Select(g => new { AreaId = g.Key, Total = g.Count() })
            .ToListAsync();

        var pesoTotal = exame.SkillAreas.Sum(a => a.WeightPercent);

        Assert.Equal(exame.SkillAreas.Count, porArea.Count);

        foreach (var area in exame.SkillAreas)
        {
            var esperado = area.WeightPercent / pesoTotal * exame.TotalQuestions;
            var obtido = porArea.Single(p => p.AreaId == area.Id).Total;

            Assert.True(
                Math.Abs(obtido - esperado) <= 1m,
                $"{codigo}/{area.Key}: {obtido} itens, esperado ~{esperado:0.0}");
        }
    }

    /// <summary>
    /// O <b>banco</b> de cada exame oferece os quatro formatos.
    /// </summary>
    /// <remarks>
    /// ⚠️ Esta asserção já foi escrita sobre a prova sorteada, e estava errada — foi corrigida
    /// depois de falhar. Conter os quatro formatos é propriedade da composição do banco, não de um
    /// sorteio: com o AZ-900 tendo 11 questões de arrastar em 285 (3,9%), uma prova de 40 itens tem
    /// esperança de ~1,5 delas, e sortear zero é resultado normal. A versão anterior passava por
    /// acidente enquanto só existiam exames com proporção saudável, e quebrou ao entrar um exame
    /// cujo banco é desequilibrado — que é justamente o defeito que ela deveria ter denunciado, e
    /// denunciava pelo lugar errado.
    ///
    /// O formato de arrastar e soltar é o que mais justifica o teste: ele não existe como linha
    /// própria no banco, e sim como todas as combinações de alvo com item.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ExamesCompletos))]
    public async Task Banco_OfereceOsQuatroFormatos(string codigo)
    {
        using var ctx = CreateContext();
        var tipos = await ctx.Questions
            .Where(q => q.IsActive && ctx.Exams.Any(e => e.Id == q.ExamId && e.Code == codigo))
            .Select(q => q.Type)
            .Distinct()
            .ToListAsync();

        Assert.Contains(TipoDeQuestao.EscolhaUnica, tipos);
        Assert.Contains(TipoDeQuestao.EscolhaMultipla, tipos);
        Assert.Contains(TipoDeQuestao.Associacao, tipos);

        // Os quatro formatos são outros na AWS: não existe Sim/Não, existe ordenação.
        var fornecedor = PrepHubDbSeeder.FornecedorPorExame[codigo];
        if (fornecedor == FornecedorDoExame.Aws)
        {
            Assert.Contains(TipoDeQuestao.Ordenacao, tipos);
            Assert.DoesNotContain(TipoDeQuestao.SimNao, tipos);
        }
        else
        {
            Assert.Contains(TipoDeQuestao.SimNao, tipos);
        }
    }

    // ------------------------------------------------------------------------- fluxo completo

    private async Task<Dictionary<Guid, List<Guid>>> GabaritoAsync(string codigo)
    {
        using var ctx = CreateContext();
        var questoes = await ctx.Questions
            .Include(q => q.Options)
            .Where(q => ctx.Exams.Any(e => e.Id == q.ExamId && e.Code == codigo))
            .ToListAsync();

        return questoes.ToDictionary(
            q => q.Id,
            q => q.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToList());
    }

    private async Task<ResultadoDaProvaDto> ResponderTudoAsync(string codigo, bool corretamente)
    {
        var attemptId = await IniciarAsync(codigo);
        var gabarito = await GabaritoAsync(codigo);

        int total;
        {
            var (service, ctx) = NewRequest();
            using (ctx) total = (await service.ObterEstadoAsync(attemptId))!.Questions.Count;
        }

        for (var n = 1; n <= total; n++)
        {
            var (service, ctx) = NewRequest();
            using (ctx)
            {
                var q = await service.ObterQuestaoAsync(attemptId, n);
                var corretas = gabarito[q!.Id];

                // Para errar, marca uma alternativa fora do gabarito. Vale para todos os tipos:
                // acertar exige o conjunto EXATO, então qualquer conjunto diferente erra.
                var selecao = corretamente
                    ? corretas
                    : q.Options.Select(o => o.Id).Where(id => !corretas.Contains(id)).Take(1).ToList();

                await service.SalvarRespostaAsync(
                    new SalvarRespostaRequest(attemptId, q.Id, selecao, false, 12));
            }
        }

        var (svc, c) = NewRequest();
        using (c) return (await svc.FinalizarTentativaAsync(attemptId))!;
    }

    [Theory]
    [MemberData(nameof(ExamesCompletos))]
    public async Task ProvaInteira_RespondidaCorretamente_Pontua100EAprova(string codigo)
    {
        var resultado = await ResponderTudoAsync(codigo, corretamente: true);

        Assert.Equal(codigo, resultado.ExamCode);
        Assert.Equal(100m, resultado.ScorePercent);
        Assert.True(resultado.Passed);
        Assert.Equal(resultado.TotalQuestions, resultado.CorrectAnswers);
        Assert.Equal(EscalaDeNota.NotaMaxima, resultado.ScaledScore);
        Assert.Equal(EscalaDeNota.NotaDeCorte, resultado.ScaledPassingScore);
    }

    [Theory]
    [MemberData(nameof(ExamesCompletos))]
    public async Task ProvaInteira_RespondidaErrada_ZeraEReprova(string codigo)
    {
        var resultado = await ResponderTudoAsync(codigo, corretamente: false);

        Assert.Equal(0m, resultado.ScorePercent);
        Assert.False(resultado.Passed);
        Assert.Equal(0, resultado.CorrectAnswers);
        Assert.True(resultado.ScaledScore < EscalaDeNota.NotaDeCorte);
    }

    /// <summary>
    /// O score report traz todos os domínios, e a soma dos itens por domínio fecha com a prova.
    /// </summary>
    [Theory]
    [MemberData(nameof(ExamesCompletos))]
    public async Task ScoreReport_CobreTodosOsDominios(string codigo)
    {
        var exame = await ObterExameAsync(codigo);
        var resultado = await ResponderTudoAsync(codigo, corretamente: true);

        Assert.Equal(exame.SkillAreas.Count, resultado.SkillAreas.Count);
        Assert.Equal(resultado.TotalQuestions, resultado.SkillAreas.Sum(a => a.TotalQuestions));
        Assert.All(resultado.SkillAreas, a => Assert.Equal(100m, a.ScorePercent));
        Assert.All(resultado.SkillAreas, a => Assert.True(a.TotalQuestions > 0, $"{a.Name} sem itens"));
    }

    /// <summary>
    /// A revisão de estudo traz explicação em toda questão, inclusive nas de arrastar e soltar.
    /// </summary>
    [Theory]
    [MemberData(nameof(ExamesCompletos))]
    public async Task RevisaoDeEstudo_TrazExplicacaoEGabaritoEmTodaQuestao(string codigo)
    {
        var resultado = await ResponderTudoAsync(codigo, corretamente: true);

        Assert.Equal(resultado.TotalQuestions, resultado.Questions.Count);
        Assert.All(resultado.Questions, q =>
        {
            Assert.False(string.IsNullOrWhiteSpace(q.Explanation), $"questão {q.Number} sem explicação");
            Assert.Contains(q.Options, o => o.IsCorrect);
            Assert.True(q.WasCorrect, $"questão {q.Number} marcada como errada numa prova 100% correta");
        });

        // Nas de arrastar, a revisão mostra o alvo de cada par — sem isso o gabarito fica ilegível.
        // Sem exigir que existam: quantas caem numa prova depende do sorteio, e o banco do AZ-900
        // tem poucas o bastante para uma prova de 40 itens sair sem nenhuma.
        var arrastar = resultado.Questions.Where(q => q.Type == TipoDeQuestao.Associacao).ToList();
        Assert.All(arrastar, q => Assert.All(q.Options, o =>
            Assert.False(string.IsNullOrWhiteSpace(o.TargetText))));
    }
}
