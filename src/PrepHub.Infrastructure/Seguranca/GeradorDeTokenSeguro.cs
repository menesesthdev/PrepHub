using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using PrepHub.Application.Abstractions;

namespace PrepHub.Infrastructure.Seguranca;

/// <summary>
/// Token aleatório de 256 bits em Base64 URL-safe, com hash SHA-256 para guardar no banco.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RandomNumberGenerator"/> e não <c>Random</c>: o segundo é previsível a partir de
/// algumas saídas, o que aqui significaria conseguir adivinhar o link de redefinição de outra
/// pessoa.
/// </para>
/// <para>
/// SHA-256 puro é a escolha certa PARA TOKEN, ao contrário de senha (que usa PBKDF2 lento).
/// São 256 bits sorteados: não existe dicionário nem força bruta viável, então o ganho de um
/// hash lento seria zero — e o hash precisa ser determinístico para servir de chave de busca.
/// </para>
/// <para>
/// Base64Url porque o valor viaja em querystring: o Base64 comum usa <c>+</c> e <c>/</c>, que
/// mudam de significado numa URL e chegariam corrompidos do outro lado.
/// </para>
/// </remarks>
public sealed class GeradorDeTokenSeguro : IGeradorDeTokenSeguro
{
    private const int TamanhoEmBytes = 32; // 256 bits

    public string Gerar()
        => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TamanhoEmBytes));

    public string Hash(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }
}
