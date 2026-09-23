using PrepHub.Application.Exames;
using PrepHub.Application.Observabilidade;
using PrepHub.Application.Sessoes;
using PrepHub.Application.Tests.Fakes;
using PrepHub.Domain.Entidades;
using PrepHub.Domain.Enums;

namespace PrepHub.Application.Tests;

/// <summary>
/// Um exame em construção tem de estar invisível nas DUAS pontas: fora do catálogo e recusando
/// tentativa. Só a primeira seria proteção de fachada — o id do exame trafega no formulário de
/// "Iniciar simulado", então esconder o botão não impede quem já tem o id.
/// </summary>
/// <remarks>
/// O sintoma que isto evita não é uma exceção: é o sorteio entregando uma prova de meia dúzia de
/// itens (<c>Math.Min(total, pool)</c>), sempre a mesma, com cara de prova normal. Numa suíte que
/// não testasse isso, publicar um exame pela metade passaria verde.
/// </remarks>
public class ExameEmConstrucaoTests
{
    private static Exame ComQuestoes(Exame exam, int quantas)
    {
        var area = exam.AdicionarAreaDeHabilidade("conceitos-de-nuvem", "Conceitos de nuvem", 100m);

        foreach (var i in Enumerable.Range(0, quantas))
        {
            var q = exam.AdicionarQuestao(area.Id, $"{exam.Code}-q{i}", $"Questão {i}", TipoDeQuestao.EscolhaUnica, "Explicação.");
            q.AdicionarOpcao("Correta", true, 0);
            q.AdicionarOpcao("Errada", false, 1);
        }

        return exam;
    }

    private static Exame Publicado() => ComQuestoes(
        new Exame("AZ-900", "Azure Fundamentals", timeLimitMinutes: 45, passingScorePercent: 70, totalQuestions: 4),
        quantas: 4);

    private static Exame EmConstrucao() => ComQuestoes(
        new Exame("AZ-104", "Azure Administrator", timeLimitMinutes: 100, passingScorePercent: 70,
            totalQuestions: 50, id: null, isPublished: false),
        quantas: 4);

    private static SessaoDeProvaService Sessao(Exame exam)
    {
        var exames = new InMemoryExamRepository(exam);

        return new SessaoDeProvaService(
            exames,
            new InMemoryExamAttemptRepository(),
            new FakeSorteadorDeQuestoes(exames),
            new FakeUnitOfWork(),
            new FixedClock(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)),
            new FakeUsuarioAtual(),
            MetricasDeNegocioSilenciosas.Instancia);
    }

    // ------------------------------------------------------------------------------ catálogo

    [Fact]
    public async Task Catalogo_OmiteExameEmConstrucao()
    {
        var catalogo = new CatalogoDeExamesService(
            new InMemoryExamRepository(Publicado(), EmConstrucao()));

        var disponiveis = await catalogo.ObterExamesDisponiveisAsync();

        Assert.Equal(["AZ-900"], disponiveis.Select(e => e.Code));
    }

    [Fact]
    public async Task Catalogo_TodosEmConstrucao_DevolveVazio()
    {
        var catalogo = new CatalogoDeExamesService(new InMemoryExamRepository(EmConstrucao()));

        Assert.Empty(await catalogo.ObterExamesDisponiveisAsync());
    }

    [Fact]
    public async Task ExameDisponivel_EmConstrucao_DevolveNulo()
    {
        var exame = EmConstrucao();
        var catalogo = new CatalogoDeExamesService(new InMemoryExamRepository(exame));

        Assert.Null(await catalogo.ObterExameDisponivelAsync(exame.Id));
    }

    [Fact]
    public async Task ExameDisponivel_Publicado_DevolveOResumoComOFornecedor()
    {
        var exame = ComQuestoes(
            new Exame("AIF-C01", "AWS Certified AI Practitioner", 90, 70, 65, vendor: FornecedorDoExame.Aws),
            quantas: 1);
        var catalogo = new CatalogoDeExamesService(new InMemoryExamRepository(exame));

        var resumo = await catalogo.ObterExameDisponivelAsync(exame.Id);

        Assert.NotNull(resumo);
        Assert.Equal(FornecedorDoExame.Aws, resumo.Vendor);
    }

    // -------------------------------------------------------------------- início da tentativa

    /// <summary>
    /// Conhecer o id não basta: iniciar tentativa de exame em construção é recusado na Application.
    /// </summary>
    /// <remarks>
    /// O exame deste cenário TEM questões de propósito — se a recusa dependesse de o pool estar
    /// vazio, ela deixaria de valer justamente quando o banco começasse a encher, que é quando o
    /// exame fica atraente de espiar e ainda está longe de pronto.
    /// </remarks>
    [Fact]
    public async Task IniciarTentativa_ExameEmConstrucao_ERecusada()
    {
        var exame = EmConstrucao();

        var erro = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Sessao(exame).IniciarTentativaAsync(exame.Id));

        Assert.Contains("em construção", erro.Message);
    }

    [Fact]
    public async Task IniciarTentativa_ExamePublicado_Prossegue()
    {
        var exame = Publicado();

        Assert.NotEqual(Guid.Empty, await Sessao(exame).IniciarTentativaAsync(exame.Id));
    }
}
