using PrepHub.Domain.Entidades;

namespace PrepHub.Domain.Correcao;

/// <summary>
/// Correção pura de uma tentativa. Sem dependência de banco ou de tempo — recebe as questões
/// do exame e as respostas dadas e devolve o placar. Isso é o coração testável do domínio.
/// </summary>
public static class CorretorDeProva
{
    /// <summary>
    /// Corrige a tentativa. O total considerado é sempre o total de questões do exame
    /// (questão não respondida conta como errada), replicando a regra real da prova.
    /// </summary>
    public static PlacarDaProva Corrigir(
        Exame exam,
        IReadOnlyCollection<Questao> questions,
        IReadOnlyCollection<RespostaDaTentativa> answers)
    {
        ArgumentNullException.ThrowIfNull(exam);
        ArgumentNullException.ThrowIfNull(questions);
        ArgumentNullException.ThrowIfNull(answers);

        var selectionByQuestion = answers.ToDictionary(
            a => a.QuestionId,
            a => a.SelectedOptionIds);

        var totalQuestions = questions.Count;
        var totalCorrect = 0;
        var skillAreaScores = new List<PlacarPorArea>();

        foreach (var group in questions.GroupBy(q => q.SkillAreaId))
        {
            var areaTotal = 0;
            var areaCorrect = 0;

            foreach (var question in group)
            {
                areaTotal++;

                if (selectionByQuestion.TryGetValue(question.Id, out var selection)
                    && question.RespondidaCorretamentePor(selection))
                {
                    areaCorrect++;
                    totalCorrect++;
                }
            }

            var skillArea = exam.SkillAreas.FirstOrDefault(s => s.Id == group.Key);
            skillAreaScores.Add(new PlacarPorArea(
                group.Key,
                skillArea?.Name ?? "—",
                skillArea?.WeightPercent ?? 0m,
                areaTotal,
                areaCorrect));
        }

        var scorePercent = totalQuestions == 0
            ? 0m
            : Math.Round((decimal)totalCorrect / totalQuestions * 100m, 1);

        var passed = scorePercent >= exam.PassingScorePercent;

        return new PlacarDaProva(
            totalQuestions,
            totalCorrect,
            scorePercent,
            passed,
            skillAreaScores,
            EscalaDeNota.Converter(scorePercent, exam.PassingScorePercent, EscalaDeNota.NotaMinimaPara(exam.Vendor)));
    }
}
