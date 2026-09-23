namespace PrepHub.Application.Contracts;

/// <summary>
/// Uma questão que já apareceu para o usuário numa tentativa recente.
/// </summary>
/// <param name="TentativasAtras">1 = tentativa mais recente, 2 = a anterior, e assim por diante.</param>
/// <param name="SelectedOptionIds">
/// O que a pessoa marcou. Vazio quando a questão caiu mas não foi respondida.
/// </param>
/// <param name="TentativaConcluida">
/// Se a tentativa em que a questão caiu chegou ao fim. Distingue os dois motivos de não haver
/// resposta: numa prova encerrada, item em branco é erro (é assim que a correção conta); numa
/// prova abandonada, é item que a pessoa nunca chegou a ler — e mandá-lo para a fila de reforço
/// afogaria os erros de verdade.
/// </param>
public sealed record QuestaoVistaDto(
    Guid QuestaoId,
    int TentativasAtras,
    IReadOnlyCollection<Guid> SelectedOptionIds,
    bool TentativaConcluida);
