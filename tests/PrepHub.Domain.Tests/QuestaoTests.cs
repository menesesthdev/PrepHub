using PrepHub.Domain.Entidades;
using PrepHub.Domain.Enums;

namespace PrepHub.Domain.Tests;

public class QuestaoTests
{
    private static Questao BuildMultipleChoiceQuestion(out Guid correctA, out Guid correctB, out Guid wrong)
    {
        var question = new Questao(Guid.NewGuid(), Guid.NewGuid(), "teste-multipla", "Selecione duas opções válidas.", TipoDeQuestao.EscolhaMultipla, "Explicação.");
        correctA = question.AdicionarOpcao("Correta A", isCorrect: true, orderIndex: 0).Id;
        correctB = question.AdicionarOpcao("Correta B", isCorrect: true, orderIndex: 1).Id;
        wrong = question.AdicionarOpcao("Errada", isCorrect: false, orderIndex: 2).Id;
        return question;
    }

    [Fact]
    public void IsAnsweredCorrectlyBy_ExactMatch_ReturnsTrue()
    {
        var question = BuildMultipleChoiceQuestion(out var a, out var b, out _);

        Assert.True(question.RespondidaCorretamentePor(new[] { a, b }));
    }

    [Fact]
    public void IsAnsweredCorrectlyBy_OrderIndependent_ReturnsTrue()
    {
        var question = BuildMultipleChoiceQuestion(out var a, out var b, out _);

        Assert.True(question.RespondidaCorretamentePor(new[] { b, a }));
    }

    [Fact]
    public void IsAnsweredCorrectlyBy_MissingOneCorrect_ReturnsFalse()
    {
        var question = BuildMultipleChoiceQuestion(out var a, out _, out _);

        Assert.False(question.RespondidaCorretamentePor(new[] { a }));
    }

    [Fact]
    public void IsAnsweredCorrectlyBy_IncludesWrongOption_ReturnsFalse()
    {
        var question = BuildMultipleChoiceQuestion(out var a, out var b, out var wrong);

        Assert.False(question.RespondidaCorretamentePor(new[] { a, b, wrong }));
    }

    [Fact]
    public void IsAnsweredCorrectlyBy_NoSelection_ReturnsFalse()
    {
        var question = BuildMultipleChoiceQuestion(out _, out _, out _);

        Assert.False(question.RespondidaCorretamentePor(Array.Empty<Guid>()));
    }

    [Fact]
    public void IsAnsweredCorrectlyBy_DuplicateSelection_IsIgnored()
    {
        var question = BuildMultipleChoiceQuestion(out var a, out var b, out _);

        // Selecionar a mesma opção duas vezes não deve invalidar a resposta.
        Assert.True(question.RespondidaCorretamentePor(new[] { a, b, a }));
    }

    [Fact]
    public void Options_AreReturnedInOrderIndexOrder()
    {
        var question = new Questao(Guid.NewGuid(), Guid.NewGuid(), "teste-ordem", "Pergunta", TipoDeQuestao.EscolhaUnica, "Explicação.");
        question.AdicionarOpcao("Terceira", isCorrect: false, orderIndex: 2);
        question.AdicionarOpcao("Primeira", isCorrect: true, orderIndex: 0);
        question.AdicionarOpcao("Segunda", isCorrect: false, orderIndex: 1);

        var texts = question.Options.Select(o => o.Text).ToArray();

        Assert.Equal(new[] { "Primeira", "Segunda", "Terceira" }, texts);
    }
}
