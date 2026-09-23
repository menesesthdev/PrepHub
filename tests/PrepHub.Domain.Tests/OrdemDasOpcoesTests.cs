using PrepHub.Domain.Entidades;
using PrepHub.Domain.Enums;
using PrepHub.Domain.Sorteio;

namespace PrepHub.Domain.Tests;

public class OrdemDasOpcoesTests
{
    private static Questao Questao(TipoDeQuestao tipo, params string[] textos)
    {
        var questao = new Questao(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "az900-teste-ordem",
            "Enunciado.",
            tipo,
            "Explicação.");

        for (var i = 0; i < textos.Length; i++)
        {
            // Correta sempre em primeiro, como nos arquivos de seed — é o viés que se está corrigindo.
            questao.AdicionarOpcao(textos[i], isCorrect: i == 0, orderIndex: i);
        }

        return questao;
    }

    private static Questao QuatroAlternativas()
        => Questao(TipoDeQuestao.EscolhaUnica, "A", "B", "C", "D");

    [Fact]
    public void MesmaTentativa_DevolveSempreAMesmaOrdem()
    {
        var questao = QuatroAlternativas();
        var tentativa = Guid.NewGuid();

        var primeira = OrdemDasOpcoes.Para(questao, tentativa).Select(o => o.Id).ToList();

        // Navegar e voltar, recarregar a página e abrir o gabarito passam todos por aqui: se a
        // ordem mudasse entre chamadas, a alternativa marcada "trocaria de lugar" na tela.
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(primeira, OrdemDasOpcoes.Para(questao, tentativa).Select(o => o.Id));
        }
    }

    [Fact]
    public void NaoPerdeNemDuplicaAlternativa()
    {
        var questao = QuatroAlternativas();

        var ordenadas = OrdemDasOpcoes.Para(questao, Guid.NewGuid());

        Assert.Equal(
            questao.Options.Select(o => o.Id).OrderBy(id => id),
            ordenadas.Select(o => o.Id).OrderBy(id => id));
    }

    [Fact]
    public void SimNao_MantemAOrdemCanonica()
    {
        var questao = Questao(TipoDeQuestao.SimNao, "Sim", "Não");

        // Vale para 500 tentativas diferentes: "Não" antes de "Sim" pareceria tela quebrada, e
        // embaralhar duas alternativas que o candidato lê inteiras não esconde gabarito nenhum.
        for (var i = 0; i < 500; i++)
        {
            var textos = OrdemDasOpcoes.Para(questao, Guid.NewGuid()).Select(o => o.Text);
            Assert.Equal(new[] { "Sim", "Não" }, textos);
        }
    }

    [Fact]
    public void TentativasDiferentes_EspalhamACorretaPorTodasAsPosicoes()
    {
        var questao = QuatroAlternativas();
        var correta = questao.Options.Single(o => o.IsCorrect).Id;
        var ocorrencias = new int[4];

        const int tentativas = 4_000;
        for (var i = 0; i < tentativas; i++)
        {
            var ordenadas = OrdemDasOpcoes.Para(questao, Guid.NewGuid());
            ocorrencias[ordenadas.ToList().FindIndex(o => o.Id == correta)]++;
        }

        // O defeito original era 100% na posição 0. Uniforme seria 25% em cada; a faixa de 15% a
        // 35% fica a mais de 3 desvios-padrão da borda, então o teste não pisca por azar.
        Assert.All(ocorrencias, n => Assert.InRange(n, tentativas * 0.15, tentativas * 0.35));
    }

    [Fact]
    public void EscolhaMultipla_SeparaAsCorretasQueOArquivoEscreveuJuntas()
    {
        var questao = new Questao(
            Guid.NewGuid(), Guid.NewGuid(), "az900-teste-multipla", "Enunciado.",
            TipoDeQuestao.EscolhaMultipla, "Explicação.");

        // Formato dos arquivos de seed: as duas corretas em primeiro.
        questao.AdicionarOpcao("Correta 1", true, 0);
        questao.AdicionarOpcao("Correta 2", true, 1);
        questao.AdicionarOpcao("Errada 1", false, 2);
        questao.AdicionarOpcao("Errada 2", false, 3);

        var vezesNasDuasPrimeiras = 0;
        for (var i = 0; i < 600; i++)
        {
            var posicoes = OrdemDasOpcoes.Para(questao, Guid.NewGuid())
                .Select((o, indice) => (o.IsCorrect, indice))
                .Where(x => x.IsCorrect)
                .Select(x => x.indice)
                .ToList();

            if (posicoes.All(p => p < 2))
            {
                vezesNasDuasPrimeiras++;
            }
        }

        // Com 4 alternativas, "as duas corretas em cima" é 1 das 6 combinações — perto de 100 em
        // 600. Se o embaralhamento não pegasse a múltipla escolha, seriam as 600.
        Assert.InRange(vezesNasDuasPrimeiras, 40, 180);
    }

    [Fact]
    public void UmaAlternativaSo_NaoQuebra()
    {
        var questao = Questao(TipoDeQuestao.EscolhaUnica, "Única");

        Assert.Single(OrdemDasOpcoes.Para(questao, Guid.NewGuid()));
    }
}
