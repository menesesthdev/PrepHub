using PrepHub.Infrastructure.Persistence;
using PrepHub.Infrastructure.Persistence.Seed;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace PrepHub.Infrastructure.Tests;

/// <summary>
/// O seed roda a cada startup e é a única coisa entre os arquivos de conteúdo e o banco de
/// produção. Estes testes cobrem o que a suíte não alcançava: a sincronização do EXAME em si —
/// criar, e depois realinhar parâmetros e áreas com a definição.
/// </summary>
/// <remarks>
/// A parte de questões já era exercitada de lado por <c>SessaoDeProvaPersistenceTests</c>, que
/// semeia antes de cada cenário. O exame não: ele era criado uma vez e nunca mais tocado, então
/// a ausência de atualização não quebrava teste nenhum — aparecia como parâmetro velho no banco,
/// que é indistinguível de parâmetro certo até alguém conferir na mão.
/// </remarks>
public sealed class PrepHubDbSeederTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public PrepHubDbSeederTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var ctx = CreateContext();
        ctx.Database.EnsureCreated();
    }

    private PrepHubDbContext CreateContext()
        => new(new DbContextOptionsBuilder<PrepHubDbContext>().UseSqlite(_connection).Options);

    public void Dispose() => _connection.Dispose();

    // ------------------------------------------------------------------------ definição base

    [Fact]
    public async Task Semear_CriaTodosOsExamesDefinidos()
    {
        await using (var ctx = CreateContext())
        {
            await PrepHubDbSeeder.SemearAsync(ctx);
        }

        await using var leitura = CreateContext();
        var codigos = await leitura.Exams.Select(e => e.Code).ToListAsync();

        Assert.Equal(
            PrepHubDbSeeder.Exames.Select(e => e.Code).OrderBy(c => c),
            codigos.OrderBy(c => c));
    }

    [Fact]
    public async Task Semear_GravaAreasComOsPesosDaDefinicao()
    {
        await using (var ctx = CreateContext())
        {
            await PrepHubDbSeeder.SemearAsync(ctx);
        }

        var definicao = PrepHubDbSeeder.Exames[0];

        await using var leitura = CreateContext();
        var areas = await leitura.SkillAreas
            .Where(a => leitura.Exams.Any(e => e.Id == a.ExamId && e.Code == definicao.Code))
            .ToListAsync();

        Assert.Equal(definicao.Areas.Count, areas.Count);
        Assert.All(definicao.Areas, esperada =>
            Assert.Contains(areas, a => a.Key == esperada.Key && a.WeightPercent == esperada.WeightPercent));
    }

    /// <summary>
    /// Rodar o seed duas vezes não pode duplicar nada — ele roda a cada startup do container.
    /// </summary>
    [Fact]
    public async Task Semear_DuasVezes_NaoDuplicaExameNemArea()
    {
        await using (var ctx = CreateContext())
        {
            await PrepHubDbSeeder.SemearAsync(ctx);
        }

        await using (var ctx = CreateContext())
        {
            await PrepHubDbSeeder.SemearAsync(ctx);
        }

        await using var leitura = CreateContext();

        Assert.Equal(PrepHubDbSeeder.Exames.Count, await leitura.Exams.CountAsync());
        Assert.Equal(
            PrepHubDbSeeder.Exames.Sum(e => e.Areas.Count),
            await leitura.SkillAreas.CountAsync());
    }

    // ------------------------------------------------------- realinhamento com a definição

    /// <summary>
    /// Parâmetro de prova alterado no banco volta ao valor da definição no seed seguinte.
    /// </summary>
    /// <remarks>
    /// É a regressão que motivou <c>Exame.AtualizarDefinicao</c>. Sem ela, a definição valia só na
    /// primeira execução e ajustar o tamanho de uma prova exigia <c>UPDATE</c> à mão numa migration
    /// — foi o que a migration <c>SorteioDeQuestoes</c> teve de fazer pelo AZ-900. Com quatro
    /// exames em calibração, isso deixaria de ser eventual.
    /// </remarks>
    [Fact]
    public async Task Semear_ExameComParametrosDivergentes_RealinhaComADefinicao()
    {
        var definicao = PrepHubDbSeeder.Exames[0];

        await using (var ctx = CreateContext())
        {
            await PrepHubDbSeeder.SemearAsync(ctx);
        }

        // Simula um banco que ficou para trás: prova mais curta e tempo menor do que a definição.
        await using (var ctx = CreateContext())
        {
            var exame = await ctx.Exams.SingleAsync(e => e.Code == definicao.Code);
            exame.AtualizarDefinicao("Nome Antigo", timeLimitMinutes: 10, passingScorePercent: 50, totalQuestions: 5);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            await PrepHubDbSeeder.SemearAsync(ctx);
        }

        await using var leitura = CreateContext();
        var atual = await leitura.Exams.SingleAsync(e => e.Code == definicao.Code);

        Assert.Equal(definicao.Name, atual.Name);
        Assert.Equal(definicao.TimeLimitMinutes, atual.TimeLimitMinutes);
        Assert.Equal(definicao.PassingScorePercent, atual.PassingScorePercent);
        Assert.Equal(definicao.TotalQuestions, atual.TotalQuestions);
    }

    /// <summary>
    /// Peso de domínio alterado no banco também volta — é ele que reparte a prova no sorteio.
    /// </summary>
    [Fact]
    public async Task Semear_PesoDeAreaDivergente_RealinhaComADefinicao()
    {
        var definicao = PrepHubDbSeeder.Exames[0];
        var areaEsperada = definicao.Areas[0];

        await using (var ctx = CreateContext())
        {
            await PrepHubDbSeeder.SemearAsync(ctx);
        }

        await using (var ctx = CreateContext())
        {
            var area = await ctx.SkillAreas.FirstAsync(a => a.Key == areaEsperada.Key);
            area.Atualizar("Nome Antigo Do Domínio", weightPercent: 1m);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            await PrepHubDbSeeder.SemearAsync(ctx);
        }

        await using var leitura = CreateContext();
        var atual = await leitura.SkillAreas.FirstAsync(a => a.Key == areaEsperada.Key);

        Assert.Equal(areaEsperada.Name, atual.Name);
        Assert.Equal(areaEsperada.WeightPercent, atual.WeightPercent);
    }

    /// <summary>
    /// O Id do exame e das áreas é preservado no realinhamento.
    /// </summary>
    /// <remarks>
    /// Não é detalhe: as tentativas já feitas apontam para o <c>ExamId</c> e as questões para o
    /// <c>SkillAreaId</c>. Recriar em vez de atualizar arrastaria o histórico inteiro junto — ou
    /// esbarraria nos FKs <c>Restrict</c>, o que ao menos falharia alto em vez de calado.
    /// </remarks>
    [Fact]
    public async Task Semear_Realinhamento_PreservaIdsDoExameEDasAreas()
    {
        await using (var ctx = CreateContext())
        {
            await PrepHubDbSeeder.SemearAsync(ctx);
        }

        Guid exameId;
        List<Guid> areaIds;

        await using (var ctx = CreateContext())
        {
            exameId = await ctx.Exams.Select(e => e.Id).FirstAsync();
            areaIds = await ctx.SkillAreas.Select(a => a.Id).OrderBy(id => id).ToListAsync();
        }

        await using (var ctx = CreateContext())
        {
            await PrepHubDbSeeder.SemearAsync(ctx);
        }

        await using var leitura = CreateContext();

        Assert.Equal(exameId, await leitura.Exams.Select(e => e.Id).FirstAsync());
        Assert.Equal(areaIds, await leitura.SkillAreas.Select(a => a.Id).OrderBy(id => id).ToListAsync());
    }

    // ------------------------------------------------------------------- exame em construção

    /// <summary>
    /// O fornecedor declarado chega ao banco — é ele que escolhe a tela da prova e a escala da nota.
    /// </summary>
    /// <remarks>
    /// Falha calada se regredisse: um exame AWS gravado como Microsoft abriria na tela da
    /// Microsoft, com nota de 1 a 1000 e itens de ordenação caindo no arrastar — tudo funcionando.
    /// </remarks>
    [Fact]
    public async Task Semear_GravaOFornecedorDeCadaExame()
    {
        await using (var ctx = CreateContext())
        {
            await PrepHubDbSeeder.SemearAsync(ctx);
        }

        await using var leitura = CreateContext();
        var porCodigo = await leitura.Exams.ToDictionaryAsync(e => e.Code, e => e.Vendor);

        Assert.All(PrepHubDbSeeder.Exames, d => Assert.Equal(d.Fornecedor, porCodigo[d.Code]));
        Assert.Equal(Domain.Enums.FornecedorDoExame.Aws, porCodigo["AIF-C01"]);
    }

    /// <summary>
    /// O flag de publicação da definição chega ao banco como está declarado.
    /// </summary>
    [Fact]
    public async Task Semear_GravaOEstadoDePublicacaoDeCadaExame()
    {
        await using (var ctx = CreateContext())
        {
            await PrepHubDbSeeder.SemearAsync(ctx);
        }

        await using var leitura = CreateContext();
        var porCodigo = await leitura.Exams.ToDictionaryAsync(e => e.Code, e => e.IsPublished);

        Assert.All(PrepHubDbSeeder.Exames, d => Assert.Equal(d.Publicado, porCodigo[d.Code]));
    }

    /// <summary>
    /// Exame em construção é semeado normalmente — é isso que permite escrever o banco aos poucos.
    /// </summary>
    /// <remarks>
    /// O par que importa: ele PRECISA existir no banco (senão os lotes com aquele
    /// <c>exameCode</c> não teriam onde entrar, e a validação os rejeitaria como órfãos) e
    /// PRECISA ficar invisível no catálogo. Testar só um dos dois lados deixaria o outro livre
    /// para regredir.
    /// </remarks>
    /// <remarks>
    /// Escrito como invariante sobre a lista, e não sobre "o exame em construção": quando o AZ-104
    /// for publicado a asserção passa a valer sobre um conjunto vazio, o que é o resultado certo —
    /// em vez de o teste começar a falhar por ter ficado sem sujeito.
    /// </remarks>
    [Fact]
    public async Task Semear_ExamesEmConstrucao_ExistemNoBancoComSuasAreas()
    {
        await using (var ctx = CreateContext())
        {
            await PrepHubDbSeeder.SemearAsync(ctx);
        }

        await using var leitura = CreateContext();
        var noBanco = await leitura.Exams.Include(e => e.SkillAreas).ToListAsync();

        Assert.All(PrepHubDbSeeder.Exames.Where(d => !d.Publicado), definicao =>
        {
            var exame = Assert.Single(noBanco, e => e.Code == definicao.Code);
            Assert.False(exame.IsPublished);
            Assert.Equal(definicao.Areas.Count, exame.SkillAreas.Count);
        });
    }

    /// <summary>
    /// Publicar e despublicar segue a definição, nos dois sentidos.
    /// </summary>
    /// <remarks>
    /// O sentido que importa é o de VOLTA: se o seed só soubesse publicar, um exame que precisasse
    /// sair do ar às pressas (gabarito errado descoberto em produção) exigiria UPDATE manual no
    /// banco — exatamente o que <c>AtualizarDefinicao</c> existe para evitar.
    /// </remarks>
    [Fact]
    public async Task Semear_EstadoDePublicacaoDivergente_RealinhaNosDoisSentidos()
    {
        var definicao = PrepHubDbSeeder.Exames.First(e => e.Publicado);

        await using (var ctx = CreateContext())
        {
            await PrepHubDbSeeder.SemearAsync(ctx);
        }

        // Alguém tirou do ar direto no banco; o seed seguinte tem de repor o estado declarado.
        await using (var ctx = CreateContext())
        {
            var exame = await ctx.Exams.SingleAsync(e => e.Code == definicao.Code);
            exame.AtualizarDefinicao(
                definicao.Name,
                definicao.TimeLimitMinutes,
                definicao.PassingScorePercent,
                definicao.TotalQuestions,
                isPublished: false);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = CreateContext())
        {
            await PrepHubDbSeeder.SemearAsync(ctx);
        }

        await using var leitura = CreateContext();
        Assert.True((await leitura.Exams.SingleAsync(e => e.Code == definicao.Code)).IsPublished);
    }

    // --------------------------------------------------------------------- catálogo inválido

    /// <summary>
    /// Lote apontando para exame inexistente derruba o seed em vez de sumir.
    /// </summary>
    /// <remarks>
    /// A validação roda sobre o catálogo INTEIRO antes de tocar no banco, justamente porque este
    /// caso não pertence a exame nenhum: validando dentro do laço por exame, o lote não seria
    /// visitado por iteração alguma e o erro não teria onde aparecer.
    /// </remarks>
    [Fact]
    public void Validar_LoteDeExameInexistente_ImpedeOSeed()
    {
        var lote = new ArquivoDeQuestoes
        {
            Origem = "lote-orfao.json",
            ExameCode = "AZ-999",
            Area = "conceitos-de-nuvem",
            Questoes =
            [
                new QuestaoDeSeed
                {
                    Id = "az999-teste-01",
                    Tipo = "EscolhaUnica",
                    Enunciado = "Enunciado de teste que descreve um cenário aplicado qualquer.",
                    Explicacao = "A correta descreve o comportamento do serviço no cenário; a segunda " +
                                 "confunde com recurso parecido, a terceira resolve outro problema e a " +
                                 "quarta contraria a restrição enunciada.",
                    Opcoes =
                    [
                        new OpcaoDeSeed { Texto = "Primeira", Correta = true },
                        new OpcaoDeSeed { Texto = "Segunda", Correta = false },
                        new OpcaoDeSeed { Texto = "Terceira", Correta = false },
                        new OpcaoDeSeed { Texto = "Quarta", Correta = false }
                    ]
                }
            ]
        };

        var problemas = CatalogoDeQuestoesDeSeed.Validar([lote], PrepHubDbSeeder.AreasPorExame);

        Assert.Contains(problemas, p => p.Contains("lote-orfao.json") && p.Contains("AZ-999"));
    }
}
