using PrepHub.Domain.Common;

namespace PrepHub.Domain.Entidades;

/// <summary>
/// Representa uma certificação/exame (ex.: AZ-900). O modelo é genérico de propósito:
/// nada aqui é hardcoded para o AZ-900, para suportar futuros exames (AZ-104, AI-900...).
/// </summary>
public class Exame : Entity
{
    private readonly List<AreaDeHabilidade> _skillAreas = new();
    private readonly List<Questao> _questions = new();

    // Construtor exigido pelo EF Core (materialização).
    private Exame()
    {
    }

    public Exame(
        string code,
        string name,
        int timeLimitMinutes,
        int passingScorePercent,
        int totalQuestions,
        Guid? id = null,
        bool isPublished = true,
        Enums.FornecedorDoExame vendor = Enums.FornecedorDoExame.Microsoft)
        : base(id ?? Guid.NewGuid())
    {
        Code = Guard.NotNullOrWhiteSpace(code, nameof(code));
        Name = Guard.NotNullOrWhiteSpace(name, nameof(name));
        TimeLimitMinutes = Guard.Positive(timeLimitMinutes, nameof(timeLimitMinutes));
        PassingScorePercent = Guard.InRange(passingScorePercent, 0, 100, nameof(passingScorePercent));
        TotalQuestions = Guard.Positive(totalQuestions, nameof(totalQuestions));
        IsPublished = isPublished;
        Vendor = vendor;
    }

    /// <summary>
    /// Quem emite a certificação — decide escala da nota, regras de formato e a tela da prova.
    /// </summary>
    public Enums.FornecedorDoExame Vendor { get; private set; } = Enums.FornecedorDoExame.Microsoft;

    /// <summary>Código oficial do exame (ex.: "AZ-900").</summary>
    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public int TimeLimitMinutes { get; private set; }

    /// <summary>Percentual mínimo para aprovação (ex.: 70).</summary>
    public int PassingScorePercent { get; private set; }

    /// <summary>Quantidade de questões que compõem uma tentativa deste exame.</summary>
    public int TotalQuestions { get; private set; }

    /// <summary>
    /// Se o exame está disponível para os candidatos. Exame <b>em construção</b> existe no banco,
    /// recebe questões e pode ser testado, mas não aparece no catálogo nem aceita nova tentativa.
    /// </summary>
    /// <remarks>
    /// Existe porque escrever um banco de questões é trabalho de semanas e os arquivos de seed
    /// exigem um <c>exameCode</c> que corresponda a um exame declarado — sem este estado
    /// intermediário, um exame só poderia entrar no sistema já pronto, e as centenas de questões
    /// teriam de ser escritas sem nunca serem exercitadas pelo seed nem pelos testes.
    ///
    /// ⚠️ Não é cosmético: o sorteio entrega uma prova mais curta quando o pool não cobre
    /// <see cref="TotalQuestions"/> (<c>Math.Min</c>), sem erro nenhum. Deixar um exame incompleto
    /// visível publicaria uma prova curta e sempre parecida — e fidelidade à prova real é o
    /// produto. Por isso o bloqueio é imposto também ao INICIAR a tentativa, não só na listagem:
    /// esconder o botão não impede quem tem o id.
    /// </remarks>
    public bool IsPublished { get; private set; } = true;

    public IReadOnlyCollection<AreaDeHabilidade> SkillAreas => _skillAreas;

    public IReadOnlyCollection<Questao> Questions => _questions;

    /// <summary>
    /// Reescreve os parâmetros do exame no lugar, preservando o Id. O <see cref="Code"/> é a
    /// identidade e não muda — para trocá-lo, o exame é outro.
    /// </summary>
    /// <remarks>
    /// Existe para que a definição no seeder seja a fonte única da verdade também DEPOIS da
    /// primeira execução. Sem isso, ajustar o tamanho ou o tempo de uma prova exigiria um
    /// <c>UPDATE</c> escrito à mão numa migration — foi exatamente o que a migration
    /// <c>SorteioDeQuestoes</c> teve de fazer para corrigir o <see cref="TotalQuestions"/> do
    /// AZ-900. Com quatro exames em calibração, isso deixaria de ser eventual e viraria rotina.
    /// </remarks>
    public void AtualizarDefinicao(
        string name,
        int timeLimitMinutes,
        int passingScorePercent,
        int totalQuestions,
        bool isPublished = true,
        Enums.FornecedorDoExame? vendor = null)
    {
        Name = Guard.NotNullOrWhiteSpace(name, nameof(name));
        TimeLimitMinutes = Guard.Positive(timeLimitMinutes, nameof(timeLimitMinutes));
        PassingScorePercent = Guard.InRange(passingScorePercent, 0, 100, nameof(passingScorePercent));
        TotalQuestions = Guard.Positive(totalQuestions, nameof(totalQuestions));
        IsPublished = isPublished;

        // Nulo preserva o atual: quem só quer republicar (os testes, por exemplo) não precisa
        // repetir o fornecedor e não corre o risco de transformar um exame AWS em Microsoft.
        Vendor = vendor ?? Vendor;
    }

    public AreaDeHabilidade AdicionarAreaDeHabilidade(string key, string name, decimal weightPercent, Guid? id = null)
    {
        var skillArea = new AreaDeHabilidade(Id, key, name, weightPercent, id);
        _skillAreas.Add(skillArea);
        return skillArea;
    }

    /// <summary>
    /// Adiciona uma questão ao exame. As questões fazem parte do agregado Exame — é aqui que a
    /// coleção <see cref="Questions"/> é a fonte da verdade (o EF a repovoa via Include ao ler).
    /// </summary>
    public Questao AdicionarQuestao(
        Guid skillAreaId,
        string externalId,
        string text,
        Enums.TipoDeQuestao type,
        string explanation,
        string? topic = null,
        Guid? id = null)
    {
        var question = new Questao(Id, skillAreaId, externalId, text, type, explanation, topic, id);
        _questions.Add(question);
        return question;
    }
}
