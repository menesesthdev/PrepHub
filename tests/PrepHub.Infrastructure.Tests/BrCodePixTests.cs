using PrepHub.Infrastructure.Doacao;

namespace PrepHub.Infrastructure.Tests;

/// <summary>
/// O payload do Pix é um formato fechado por checksum: um caractere fora do lugar e o aplicativo
/// do banco recusa o código inteiro, sem dizer onde está o erro. Como não há como testar isso em
/// produção sem transferir dinheiro de verdade, os testes comparam contra valores de referência.
/// </summary>
public class BrCodePixTests
{
    private const string ChaveDoExemplo = "123e4567-e12b-12d1-a456-426655440000";

    /// <summary>
    /// Exemplo do manual do BR Code do Banco Central, reproduzido campo a campo. É o único teste
    /// que prova que a montagem inteira está certa — ordem dos campos, tamanhos e CRC final.
    /// </summary>
    [Fact]
    public void Gerar_ReproduzOExemploDoBancoCentral()
    {
        var payload = BrCodePix.Gerar(ChaveDoExemplo, "Fulano de Tal", "BRASILIA", 15.00m);

        Assert.Equal(
            "00020126580014BR.GOV.BCB.PIX0136123e4567-e12b-12d1-a456-426655440000" +
            "520400005303986540515.005802BR5913Fulano de Tal6008BRASILIA62070503***63040731",
            payload);
    }

    /// <summary>Sem valor, o campo 54 não existe — é o que torna o código de valor livre.</summary>
    [Fact]
    public void Gerar_SemValor_OmiteOCampoDeValor()
    {
        var payload = BrCodePix.Gerar(ChaveDoExemplo, "PrepHub", "SAO PAULO");

        Assert.DoesNotContain("5405", payload);
        Assert.Contains("53039865802BR", payload);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Gerar_ValorNaoPositivo_TratadoComoValorLivre(int valor)
    {
        var comValorInvalido = BrCodePix.Gerar(ChaveDoExemplo, "PrepHub", "SAO PAULO", valor);
        var semValor = BrCodePix.Gerar(ChaveDoExemplo, "PrepHub", "SAO PAULO");

        Assert.Equal(semValor, comValorInvalido);
    }

    /// <summary>
    /// O valor sempre usa ponto decimal e duas casas. Se a formatação seguisse a cultura do
    /// servidor, uma máquina em pt-BR escreveria "15,00" e o código quebraria em produção sem
    /// nunca ter falhado na máquina de quem desenvolveu.
    /// </summary>
    [Fact]
    public void Gerar_FormataValorComPontoIndependenteDaCulturaDoServidor()
    {
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");
            var payload = BrCodePix.Gerar(ChaveDoExemplo, "PrepHub", "SAO PAULO", 15.5m);

            Assert.Contains("540515.50", payload);
            Assert.DoesNotContain("15,50", payload);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    /// <summary>Acento vira a letra base: alguns aplicativos recusam byte não-ASCII nesses campos.</summary>
    [Fact]
    public void Gerar_RemoveAcentoDoNomeEDaCidade()
    {
        var payload = BrCodePix.Gerar(ChaveDoExemplo, "João Ação", "BRASÍLIA");

        Assert.Contains("5909Joao Acao", payload);
        Assert.Contains("6008BRASILIA", payload);
    }

    [Fact]
    public void Gerar_TruncaNomeECidadeNosLimitesDoPadrao()
    {
        var payload = BrCodePix.Gerar(
            ChaveDoExemplo,
            new string('N', 40),
            new string('C', 40));

        // 25 para o nome (campo 59) e 15 para a cidade (campo 60).
        Assert.Contains("59" + "25" + new string('N', 25), payload);
        Assert.Contains("60" + "15" + new string('C', 15), payload);
    }

    /// <summary>
    /// O CRC cobre o próprio marcador "6304". Trocar um caractere qualquer do meio tem de mudar
    /// os quatro dígitos finais — é isso que faz o aplicativo do banco detectar código adulterado.
    /// </summary>
    [Fact]
    public void Gerar_CrcMudaQuandoQualquerCampoMuda()
    {
        var comUmValor = BrCodePix.Gerar(ChaveDoExemplo, "PrepHub", "SAO PAULO", 5m);
        var comOutro = BrCodePix.Gerar(ChaveDoExemplo, "PrepHub", "SAO PAULO", 15m);

        Assert.NotEqual(comUmValor[^4..], comOutro[^4..]);
    }

    [Fact]
    public void Gerar_TerminaSempreComQuatroDigitosHexadecimaisMaiusculos()
    {
        var payload = BrCodePix.Gerar(ChaveDoExemplo, "PrepHub", "SAO PAULO", 30m);

        Assert.Matches("6304[0-9A-F]{4}$", payload);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Gerar_SemChave_Falha(string? chave)
    {
        // ThrowsAny e não Throws: null vem como ArgumentNullException, que deriva de
        // ArgumentException — o que importa é falhar alto, não a subclasse exata.
        Assert.ThrowsAny<ArgumentException>(() => BrCodePix.Gerar(chave!, "PrepHub", "SAO PAULO"));
    }

    /// <summary>Nome vazio não pode gerar campo de tamanho zero, que invalidaria o payload.</summary>
    [Fact]
    public void Gerar_NomeVazio_UsaPlaceholderEmVezDeCampoVazio()
    {
        var payload = BrCodePix.Gerar(ChaveDoExemplo, "   ", "   ");

        Assert.Contains("5903N/A", payload);
        Assert.Contains("6003N/A", payload);
    }
}
