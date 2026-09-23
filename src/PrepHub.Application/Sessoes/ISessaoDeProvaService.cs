using PrepHub.Application.Contracts;

namespace PrepHub.Application.Sessoes;

/// <summary>
/// Orquestra o ciclo de vida de uma tentativa de prova — o coração do produto.
/// Toda a regra de correção fica no domínio; aqui só coordenamos repositórios e mapeamos DTOs.
/// </summary>
public interface ISessaoDeProvaService
{
    /// <summary>Inicia uma nova tentativa e devolve o Id dela.</summary>
    Task<Guid> IniciarTentativaAsync(Guid examId, CancellationToken cancellationToken = default);

    /// <summary>Estado geral da tentativa (header, tempo restante, painel de navegação).</summary>
    Task<EstadoDaTentativaDto?> ObterEstadoAsync(Guid attemptId, CancellationToken cancellationToken = default);

    /// <summary>Uma questão específica pela posição (1-based) para renderização.</summary>
    Task<QuestaoDto?> ObterQuestaoAsync(Guid attemptId, int number, CancellationToken cancellationToken = default);

    /// <summary>
    /// Grava/atualiza a resposta de uma questão. Devolve o desfecho para o Web traduzir em
    /// status HTTP — em especial, recusa questão ou alternativa que não pertençam a esta prova.
    /// </summary>
    Task<ResultadoDeSalvarResposta> SalvarRespostaAsync(
        SalvarRespostaRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Finaliza a tentativa, corrige e devolve o resultado. Idempotente.</summary>
    Task<ResultadoDaProvaDto?> FinalizarTentativaAsync(Guid attemptId, CancellationToken cancellationToken = default);

    /// <summary>Resultado de uma tentativa já finalizada (ou null se ainda em andamento).</summary>
    Task<ResultadoDaProvaDto?> ObterResultadoAsync(Guid attemptId, CancellationToken cancellationToken = default);
}
