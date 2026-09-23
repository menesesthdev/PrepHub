using PrepHub.Domain.Correcao;
using PrepHub.Domain.Enums;

namespace PrepHub.Domain.Tests;

public class EscalaDeNotaTests
{
    // A âncora que dá sentido à escala: o percentual de corte do exame vale exatamente 700,
    // qualquer que seja esse percentual. É o que impede a nota de contradizer o veredito.
    [Theory]
    [InlineData(70)]
    [InlineData(75)]
    [InlineData(50)]
    public void Converter_NoPercentualDeCorte_Retorna700(int passing)
    {
        Assert.Equal(EscalaDeNota.NotaDeCorte, EscalaDeNota.Converter(passing, passing));
    }

    [Fact]
    public void Converter_ProvaPerfeita_Retorna1000()
    {
        Assert.Equal(1000, EscalaDeNota.Converter(100m, 70));
    }

    [Fact]
    public void Converter_ZeroAcertos_RetornaNotaMinima()
    {
        Assert.Equal(EscalaDeNota.NotaMinima, EscalaDeNota.Converter(0m, 70));
    }

    [Fact]
    public void Converter_AbaixoDoCorte_FicaAbaixoDe700()
    {
        var nota = EscalaDeNota.Converter(69.9m, 70);

        Assert.InRange(nota, EscalaDeNota.NotaMinima, EscalaDeNota.NotaDeCorte - 1);
    }

    [Fact]
    public void Converter_AcimaDoCorte_FicaEntre700E1000()
    {
        var nota = EscalaDeNota.Converter(85m, 70);

        Assert.InRange(nota, EscalaDeNota.NotaDeCorte + 1, EscalaDeNota.NotaMaxima);
    }

    [Fact]
    public void Converter_EhMonotonica()
    {
        var anterior = 0;

        for (var percent = 0m; percent <= 100m; percent += 2.5m)
        {
            var nota = EscalaDeNota.Converter(percent, 70);
            Assert.True(nota >= anterior, $"Nota caiu em {percent}%: {nota} < {anterior}");
            anterior = nota;
        }
    }

    [Fact]
    public void Converter_ForaDaFaixa_EhClampeada()
    {
        Assert.Equal(EscalaDeNota.NotaMinima, EscalaDeNota.Converter(-10m, 70));
        Assert.Equal(EscalaDeNota.NotaMaxima, EscalaDeNota.Converter(150m, 70));
    }

    // Bordas degeneradas: sem corte tudo aprova; corte em 100% só a prova perfeita aprova.
    [Fact]
    public void Converter_SemCorte_SempreAtingeANotaDeCorte()
    {
        Assert.True(EscalaDeNota.Converter(0m, 0) >= EscalaDeNota.NotaDeCorte);
    }

    [Fact]
    public void Converter_CorteEm100_SoAprovaProvaPerfeita()
    {
        Assert.Equal(EscalaDeNota.NotaMaxima, EscalaDeNota.Converter(100m, 100));
        Assert.True(EscalaDeNota.Converter(99m, 100) < EscalaDeNota.NotaDeCorte);
    }

    // AWS publica a escala como 100–1000: o piso muda, o corte e o teto não. Uma prova zerada que
    // mostrasse 1 contradiria o relatório real, e a régua do score report ficaria desalinhada.
    [Fact]
    public void Converter_EscalaAws_ZeroAcertosRetorna100()
    {
        Assert.Equal(100, EscalaDeNota.Converter(0m, 70, EscalaDeNota.NotaMinimaPara(FornecedorDoExame.Aws)));
    }

    [Theory]
    [InlineData(70, 700)]
    [InlineData(100, 1000)]
    public void Converter_EscalaAws_MantemCorteETeto(int percent, int esperado)
    {
        Assert.Equal(esperado, EscalaDeNota.Converter(percent, 70, EscalaDeNota.NotaMinimaAws));
    }

    [Fact]
    public void Converter_EscalaAws_AbaixoDoCorteFicaEntre100E699()
    {
        var nota = EscalaDeNota.Converter(35m, 70, EscalaDeNota.NotaMinimaAws);

        Assert.InRange(nota, EscalaDeNota.NotaMinimaAws + 1, EscalaDeNota.NotaDeCorte - 1);
    }

    [Fact]
    public void NotaMinimaPara_MicrosoftContinuaEm1()
    {
        Assert.Equal(1, EscalaDeNota.NotaMinimaPara(FornecedorDoExame.Microsoft));
    }
}
