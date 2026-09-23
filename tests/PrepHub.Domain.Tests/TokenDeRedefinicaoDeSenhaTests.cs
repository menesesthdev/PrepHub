using PrepHub.Domain.Autenticacao;
using PrepHub.Domain.Entidades;

namespace PrepHub.Domain.Tests;

public class TokenDeRedefinicaoDeSenhaTests
{
    private static readonly DateTime Agora = new(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);

    private static TokenDeRedefinicaoDeSenha Novo() => new(Guid.NewGuid(), "hash-do-token", Agora);

    [Fact]
    public void NasceUtilizavelEComPrazoDaPolitica()
    {
        var token = Novo();

        Assert.True(token.EstaUtilizavel(Agora));
        Assert.Equal(Agora.Add(PoliticaDeRedefinicaoDeSenha.Validade), token.ExpiresAt);
        Assert.Null(token.UsedAt);
    }

    [Fact]
    public void DeixaDeServirNoInstanteEmQueVence()
    {
        var token = Novo();

        Assert.True(token.EstaUtilizavel(token.ExpiresAt.AddSeconds(-1)));
        Assert.False(token.EstaUtilizavel(token.ExpiresAt));
    }

    [Fact]
    public void Consumir_MarcaOUsoENaoAceitaDeNovo()
    {
        var token = Novo();

        token.Consumir(Agora.AddMinutes(5));

        Assert.Equal(Agora.AddMinutes(5), token.UsedAt);
        Assert.False(token.EstaUtilizavel(Agora.AddMinutes(6)));
        Assert.Throws<InvalidOperationException>(() => token.Consumir(Agora.AddMinutes(7)));
    }

    // Invalidar não sobrescreve o instante do uso: o rastro de QUANDO o link foi usado é o que
    // se olha se alguém reclamar de troca de senha que não pediu.
    [Fact]
    public void Invalidar_NaoApagaOInstanteDeUsoJaRegistrado()
    {
        var token = Novo();
        token.Consumir(Agora.AddMinutes(5));

        token.Invalidar(Agora.AddMinutes(30));

        Assert.Equal(Agora.AddMinutes(5), token.UsedAt);
    }

    [Fact]
    public void Invalidar_DerrubaTokenQueNuncaFoiUsado()
    {
        var token = Novo();

        token.Invalidar(Agora.AddMinutes(2));

        Assert.False(token.EstaUtilizavel(Agora.AddMinutes(3)));
    }
}
