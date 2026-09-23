using PrepHub.Domain.Common;
using PrepHub.Domain.Enums;

namespace PrepHub.Domain.Entidades;

/// <summary>
/// Questão do banco. Sempre original, escrita a partir do Skills Measured outline público —
/// nunca reproduz conteúdo real de prova. A <see cref="Explanation"/> deve justificar por que
/// cada distrator está errado, não só o gabarito.
/// </summary>
public class Questao : Entity
{
    private readonly List<OpcaoDeResposta> _options = new();

    // Construtor exigido pelo EF Core.
    private Questao()
    {
    }

    public Questao(
        Guid examId,
        Guid skillAreaId,
        string externalId,
        string text,
        TipoDeQuestao type,
        string explanation,
        string? topic = null,
        Guid? id = null)
        : base(id ?? Guid.NewGuid())
    {
        ExamId = examId;
        SkillAreaId = skillAreaId;
        ExternalId = Guard.NotNullOrWhiteSpace(externalId, nameof(externalId));
        Text = Guard.NotNullOrWhiteSpace(text, nameof(text));
        Type = type;
        Explanation = Guard.NotNullOrWhiteSpace(explanation, nameof(explanation));
        Topic = string.IsNullOrWhiteSpace(topic) ? null : topic.Trim();
    }

    public Guid ExamId { get; private set; }

    public Guid SkillAreaId { get; private set; }

    /// <summary>
    /// Chave estável vinda do arquivo de seed (ex.: "az900-nuvem-resp-compartilhada-01"). É ela
    /// que torna o seed idempotente: reimportar um lote atualiza a questão existente em vez de
    /// criar uma cópia, e o Guid da questão — derivado desta chave — não muda entre importações,
    /// o que preserva o histórico de tentativas que já a referenciam.
    /// </summary>
    public string ExternalId { get; private set; } = string.Empty;

    public string Text { get; private set; } = string.Empty;

    public TipoDeQuestao Type { get; private set; }

    public string Explanation { get; private set; } = string.Empty;

    /// <summary>
    /// Assunto específico dentro do domínio (ex.: "Zonas de disponibilidade"). Serve para medir
    /// cobertura do banco e, no futuro, para estudo dirigido por tópico.
    /// </summary>
    public string? Topic { get; private set; }

    /// <summary>
    /// Se a questão ainda entra no sorteio de novas provas.
    /// </summary>
    /// <remarks>
    /// Aposentar em vez de apagar é obrigatório, não preferência: tentativas já feitas apontam
    /// para esta questão e para as alternativas dela, então apagar a linha destruiria o histórico
    /// (e o FK <c>Restrict</c> de <c>ExamAttemptQuestions</c> nem deixaria). Uma questão retirada
    /// dos arquivos de seed vira inativa; se voltar ao arquivo, volta a valer, com o mesmo Id.
    /// </remarks>
    public bool IsActive { get; private set; } = true;

    public IReadOnlyCollection<OpcaoDeResposta> Options => _options.OrderBy(o => o.OrderIndex).ToList();

    public OpcaoDeResposta AdicionarOpcao(
        string text,
        bool isCorrect,
        int orderIndex,
        Guid? id = null,
        string? targetText = null)
    {
        var option = new OpcaoDeResposta(Id, text, isCorrect, orderIndex, id, targetText);
        _options.Add(option);
        return option;
    }

    /// <summary>
    /// Reaplica o conteúdo vindo do arquivo de seed, preservando o Id da questão. É o que permite
    /// corrigir um enunciado ou uma explicação sem invalidar as tentativas que já a responderam.
    /// </summary>
    /// <remarks>
    /// O domínio (<paramref name="skillAreaId"/>) entra aqui de propósito: mover a questão de
    /// arquivo é como se corrige uma classificação errada, e fidelidade ao blueprint é a garantia
    /// nº 1 do sorteio. Se este método não reescrevesse a área, a questão mal classificada
    /// continuaria contando para a cota do domínio errado para sempre.
    /// </remarks>
    public void Atualizar(Guid skillAreaId, string text, TipoDeQuestao type, string explanation, string? topic)
    {
        SkillAreaId = Guard.NotEmpty(skillAreaId, nameof(skillAreaId));
        Text = Guard.NotNullOrWhiteSpace(text, nameof(text));
        Type = type;
        Explanation = Guard.NotNullOrWhiteSpace(explanation, nameof(explanation));
        Topic = string.IsNullOrWhiteSpace(topic) ? null : topic.Trim();
    }

    /// <summary>Tira a questão do sorteio de novas provas, sem tocar no histórico.</summary>
    public void Aposentar() => IsActive = false;

    /// <summary>Devolve ao sorteio uma questão aposentada — o caminho de volta de <see cref="Aposentar"/>.</summary>
    public void Reativar() => IsActive = true;

    /// <summary>Busca uma alternativa pela posição — usada pelo seed para atualizar no lugar.</summary>
    public OpcaoDeResposta? OpcaoNaPosicao(int orderIndex)
        => _options.FirstOrDefault(o => o.OrderIndex == orderIndex);

    /// <summary>Ids das alternativas corretas desta questão.</summary>
    public IReadOnlyCollection<Guid> CorrectOptionIds
        => _options.Where(o => o.IsCorrect).Select(o => o.Id).ToList();

    /// <summary>
    /// Uma questão está correta quando o conjunto de alternativas selecionadas é
    /// exatamente igual ao conjunto de alternativas corretas — nem a mais, nem a menos.
    /// Vale para single choice, múltipla escolha e Sim/Não.
    /// </summary>
    public bool RespondidaCorretamentePor(IEnumerable<Guid> selectedOptionIds)
    {
        var selected = selectedOptionIds.Distinct().ToHashSet();
        var correct = CorrectOptionIds.ToHashSet();
        return selected.SetEquals(correct);
    }
}
