namespace PrepHub.Application.Abstractions;

/// <summary>
/// Gera o token do link de redefinição e calcula o hash pelo qual ele é procurado no banco.
/// </summary>
/// <remarks>
/// Separado de <see cref="IHasherDeSenha"/> porque o problema é outro. Senha é segredo de
/// baixa entropia escolhido por humano, então precisa de derivação LENTA e com salt aleatório.
/// Token é aleatório e longo — não há dicionário que o alcance, e o hash precisa ser
/// DETERMINÍSTICO para servir de chave de busca (com salt aleatório não daria para procurar).
/// Usar PBKDF2 aqui só tornaria cada validação de link caríssima sem ganho nenhum.
/// </remarks>
public interface IGeradorDeTokenSeguro
{
    /// <summary>Token novo, seguro para viajar numa URL.</summary>
    string Gerar();

    /// <summary>Hash determinístico do token, para comparar com o que está guardado.</summary>
    string Hash(string token);
}
