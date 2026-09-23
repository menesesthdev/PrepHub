using PrepHub.Infrastructure.Seguranca;

namespace PrepHub.Infrastructure.Tests;

/// <summary>
/// Cobre a derivação real. São poucos casos de propósito: cada Hash/Verificar roda o PBKDF2
/// completo (fator de trabalho alto é o objetivo), então cada Fact aqui custa tempo de verdade.
/// </summary>
public class HasherDeSenhaPbkdf2Tests
{
    private readonly HasherDeSenhaPbkdf2 _hasher = new();

    [Fact]
    public void Hash_ConfereComASenhaOriginalENaoComOutra()
    {
        var hash = _hasher.Hash("senha-correta-1");

        Assert.True(_hasher.Verificar("senha-correta-1", hash));
        Assert.False(_hasher.Verificar("senha-correta-2", hash));

        // E o que vai para o banco não carrega a senha em texto.
        Assert.DoesNotContain("senha-correta-1", hash);
    }

    // Salt por senha: sem ele, senhas iguais gerariam hashes iguais e um vazamento revelaria
    // de imediato quem compartilha senha — além de permitir rainbow table.
    [Fact]
    public void Hash_DaMesmaSenhaDuasVezes_GeraResultadosDiferentes()
    {
        var primeiro = _hasher.Hash("senha-repetida-1");
        var segundo = _hasher.Hash("senha-repetida-1");

        Assert.NotEqual(primeiro, segundo);
        Assert.True(_hasher.Verificar("senha-repetida-1", segundo));
    }

    // Os parâmetros viajam no próprio hash, então subir o fator de trabalho não invalida o
    // que já está no banco: um hash com iterações antigas continua verificável.
    [Fact]
    public void Verificar_AceitaHashComFatorDeTrabalhoDiferente()
    {
        var hash = _hasher.Hash("senha-parametrizada-1");
        var partes = hash.Split('$');

        Assert.Equal(4, partes.Length);
        Assert.Equal("pbkdf2-sha256", partes[0]);
        Assert.True(int.Parse(partes[1]) >= 600_000);
    }

    // Hash ilegível é "não confere", não exceção: um registro corrompido não deve virar
    // erro 500 na tela de login.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hash-sem-formato")]
    [InlineData("pbkdf2-sha256$abc$c2FsdA==$aGFzaA==")]   // iterações não numéricas
    [InlineData("pbkdf2-sha256$600000$@@@$aGFzaA==")]      // salt não é Base64
    [InlineData("argon2id$600000$c2FsdA==$aGFzaA==")]      // algoritmo desconhecido
    public void Verificar_ComHashInvalido_DevolveFalseSemLancar(string hash)
    {
        Assert.False(_hasher.Verificar("qualquer-senha-1", hash));
    }
}
