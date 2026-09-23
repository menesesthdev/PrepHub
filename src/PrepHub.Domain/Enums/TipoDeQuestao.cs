namespace PrepHub.Domain.Enums;

/// <summary>
/// Formatos de questão suportados — todos publicamente documentados pela Microsoft
/// como parte do formato de prova. Replicamos o formato, nunca o conteúdo real.
/// </summary>
public enum TipoDeQuestao
{
    /// <summary>Uma única resposta correta (radio button).</summary>
    EscolhaUnica = 0,

    /// <summary>Duas ou mais respostas corretas (checkbox, "selecione N").</summary>
    EscolhaMultipla = 1,

    /// <summary>Afirmação avaliada como Verdadeiro/Falso ou Sim/Não.</summary>
    SimNao = 2,

    /// <summary>
    /// Arrastar e soltar: itens de um painel são levados a alvos (categorias, descrições,
    /// lacunas), um item por alvo.
    /// </summary>
    /// <remarks>
    /// No banco, cada alternativa é um <b>par candidato</b> (alvo × item): a combinação certa é a
    /// marcada como correta, e responder é selecionar um par por alvo. Modelar assim mantém a
    /// correção, a gravação da resposta e o embaralhamento exatamente como já eram — igualdade de
    /// conjuntos de Ids de alternativa —, em vez de exigir uma segunda forma de resposta que todo
    /// caminho do sistema teria de aprender a tratar.
    /// </remarks>
    Associacao = 3,

    /// <summary>
    /// Ordenação: escolher, entre os passos listados, os que resolvem a tarefa e colocá-los na
    /// sequência certa ("Etapa 1", "Etapa 2"...). Formato documentado da AWS; sobram passos que
    /// não entram em etapa nenhuma.
    /// </summary>
    /// <remarks>
    /// Mesmo modelo de <see cref="Associacao"/>, sem nada novo no banco: cada etapa é um alvo, cada
    /// passo é um item, e existe um par candidato por combinação. A diferença é só de apresentação
    /// — os alvos têm ordem natural e a tela não pode embaralhá-los.
    /// </remarks>
    Ordenacao = 4
}
