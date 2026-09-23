using PrepHub.Infrastructure.Seguranca;

namespace PrepHub.Infrastructure.Tests;

public class GeradorDeTokenSeguroTests
{
    private readonly GeradorDeTokenSeguro _gerador = new();

    // O token viaja em querystring: qualquer caractere que precise de escape chegaria
    // corrompido do outro lado e o link morreria sem motivo aparente.
    [Fact]
    public void Gerar_ProduzTokenSeguroParaUrl()
    {
        var token = _gerador.Gerar();

        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);

        // 32 bytes em Base64Url dão 43 caracteres — entropia suficiente para não ser adivinhado.
        Assert.Equal(43, token.Length);
    }

    [Fact]
    public void Gerar_NaoRepeteTokens()
    {
        var tokens = Enumerable.Range(0, 200).Select(_ => _gerador.Gerar()).ToList();

        Assert.Equal(tokens.Count, tokens.Distinct().Count());
    }

    // Ao contrário do hash de senha, este PRECISA ser determinístico: é a chave pela qual o
    // token é procurado no banco.
    [Fact]
    public void Hash_EhDeterministicoEDiferentePorToken()
    {
        var token = _gerador.Gerar();

        Assert.Equal(_gerador.Hash(token), _gerador.Hash(token));
        Assert.NotEqual(_gerador.Hash(token), _gerador.Hash(_gerador.Gerar()));
    }

    [Fact]
    public void Hash_NaoDevolveOTokenOriginal()
    {
        var token = _gerador.Gerar();

        Assert.DoesNotContain(token, _gerador.Hash(token));
    }
}
