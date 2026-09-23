namespace PrepHub.Domain.Sorteio;

/// <summary>
/// Uma questão candidata ao sorteio. Deliberadamente magra: o sorteio nunca precisa do
/// enunciado nem das alternativas, então o pool inteiro cabe na memória mesmo com milhares
/// de questões no banco.
/// </summary>
/// <param name="Topico">
/// Assunto dentro do domínio. O sorteio o usa para espalhar a cota do domínio entre assuntos
/// diferentes — sem isso, nada impediria 5 das 11 questões de "conceitos de nuvem" caírem todas
/// sobre CapEx/OpEx. Nulo é tratado como um assunto próprio, o "sem tópico declarado".
/// </param>
public sealed record QuestaoSorteavel(Guid QuestaoId, Guid AreaId, string? Topico = null);

/// <summary>Área de habilidade com seu peso no exame, usada para distribuir as cotas.</summary>
public sealed record AreaSorteavel(Guid AreaId, decimal WeightPercent);

/// <summary>
/// Registro de que o usuário já viu uma questão.
/// </summary>
/// <param name="TentativasAtras">
/// 1 = tentativa mais recente, 2 = a anterior, e assim por diante. É distância em tentativas,
/// não em dias: quem faz dez simulados numa tarde tem a mesma proteção de quem faz um por semana.
/// </param>
/// <param name="Acertou">
/// Resultado daquela vez — e <c>null</c> quando não há resultado a apurar: a questão foi
/// apresentada numa tentativa que o usuário abandonou sem responder. Os três estados importam
/// porque só o <c>false</c> alimenta a fila de reforço. Tratar o abandono como erro faria uma
/// tentativa fechada no item 1 despejar 40 questões nunca lidas na fila de "o que você errou",
/// que é justamente a informação mais valiosa do histórico.
/// </param>
public sealed record HistoricoDeQuestao(Guid QuestaoId, int TentativasAtras, bool? Acertou);
