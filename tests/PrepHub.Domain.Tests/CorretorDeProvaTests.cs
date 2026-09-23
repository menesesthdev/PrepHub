using PrepHub.Domain.Entidades;
using PrepHub.Domain.Enums;
using PrepHub.Domain.Correcao;

namespace PrepHub.Domain.Tests;

public class CorretorDeProvaTests
{
    // Monta um exame de 4 questões em 2 skill areas, retornando os ids necessários para responder.
    private static (Exame exam, List<Questao> questions) BuildExam()
    {
        var exam = new Exame("AZ-900", "Azure Fundamentals", timeLimitMinutes: 45, passingScorePercent: 70, totalQuestions: 4);
        var areaCloud = exam.AdicionarAreaDeHabilidade("conceitos-de-nuvem", "Conceitos de nuvem", 30m);
        var areaGov = exam.AdicionarAreaDeHabilidade("governanca", "Gestão e governança", 30m);

        var questions = new List<Questao>();
        for (var i = 0; i < 2; i++)
        {
            var q = new Questao(exam.Id, areaCloud.Id, $"cloud-{i}", $"Cloud {i}", TipoDeQuestao.EscolhaUnica, "exp");
            q.AdicionarOpcao("certa", true, 0);
            q.AdicionarOpcao("errada", false, 1);
            questions.Add(q);
        }

        for (var i = 0; i < 2; i++)
        {
            var q = new Questao(exam.Id, areaGov.Id, $"gov-{i}", $"Gov {i}", TipoDeQuestao.EscolhaUnica, "exp");
            q.AdicionarOpcao("certa", true, 0);
            q.AdicionarOpcao("errada", false, 1);
            questions.Add(q);
        }

        return (exam, questions);
    }

    private static RespostaDaTentativa Answer(Guid attemptId, Questao question, bool correct)
    {
        var answer = new RespostaDaTentativa(attemptId, question.Id);
        var option = question.Options.First(o => o.IsCorrect == correct);
        answer.DefinirSelecao(new[] { option.Id });
        return answer;
    }

    [Fact]
    public void Grade_AllCorrect_Is100PercentAndPassed()
    {
        var (exam, questions) = BuildExam();
        var attemptId = Guid.NewGuid();
        var answers = questions.Select(q => Answer(attemptId, q, correct: true)).ToList();

        var score = CorretorDeProva.Corrigir(exam, questions, answers);

        Assert.Equal(4, score.TotalQuestions);
        Assert.Equal(4, score.CorrectAnswers);
        Assert.Equal(100m, score.ScorePercent);
        Assert.True(score.Passed);
    }

    [Fact]
    public void Grade_HalfCorrect_Is50PercentAndFailed()
    {
        var (exam, questions) = BuildExam();
        var attemptId = Guid.NewGuid();
        var answers = new List<RespostaDaTentativa>
        {
            Answer(attemptId, questions[0], correct: true),
            Answer(attemptId, questions[1], correct: false),
            Answer(attemptId, questions[2], correct: true),
            Answer(attemptId, questions[3], correct: false),
        };

        var score = CorretorDeProva.Corrigir(exam, questions, answers);

        Assert.Equal(50m, score.ScorePercent);
        Assert.False(score.Passed);
    }

    [Fact]
    public void Grade_UnansweredQuestions_CountAsWrong()
    {
        var (exam, questions) = BuildExam();
        var attemptId = Guid.NewGuid();
        // Responde só uma das quatro.
        var answers = new List<RespostaDaTentativa> { Answer(attemptId, questions[0], correct: true) };

        var score = CorretorDeProva.Corrigir(exam, questions, answers);

        Assert.Equal(4, score.TotalQuestions);
        Assert.Equal(1, score.CorrectAnswers);
        Assert.Equal(25m, score.ScorePercent);
        Assert.False(score.Passed);
    }

    [Fact]
    public void Grade_ExactlyAtPassingThreshold_Passes()
    {
        // Exame com corte em 75% e 4 questões: 3 certas = 75% deve aprovar (>=).
        var exam = new Exame("AZ-900", "Azure Fundamentals", 45, passingScorePercent: 75, totalQuestions: 4);
        var area = exam.AdicionarAreaDeHabilidade("area", "Área", 100m);
        var questions = Enumerable.Range(0, 4).Select(i =>
        {
            var q = new Questao(exam.Id, area.Id, $"q-{i}", $"Q{i}", TipoDeQuestao.EscolhaUnica, "exp");
            q.AdicionarOpcao("certa", true, 0);
            q.AdicionarOpcao("errada", false, 1);
            return q;
        }).ToList();

        var attemptId = Guid.NewGuid();
        var answers = new List<RespostaDaTentativa>
        {
            Answer(attemptId, questions[0], true),
            Answer(attemptId, questions[1], true),
            Answer(attemptId, questions[2], true),
            Answer(attemptId, questions[3], false),
        };

        var score = CorretorDeProva.Corrigir(exam, questions, answers);

        Assert.Equal(75m, score.ScorePercent);
        Assert.True(score.Passed);
    }

    [Fact]
    public void Grade_ProducesPerSkillAreaBreakdown()
    {
        var (exam, questions) = BuildExam();
        var attemptId = Guid.NewGuid();
        var answers = new List<RespostaDaTentativa>
        {
            Answer(attemptId, questions[0], correct: true),   // Cloud
            Answer(attemptId, questions[1], correct: true),   // Cloud
            Answer(attemptId, questions[2], correct: false),  // Gov
            Answer(attemptId, questions[3], correct: false),  // Gov
        };

        var score = CorretorDeProva.Corrigir(exam, questions, answers);

        Assert.Equal(2, score.SkillAreas.Count);
        var cloud = score.SkillAreas.Single(s => s.SkillAreaName == "Conceitos de nuvem");
        var gov = score.SkillAreas.Single(s => s.SkillAreaName == "Gestão e governança");
        Assert.Equal(100m, cloud.ScorePercent);
        Assert.Equal(0m, gov.ScorePercent);
    }
}
