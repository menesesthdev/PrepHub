using PrepHub.Domain.Entidades;
using PrepHub.Domain.Enums;
using PrepHub.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace PrepHub.Infrastructure.Tests;

/// <summary>
/// As migrations não eram exercitadas por teste nenhum: os testes de persistência usam
/// <c>EnsureCreated</c>, que constrói o banco direto do modelo e <b>pula as migrations</b>. Ou seja,
/// uma migration faltando, incompleta ou fora de sincronia com o snapshot passava por toda a suíte
/// e só aparecia no startup da aplicação — em produção, onde o banco é o que a migration fez dele,
/// e não o que o modelo diz.
/// </summary>
public sealed class MigracoesTests : IDisposable
{
    private readonly SqliteConnection _conexao;

    public MigracoesTests()
    {
        _conexao = new SqliteConnection("DataSource=:memory:");
        _conexao.Open();
    }

    public void Dispose() => _conexao.Dispose();

    private PrepHubDbContext NovoContexto()
        => new(new DbContextOptionsBuilder<PrepHubDbContext>().UseSqlite(_conexao).Options);

    /// <summary>
    /// Aplicar todas as migrations tem de produzir exatamente o modelo atual. É o mesmo diff que o
    /// <c>dotnet ef</c> faz ao avisar "pending model changes" — aqui ele vira teste, porque o aviso
    /// só aparece para quem roda a ferramenta, e o erro nasce justamente de quem não a rodou.
    /// </summary>
    [Fact]
    public void MigrationsAplicadas_ProduzemOModeloAtual()
    {
        using var ctx = NovoContexto();

        var differ = ctx.GetService<IMigrationsModelDiffer>();
        var snapshot = ctx.GetService<IMigrationsAssembly>().ModelSnapshot;
        Assert.NotNull(snapshot);

        var modeloDoSnapshot = ctx.GetService<IModelRuntimeInitializer>()
            .Initialize(((IMutableModel)snapshot!.Model).FinalizeModel(), designTime: true, validationLogger: null);

        var diferencas = differ.GetDifferences(
            modeloDoSnapshot.GetRelationalModel(),
            ctx.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        Assert.Empty(diferencas);
    }

    /// <summary>
    /// O banco resultante das migrations aceita de fato uma questão de arrastar e soltar — com o
    /// alvo do par candidato gravado e lido de volta.
    /// </summary>
    /// <remarks>
    /// O diff acima compara modelos; este grava. A coluna <c>TargetText</c> entrou por migration
    /// escrita à mão, e a diferença entre "o snapshot diz que existe" e "o SQLite tem a coluna" é
    /// exatamente onde uma migration manual erra.
    /// </remarks>
    [Fact]
    public async Task BancoMigrado_GuardaEDevolveOAlvoDoParCandidato()
    {
        await using (var ctx = NovoContexto())
        {
            await ctx.Database.MigrateAsync();
        }

        var exameId = Guid.NewGuid();
        var questaoId = Guid.NewGuid();

        await using (var ctx = NovoContexto())
        {
            var exame = new Exame("AZ-TESTE", "Exame de teste", 45, 70, 40, exameId);
            var area = exame.AdicionarAreaDeHabilidade("area-de-teste", "Área de teste", 100m);

            var questao = exame.AdicionarQuestao(
                area.Id,
                "az900-teste-migracao-assoc",
                "Associe cada requisito ao mecanismo adequado.",
                TipoDeQuestao.Associacao,
                "Explicação por distrator.",
                "Redundância",
                questaoId);

            questao.AdicionarOpcao("ZRS", isCorrect: true, orderIndex: 0, targetText: "Queda de um datacenter");
            questao.AdicionarOpcao("GRS", isCorrect: false, orderIndex: 1, targetText: "Queda de um datacenter");

            ctx.Exams.Add(exame);
            ctx.Questions.Add(questao);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = NovoContexto())
        {
            var opcoes = await ctx.AnswerOptions
                .Where(o => o.QuestionId == questaoId)
                .OrderBy(o => o.OrderIndex)
                .ToListAsync();

            Assert.All(opcoes, o => Assert.Equal("Queda de um datacenter", o.TargetText));
            Assert.Equal(new[] { "ZRS", "GRS" }, opcoes.Select(o => o.Text));
        }
    }

    /// <summary>
    /// Nos demais tipos a coluna continua nula — nulo é o que significa "esta alternativa não
    /// pertence a alvo nenhum", e é o que a migration deixou nas linhas que já existiam.
    /// </summary>
    [Fact]
    public async Task BancoMigrado_MantemAlvoNuloNosDemaisTipos()
    {
        await using (var ctx = NovoContexto())
        {
            await ctx.Database.MigrateAsync();
        }

        var questaoId = Guid.NewGuid();

        await using (var ctx = NovoContexto())
        {
            var exame = new Exame("AZ-TESTE-2", "Exame de teste", 45, 70, 40);
            var area = exame.AdicionarAreaDeHabilidade("area-de-teste", "Área de teste", 100m);

            var questao = exame.AdicionarQuestao(
                area.Id,
                "az900-teste-migracao-unica",
                "Qual abordagem atende ao requisito?",
                TipoDeQuestao.EscolhaUnica,
                "Explicação por distrator.",
                "Redundância",
                questaoId);

            questao.AdicionarOpcao("Correta", isCorrect: true, orderIndex: 0);

            ctx.Exams.Add(exame);
            ctx.Questions.Add(questao);
            await ctx.SaveChangesAsync();
        }

        await using (var contexto = NovoContexto())
        {
            var opcao = await contexto.AnswerOptions.SingleAsync(o => o.QuestionId == questaoId);

            Assert.Null(opcao.TargetText);
        }
    }
}
