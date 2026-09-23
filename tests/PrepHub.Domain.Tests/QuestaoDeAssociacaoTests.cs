using PrepHub.Domain.Entidades;
using PrepHub.Domain.Enums;
using PrepHub.Domain.Sorteio;

namespace PrepHub.Domain.Tests;

/// <summary>
/// O tipo <see cref="TipoDeQuestao.Associacao"/> não trouxe regra de correção nova — e é
/// justamente isso que precisa ficar provado. Cada alternativa é um par candidato (alvo × item),
/// então "acertou" continua sendo igualdade de conjuntos de Ids. Se algum dia alguém trocar essa
/// modelagem por um formato de resposta próprio, são estes testes que vão apontar o que quebrou.
/// </summary>
public class QuestaoDeAssociacaoTests
{
    private const string AlvoA = "Sobreviver à falha de um rack";
    private const string AlvoB = "Sobreviver à perda do datacenter";
    private const string AlvoC = "Sobreviver à perda da região";

    private static readonly string[] Itens = { "Availability set", "Zonas de disponibilidade", "Outra região", "Escalar instâncias" };

    /// <summary>Gabarito: A→Availability set, B→Zonas, C→Outra região.</summary>
    private static readonly Dictionary<string, string> Gabarito = new()
    {
        [AlvoA] = "Availability set",
        [AlvoB] = "Zonas de disponibilidade",
        [AlvoC] = "Outra região"
    };

    /// <summary>
    /// Monta a questão como o seed a grava: um par para cada combinação de alvo com item.
    /// </summary>
    private static Questao QuestaoDeAssociacao()
    {
        var questao = new Questao(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "az900-teste-assoc",
            "Associe cada requisito ao mecanismo adequado.",
            TipoDeQuestao.Associacao,
            "Explicação por distrator.");

        var indice = 0;
        foreach (var alvo in new[] { AlvoA, AlvoB, AlvoC })
        {
            foreach (var item in Itens)
            {
                questao.AdicionarOpcao(item, Gabarito[alvo] == item, indice++, targetText: alvo);
            }
        }

        return questao;
    }

    private static Guid Par(Questao questao, string alvo, string item)
        => questao.Options.First(o => o.TargetText == alvo && o.Text == item).Id;

    [Fact]
    public void TodosOsAlvosCertos_EstaCorreta()
    {
        var questao = QuestaoDeAssociacao();

        var resposta = Gabarito.Select(p => Par(questao, p.Key, p.Value));

        Assert.True(questao.RespondidaCorretamentePor(resposta));
    }

    [Fact]
    public void UmAlvoTrocado_EstaIncorreta()
    {
        var questao = QuestaoDeAssociacao();

        var resposta = new[]
        {
            Par(questao, AlvoA, "Availability set"),
            Par(questao, AlvoB, "Outra região"), // troca B e C
            Par(questao, AlvoC, "Zonas de disponibilidade")
        };

        Assert.False(questao.RespondidaCorretamentePor(resposta));
    }

    [Fact]
    public void AlvoDeixadoEmBranco_EstaIncorreta()
    {
        // Não há crédito parcial: a correção é a mesma igualdade de conjuntos da múltipla escolha.
        var questao = QuestaoDeAssociacao();

        var resposta = new[]
        {
            Par(questao, AlvoA, "Availability set"),
            Par(questao, AlvoB, "Zonas de disponibilidade")
        };

        Assert.False(questao.RespondidaCorretamentePor(resposta));
    }

    [Fact]
    public void QuantidadeDeSelecoesExigidas_EIgualAoNumeroDeAlvos()
    {
        // É esta contagem que a tela usa para dizer "Completo" ou "Incompleto" na tela de revisão:
        // um alvo vazio tem de aparecer como item incompleto, não como item pronto.
        var questao = QuestaoDeAssociacao();

        Assert.Equal(3, questao.CorrectOptionIds.Count);
    }

    [Fact]
    public void Embaralhamento_PreservaTodosOsParesEOsAlvos()
    {
        // As duas colunas da tela são derivadas dos pares embaralhados. Perder um par apagaria uma
        // combinação possível da tela — e se o par perdido fosse o correto, a questão ficaria sem
        // resposta certa, sem que nada reclamasse.
        var questao = QuestaoDeAssociacao();
        var tentativa = Guid.NewGuid();

        var embaralhada = OrdemDasOpcoes.Para(questao, tentativa);

        Assert.Equal(questao.Options.Count, embaralhada.Count);
        Assert.Equal(questao.Options.Select(o => o.Id).ToHashSet(), embaralhada.Select(o => o.Id).ToHashSet());
        Assert.Equal(3, embaralhada.Select(o => o.TargetText).Distinct().Count());
        Assert.Equal(Itens.Length, embaralhada.Select(o => o.Text).Distinct().Count());
    }

    [Fact]
    public void Embaralhamento_NaoDeixaOGabaritoNoInicioDeCadaAlvo()
    {
        // O seed escreve o par correto em primeiro dentro de cada alvo. Se a ordem do arquivo
        // chegasse à tela, o primeiro item do painel seria a resposta do primeiro alvo — e o
        // padrão apareceria já na segunda prova.
        var questao = QuestaoDeAssociacao();

        var alguemSaiuDoLugar = Enumerable.Range(0, 40)
            .Select(_ => OrdemDasOpcoes.Para(questao, Guid.NewGuid()))
            .Any(ordem => ordem[0].Id != questao.Options.First().Id);

        Assert.True(alguemSaiuDoLugar);
    }
}
