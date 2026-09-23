using PrepHub.Domain.Entidades;
using PrepHub.Domain.Enums;

namespace PrepHub.Domain.Sorteio;

/// <summary>
/// Ordem em que as opções de uma questão são apresentadas numa tentativa.
///
/// Os arquivos de seed escrevem a alternativa correta primeiro — é o que mantém o JSON legível e
/// revisável. Apresentar nessa ordem, porém, ensina a marcar a primeira opção: o padrão aparece já
/// na segunda ou terceira prova e destrói o valor do simulado, porque acertar deixa de exigir
/// entender o conceito. Engenharia de distrator não resolve isso — nenhuma alternativa errada é
/// plausível o bastante para competir com "é sempre a de cima".
/// </summary>
/// <remarks>
/// A permutação é <b>derivada</b> do par (tentativa, opção), não sorteada na hora: a mesma
/// tentativa vê sempre a mesma ordem — navegar e voltar, recarregar a página ou abrir o gabarito
/// depois não remexem as alternativas — e cada nova tentativa vê outra. Guardar a ordem numa
/// coluna seria a alternativa; derivar dispensa migration, não ocupa memória entre requisições e
/// não tem como dessincronizar do que foi respondido, já que a resposta gravada aponta para o Id
/// da opção e não para a posição dela.
/// </remarks>
public static class OrdemDasOpcoes
{
    /// <summary>
    /// As opções de <paramref name="questao"/> na ordem de apresentação da tentativa
    /// <paramref name="tentativaId"/>.
    /// </summary>
    /// <remarks>
    /// Sim/Não fica de fora de propósito: ali as opções não são alternativas concorrentes, e sim um
    /// par fixo ("Sim" antes de "Não", "Verdadeiro" antes de "Falso"). Embaralhá-lo não esconde
    /// gabarito nenhum — são só duas, ambas lidas de qualquer jeito — e ainda pareceria defeito de
    /// tela.
    /// </remarks>
    public static IReadOnlyList<OpcaoDeResposta> Para(Questao questao, Guid tentativaId)
    {
        ArgumentNullException.ThrowIfNull(questao);

        var opcoes = questao.Options.ToList(); // já vêm ordenadas por OrderIndex

        if (questao.Type == TipoDeQuestao.SimNao || opcoes.Count < 2)
        {
            return opcoes;
        }

        // Ordenar por chave derivada equivale a embaralhar: as chaves são independentes entre si e
        // bem distribuídas, então toda permutação é igualmente provável. O desempate por OrderIndex
        // só valeria numa colisão de 64 bits, mas mantém o resultado determinístico em qualquer caso.
        return opcoes
            .OrderBy(o => Chave(tentativaId, o.Id))
            .ThenBy(o => o.OrderIndex)
            .ToList();
    }

    /// <summary>
    /// Chave de ordenação de uma opção dentro de uma tentativa: FNV-1a sobre os 32 bytes dos dois
    /// Guids, com a finalização do splitmix64 por cima.
    /// </summary>
    /// <remarks>
    /// A finalização não é enfeite: o FNV-1a difunde mal os bits altos, e é justamente por eles que
    /// a ordenação começa a comparar — sem ela, entradas parecidas sairiam próximas e a ordem
    /// "embaralhada" tenderia a repetir a do arquivo, que é o defeito que se está corrigindo.
    /// Não é hash criptográfico e não precisa ser: prever a posição da alternativa certa exigiria
    /// saber qual é ela, e quem sabe qual é não precisa da posição.
    /// </remarks>
    private static ulong Chave(Guid tentativaId, Guid opcaoId)
    {
        Span<byte> bytes = stackalloc byte[32];
        tentativaId.TryWriteBytes(bytes[..16]);
        opcaoId.TryWriteBytes(bytes[16..]);

        var hash = 14695981039346656037UL; // offset basis do FNV-1a de 64 bits
        foreach (var b in bytes)
        {
            hash = (hash ^ b) * 1099511628211UL; // primo do FNV-1a
        }

        hash ^= hash >> 30;
        hash *= 0xBF58476D1CE4E5B9UL;
        hash ^= hash >> 27;
        hash *= 0x94D049BB133111EBUL;
        return hash ^ (hash >> 31);
    }
}
