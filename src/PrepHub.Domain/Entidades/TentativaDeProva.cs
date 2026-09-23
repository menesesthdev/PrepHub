using PrepHub.Domain.Common;

namespace PrepHub.Domain.Entidades;

/// <summary>
/// Uma tentativa (sessão) de prova. Nasce ao clicar "Iniciar Simulado" e é finalizada
/// manualmente ou por esgotamento do tempo. Guarda o placar consolidado após a correção.
/// </summary>
public class TentativaDeProva : Entity
{
    private readonly List<RespostaDaTentativa> _answers = new();
    private readonly List<QuestaoDaTentativa> _questions = new();

    // Construtor exigido pelo EF Core.
    private TentativaDeProva()
    {
    }

    public TentativaDeProva(Guid examId, Guid userId, DateTime startedAt, Guid? id = null)
        : base(id ?? Guid.NewGuid())
    {
        ExamId = examId;
        UserId = Guard.NotEmpty(userId, nameof(userId));
        StartedAt = startedAt;
    }

    public Guid ExamId { get; private set; }

    /// <summary>Dono da tentativa. Obrigatório — o simulado exige login.</summary>
    public Guid UserId { get; private set; }

    public DateTime StartedAt { get; private set; }

    public DateTime? FinishedAt { get; private set; }

    public decimal? ScorePercent { get; private set; }

    public bool? Passed { get; private set; }

    public IReadOnlyCollection<RespostaDaTentativa> Answers => _answers;

    /// <summary>As questões sorteadas para esta tentativa, na ordem de apresentação.</summary>
    public IReadOnlyList<QuestaoDaTentativa> Questions
        => _questions.OrderBy(q => q.OrderIndex).ToList();

    public bool IsFinished => FinishedAt is not null;

    /// <summary>
    /// Fixa a composição da prova. Chamado uma única vez, no início da tentativa, com o resultado
    /// do sorteio. Depois disso a prova é imutável: mudar as questões no meio significaria
    /// corrigir o candidato por itens que ele não viu.
    /// </summary>
    public IReadOnlyList<QuestaoDaTentativa> DefinirQuestoes(IEnumerable<Guid> questionIds)
    {
        ArgumentNullException.ThrowIfNull(questionIds);
        GarantirNaoConcluida();

        if (_questions.Count > 0)
        {
            throw new InvalidOperationException("As questões desta tentativa já foram definidas.");
        }

        var index = 0;
        foreach (var questionId in questionIds.Distinct())
        {
            _questions.Add(new QuestaoDaTentativa(Id, questionId, index++));
        }

        if (_questions.Count == 0)
        {
            throw new ArgumentException("Uma tentativa precisa de ao menos uma questão.", nameof(questionIds));
        }

        return _questions;
    }

    /// <summary>
    /// Cria ou atualiza a resposta de uma questão. Idempotente por questão — refletir a
    /// navegação livre da prova, onde o candidato volta e altera respostas à vontade.
    /// </summary>
    public RespostaDaTentativa DefinirResposta(
        Guid questionId,
        IEnumerable<Guid> selectedOptionIds,
        bool flagged,
        int addedTimeSpentSeconds = 0)
    {
        GarantirNaoConcluida();

        var answer = _answers.FirstOrDefault(a => a.QuestionId == questionId);
        if (answer is null)
        {
            answer = new RespostaDaTentativa(Id, questionId);
            _answers.Add(answer);
        }

        answer.DefinirSelecao(selectedOptionIds);
        answer.DefinirMarcacao(flagged);
        answer.AdicionarTempoGasto(addedTimeSpentSeconds);
        return answer;
    }

    public void AlternarMarcacao(Guid questionId, bool flagged)
    {
        GarantirNaoConcluida();

        var answer = _answers.FirstOrDefault(a => a.QuestionId == questionId);
        if (answer is null)
        {
            answer = new RespostaDaTentativa(Id, questionId);
            _answers.Add(answer);
        }

        answer.DefinirMarcacao(flagged);
    }

    /// <summary>
    /// Fecha a tentativa gravando o placar já calculado pelo domínio de correção.
    /// Chamar mais de uma vez é no-op — o primeiro fechamento vale.
    /// </summary>
    public void Concluir(decimal scorePercent, bool passed, DateTime finishedAt)
    {
        if (IsFinished)
        {
            return;
        }

        ScorePercent = Guard.InRange(scorePercent, 0m, 100m, nameof(scorePercent));
        Passed = passed;
        FinishedAt = finishedAt;
    }

    private void GarantirNaoConcluida()
    {
        if (IsFinished)
        {
            throw new InvalidOperationException("A tentativa já foi finalizada e não pode ser alterada.");
        }
    }
}
