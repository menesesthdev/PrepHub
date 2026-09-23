using PrepHub.Domain.Entidades;
using PrepHub.Domain.Sorteio;

namespace PrepHub.Application.Abstractions;

/// <summary>
/// Porta de acesso aos exames e seu conteúdo. Implementada na Infrastructure (EF Core).
/// </summary>
public interface IExameRepository
{
    Task<IReadOnlyList<Exame>> ObterTodosAsync(CancellationToken cancellationToken = default);

    Task<Exame?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Exame com as áreas de habilidade, sem questões. É o que a correção e o placar por domínio precisam.</summary>
    Task<Exame?> ObterComAreasAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exame com skill areas, questões e opções carregadas.
    /// </summary>
    /// <remarks>
    /// ⚠️ Carrega o banco de questões INTEIRO. Com centenas de questões isso é caro, então não use
    /// no fluxo da prova — lá o certo é <see cref="ObterQuestoesAsync"/> com os ids sorteados.
    /// Continua existindo para o seed, para a validação do banco e para tentativas antigas,
    /// anteriores ao sorteio, cuja prova era literalmente "todas as questões do exame".
    /// </remarks>
    Task<Exame?> ObterComConteudoAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Projeção mínima (id + área) de todas as questões de um exame, para alimentar o sorteio.
    /// Não traz enunciado nem alternativas: o pool inteiro precisa caber na memória barato.
    /// </summary>
    Task<IReadOnlyList<QuestaoSorteavel>> ObterPoolAsync(Guid examId, CancellationToken cancellationToken = default);

    /// <summary>Questões com suas alternativas, pelos ids — o caminho quente da prova.</summary>
    Task<IReadOnlyList<Questao>> ObterQuestoesAsync(
        IReadOnlyCollection<Guid> questionIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Alternativas corretas das questões informadas. Serve ao sorteio, que precisa saber o que o
    /// usuário errou antes sem carregar enunciado e explicação de tudo que ele já respondeu.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> ObterOpcoesCorretasAsync(
        IReadOnlyCollection<Guid> questionIds,
        CancellationToken cancellationToken = default);
}
