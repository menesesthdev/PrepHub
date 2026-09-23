using PrepHub.Domain.Common;

namespace PrepHub.Domain.Entidades;

/// <summary>
/// Uma alternativa de resposta de uma questão. <see cref="IsCorrect"/> nunca é exposto
/// ao candidato durante a prova — só na tela de revisão pós-resultado.
/// </summary>
public class OpcaoDeResposta : Entity
{
    // Construtor exigido pelo EF Core.
    private OpcaoDeResposta()
    {
    }

    public OpcaoDeResposta(
        Guid questionId,
        string text,
        bool isCorrect,
        int orderIndex,
        Guid? id = null,
        string? targetText = null)
        : base(id ?? Guid.NewGuid())
    {
        QuestionId = questionId;
        Text = Guard.NotNullOrWhiteSpace(text, nameof(text));
        IsCorrect = isCorrect;
        OrderIndex = orderIndex;
        TargetText = Normalizar(targetText);
    }

    public Guid QuestionId { get; private set; }

    public string Text { get; private set; } = string.Empty;

    /// <summary>
    /// Alvo ao qual esta alternativa se refere nas questões de arrastar e soltar — nulo em
    /// todos os outros tipos.
    /// </summary>
    /// <remarks>
    /// Numa questão <see cref="Enums.TipoDeQuestao.Associacao"/> a alternativa deixa de ser "uma
    /// resposta" e passa a ser um <b>par candidato</b>: <see cref="TargetText"/> é o alvo (a
    /// coluna da direita) e <see cref="Text"/> é o item arrastável. Existe uma alternativa para
    /// cada combinação possível, e só a combinação certa tem <see cref="IsCorrect"/>. É o que
    /// permite responder "arrastando" sem inventar um segundo formato de resposta: a seleção
    /// continua sendo um conjunto de Ids de alternativa, exatamente como nos outros tipos.
    /// </remarks>
    public string? TargetText { get; private set; }

    public bool IsCorrect { get; private set; }

    /// <summary>Ordem de exibição estável — evita depender da ordem de inserção no banco.</summary>
    public int OrderIndex { get; private set; }

    /// <summary>
    /// Reaplica o conteúdo vindo do arquivo de seed. Corrige texto e gabarito preservando o Id —
    /// respostas já gravadas apontam para este Id, então recriar a alternativa as tornaria órfãs.
    /// </summary>
    public void Atualizar(string text, bool isCorrect, string? targetText = null)
    {
        Text = Guard.NotNullOrWhiteSpace(text, nameof(text));
        IsCorrect = isCorrect;
        TargetText = Normalizar(targetText);
    }

    private static string? Normalizar(string? valor)
        => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
