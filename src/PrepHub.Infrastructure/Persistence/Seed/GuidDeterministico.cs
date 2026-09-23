using System.Security.Cryptography;
using System.Text;

namespace PrepHub.Infrastructure.Persistence.Seed;

/// <summary>
/// Deriva um Guid estável a partir de uma chave textual.
///
/// É o que permite escrever questão em arquivo sem inventar Guid à mão: a chave legível do
/// arquivo (<c>az900-nuvem-capex-03</c>) sempre produz o mesmo Id, então reimportar um lote
/// atualiza a questão existente em vez de criar uma cópia — e as respostas já gravadas, que
/// apontam para o Id da alternativa, continuam válidas.
/// </summary>
/// <remarks>
/// Não é UUIDv5 canônico (não segue o layout big-endian da RFC) e não precisa ser: o requisito
/// é ser determinístico e não colidir, não ser interoperável com outro gerador. Trocar este
/// algoritmo mudaria todos os Ids derivados — o que equivale a recriar o banco de questões.
/// </remarks>
public static class GuidDeterministico
{
    public static Guid DeChave(string chave)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chave);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(chave));
        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>Id da alternativa na posição informada. A posição faz parte da chave.</summary>
    public static Guid DeOpcao(string externalIdDaQuestao, int orderIndex)
        => DeChave($"{externalIdDaQuestao}#opcao#{orderIndex}");
}
