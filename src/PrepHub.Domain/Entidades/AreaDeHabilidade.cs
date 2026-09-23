using PrepHub.Domain.Common;

namespace PrepHub.Domain.Entidades;

/// <summary>
/// Domínio de habilidades do exame, com seu peso (ex.: "Descrever conceitos de nuvem — 25-30%").
/// Os pesos vêm do Skills Measured outline oficial da Microsoft — não são inventados.
/// </summary>
public class AreaDeHabilidade : Entity
{
    private readonly List<Questao> _questions = new();

    // Construtor exigido pelo EF Core.
    private AreaDeHabilidade()
    {
    }

    public AreaDeHabilidade(Guid examId, string key, string name, decimal weightPercent, Guid? id = null)
        : base(id ?? Guid.NewGuid())
    {
        ExamId = examId;
        Key = Guard.NotNullOrWhiteSpace(key, nameof(key));
        Name = Guard.NotNullOrWhiteSpace(name, nameof(name));
        WeightPercent = Guard.InRange(weightPercent, 0m, 100m, nameof(weightPercent));
    }

    public Guid ExamId { get; private set; }

    /// <summary>
    /// Slug estável da área (ex.: "conceitos-de-nuvem"). É por ele que os arquivos de seed
    /// referenciam o domínio — o <see cref="Name"/> é texto de UI e pode ser reescrito sem
    /// quebrar nada, o Key não.
    /// </summary>
    public string Key { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    /// <summary>Peso do domínio no exame, em pontos percentuais (ex.: 27.5).</summary>
    public decimal WeightPercent { get; private set; }

    public IReadOnlyCollection<Questao> Questions => _questions;

    /// <summary>
    /// Reescreve nome e peso no lugar. A <see cref="Key"/> não muda — é por ela que os arquivos
    /// de questões acham o domínio, e trocá-la órfãria o lote inteiro em silêncio.
    /// </summary>
    /// <remarks>
    /// O peso é o que o sorteio usa para repartir a prova entre os domínios. A Microsoft revisa os
    /// pesos do Skills Measured sem trocar o código do exame, então isto não é hipótese remota:
    /// sem poder atualizar, o simulado seguiria montando provas com o blueprint antigo — e a falha
    /// seria calada, porque uma prova com distribuição errada continua parecendo uma prova normal.
    /// </remarks>
    public void Atualizar(string name, decimal weightPercent)
    {
        Name = Guard.NotNullOrWhiteSpace(name, nameof(name));
        WeightPercent = Guard.InRange(weightPercent, 0m, 100m, nameof(weightPercent));
    }
}
