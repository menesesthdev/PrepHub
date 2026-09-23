using System.Security.Cryptography;
using PrepHub.Application.Abstractions;

namespace PrepHub.Infrastructure.Seguranca;

/// <summary>
/// Hash de senha com PBKDF2-HMAC-SHA256, salt por senha e comparação em tempo constante.
/// </summary>
/// <remarks>
/// PBKDF2 e não SHA-256 puro: senha precisa de derivação LENTA, senão uma GPU testa bilhões
/// de candidatas por segundo sobre o banco vazado. PBKDF2 vem no .NET e não pede pacote extra
/// (Argon2id seria mais resistente a ataque paralelo, mas exigiria dependência nativa —
/// desproporcional aqui, e é troca possível depois graças ao campo de versão no formato).
///
/// O hash guardado carrega os próprios parâmetros: <c>pbkdf2-sha256$iteracoes$salt$hash</c>,
/// com salt e hash em Base64. Sem isso, subir o fator de trabalho invalidaria todas as senhas
/// já cadastradas; com isso, hash antigo continua verificável e a migração pode ser gradual.
/// </remarks>
public sealed class HasherDeSenhaPbkdf2 : IHasherDeSenha
{
    private const string Prefixo = "pbkdf2-sha256";

    /// <summary>Fator de trabalho atual (recomendação OWASP para PBKDF2-HMAC-SHA256).</summary>
    private const int IteracoesPadrao = 600_000;

    private const int TamanhoDoSalt = 16;   // 128 bits
    private const int TamanhoDaChave = 32;  // 256 bits, igual ao bloco do SHA-256

    private static readonly char[] Separador = ['$'];

    public string Hash(string senha)
    {
        ArgumentNullException.ThrowIfNull(senha);

        var salt = RandomNumberGenerator.GetBytes(TamanhoDoSalt);
        var chave = Derivar(senha, salt, IteracoesPadrao);

        return string.Join('$',
            Prefixo,
            IteracoesPadrao.ToString(),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(chave));
    }

    public bool Verificar(string senha, string hash)
    {
        if (senha is null || string.IsNullOrWhiteSpace(hash))
        {
            return false;
        }

        // Hash ilegível (formato antigo, registro corrompido, migração manual) devolve false:
        // "não confere" é a resposta certa, e uma exceção aqui viraria erro 500 no login.
        var partes = hash.Split(Separador);
        if (partes.Length != 4
            || partes[0] != Prefixo
            || !int.TryParse(partes[1], out var iteracoes)
            || iteracoes <= 0)
        {
            return false;
        }

        byte[] salt;
        byte[] esperado;
        try
        {
            salt = Convert.FromBase64String(partes[2]);
            esperado = Convert.FromBase64String(partes[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length == 0 || esperado.Length == 0)
        {
            return false;
        }

        var calculado = Derivar(senha, salt, iteracoes, esperado.Length);

        // FixedTimeEquals e não SequenceEqual: comparação que para no primeiro byte diferente
        // vaza, pelo tempo de resposta, quanto do hash foi acertado.
        return CryptographicOperations.FixedTimeEquals(calculado, esperado);
    }

    private static byte[] Derivar(string senha, byte[] salt, int iteracoes, int tamanho = TamanhoDaChave)
        => Rfc2898DeriveBytes.Pbkdf2(senha, salt, iteracoes, HashAlgorithmName.SHA256, tamanho);
}
