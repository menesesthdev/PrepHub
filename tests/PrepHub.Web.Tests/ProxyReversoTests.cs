using System.Net;
using PrepHub.Web.Rede;
using Microsoft.AspNetCore.HttpOverrides;

namespace PrepHub.Web.Tests;

/// <summary>
/// A confiança nos cabeçalhos <c>X-Forwarded-*</c> é a configuração de produção com a pior
/// relação entre impacto e visibilidade: errada para menos, o login social quebra no provedor;
/// errada para mais, o IP do cliente passa a ser escolhido por quem ataca.
/// </summary>
/// <remarks>
/// Nenhum dos dois erros lança exceção nem aparece em teste de integração comum — o middleware
/// simplesmente descarta o cabeçalho, ou acredita nele. Por isso as decisões vivem numa função
/// pura (<see cref="ProxyReversoSetup.MontarOpcoes"/>) e são afirmadas aqui uma a uma.
/// </remarks>
public class ProxyReversoTests
{
    private static OpcoesDeProxyReverso Habilitado() => new() { Habilitado = true };

    // ------------------------------------------------------------------ o que é lido do cliente

    /// <summary>
    /// Só esquema e IP de origem. <c>X-Forwarded-Host</c> fica de fora de propósito.
    /// </summary>
    /// <remarks>
    /// Aceitar host forjado contaminaria todo link absoluto que a aplicação gera — inclusive os
    /// dos e-mails de confirmação de cadastro e de redefinição de senha, que apontariam para o
    /// domínio de quem enviou o cabeçalho.
    /// </remarks>
    [Fact]
    public void MontarOpcoes_LeApenasProtoEFor()
    {
        var opcoes = ProxyReversoSetup.MontarOpcoes(Habilitado());

        Assert.True(opcoes.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedProto));
        Assert.True(opcoes.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedFor));
        Assert.False(opcoes.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedHost));
    }

    // ------------------------------------------------------------------- origens confiáveis

    /// <summary>
    /// O padrão de loopback do ASP.NET Core é removido, e a remoção é o ponto.
    /// </summary>
    /// <remarks>
    /// Mantido, ele confiaria em 127.0.0.1 além do que for declarado. Isso é inofensivo quando o
    /// proxy é local e enganoso quando não é: a configuração parece ter efeito, o proxy real
    /// continua não confiado, e os cabeçalhos são descartados sem nada indicar o motivo.
    /// </remarks>
    [Fact]
    public void MontarOpcoes_NaoHerdaOLoopbackPadrao()
    {
        var opcoes = ProxyReversoSetup.MontarOpcoes(new OpcoesDeProxyReverso
        {
            Habilitado = true,
            RedesConhecidas = ["172.16.0.0/12"]
        });

        Assert.Single(opcoes.KnownIPNetworks);
        Assert.Empty(opcoes.KnownProxies);
        Assert.DoesNotContain(opcoes.KnownProxies, ip => IPAddress.IsLoopback(ip));
    }

    [Fact]
    public void MontarOpcoes_RegistraProxiesERedesDeclarados()
    {
        var opcoes = ProxyReversoSetup.MontarOpcoes(new OpcoesDeProxyReverso
        {
            Habilitado = true,
            ProxiesConhecidos = ["10.1.2.3"],
            RedesConhecidas = ["172.16.0.0/12", "10.0.0.0/8"]
        });

        Assert.Contains(opcoes.KnownProxies, ip => ip.Equals(IPAddress.Parse("10.1.2.3")));
        Assert.Equal(2, opcoes.KnownIPNetworks.Count);
    }

    /// <summary>
    /// Entrada malformada não derruba a aplicação e não vira confiança.
    /// </summary>
    /// <remarks>
    /// O lado seguro do erro é descartar: um CIDR digitado errado no <c>.env</c> não pode virar
    /// uma faixa mais ampla do que a pretendida.
    /// </remarks>
    [Theory]
    [InlineData("nao-e-ip")]
    [InlineData("999.999.999.999")]
    [InlineData("")]
    public void MontarOpcoes_ValorInvalido_EDescartado(string valor)
    {
        var opcoes = ProxyReversoSetup.MontarOpcoes(new OpcoesDeProxyReverso
        {
            Habilitado = true,
            ProxiesConhecidos = [valor],
            RedesConhecidas = [valor]
        });

        Assert.Empty(opcoes.KnownProxies);
        Assert.Empty(opcoes.KnownIPNetworks);
    }

    // ------------------------------------------------------------------------ o modo inseguro

    /// <summary>
    /// <c>ConfiarEmQualquerProxy</c> precisa de fato aceitar qualquer origem — sem listas e sem
    /// limite de saltos.
    /// </summary>
    /// <remarks>
    /// Testado porque a opção é a válvula de escape para quem não consegue descobrir o IP do
    /// proxy, e uma implementação que a ignorasse deixaria a pessoa com login quebrado achando
    /// que já tentou tudo. O preço dela está documentado em <see cref="OpcoesDeProxyReverso"/>.
    /// </remarks>
    [Fact]
    public void MontarOpcoes_ConfiarEmQualquerProxy_NaoRestringeOrigem()
    {
        var opcoes = ProxyReversoSetup.MontarOpcoes(new OpcoesDeProxyReverso
        {
            Habilitado = true,
            ConfiarEmQualquerProxy = true
        });

        Assert.Empty(opcoes.KnownProxies);
        Assert.Empty(opcoes.KnownIPNetworks);
        Assert.Null(opcoes.ForwardLimit);
    }

    /// <summary>
    /// O modo inseguro é sempre explícito: nunca é alcançado por omissão.
    /// </summary>
    /// <remarks>
    /// Habilitado sem nenhuma origem declarada, o resultado é o oposto — nada é confiado, e o
    /// <c>ForwardLimit</c> segue no padrão de um salto. É o estado que o log denuncia como erro,
    /// porque parece configurado e descarta tudo.
    /// </remarks>
    [Fact]
    public void MontarOpcoes_SemOrigemDeclarada_NaoConfiaEmNinguem()
    {
        var opcoes = ProxyReversoSetup.MontarOpcoes(Habilitado());

        Assert.Empty(opcoes.KnownProxies);
        Assert.Empty(opcoes.KnownIPNetworks);
        Assert.NotNull(opcoes.ForwardLimit);
    }
}

/// <summary>
/// Pilha dupla: o loopback tem duas formas, e declarar uma não cobre a outra.
/// </summary>
/// <remarks>
/// Nasceu de um teste manual que falhou: com apenas <c>127.0.0.1</c> declarado, a requisição
/// vinda de <c>::1</c> teve os cabeçalhos descartados e a resposta virou redirecionamento para
/// HTTPS — exatamente o laço de redirecionamento que o middleware existe para evitar. É o tipo de
/// diferença que passa despercebida porque depende de como o proxy resolve o nome do host.
/// </remarks>
public class ProxyReversoPilhaDuplaTests
{
    /// <summary>
    /// Endereço IPv4 declarado passa a valer também na forma mapeada para IPv6.
    /// </summary>
    /// <remarks>
    /// <c>::ffff:127.0.0.1</c> é o MESMO endereço visto por um soquete de pilha dupla, então
    /// registrá-lo não amplia confiança — apenas impede que a correspondência dependa de detalhe
    /// de rede.
    /// </remarks>
    [Fact]
    public void MontarOpcoes_EnderecoIPv4_TambemValeNaFormaMapeada()
    {
        var opcoes = ProxyReversoSetup.MontarOpcoes(new OpcoesDeProxyReverso
        {
            Habilitado = true,
            ProxiesConhecidos = ["127.0.0.1"]
        });

        Assert.Contains(opcoes.KnownProxies, ip => ip.Equals(IPAddress.Loopback));
        Assert.Contains(opcoes.KnownProxies, ip => ip.Equals(IPAddress.Loopback.MapToIPv6()));
    }

    /// <summary>
    /// Declarar as duas formas do loopback é aceito e não gera duplicata inútil.
    /// </summary>
    [Fact]
    public void MontarOpcoes_AmbosOsLoopbacks_SaoRegistrados()
    {
        var opcoes = ProxyReversoSetup.MontarOpcoes(new OpcoesDeProxyReverso
        {
            Habilitado = true,
            ProxiesConhecidos = ["127.0.0.1", "::1"]
        });

        Assert.Contains(opcoes.KnownProxies, ip => ip.Equals(IPAddress.Loopback));
        Assert.Contains(opcoes.KnownProxies, ip => ip.Equals(IPAddress.IPv6Loopback));
    }

    /// <summary>
    /// ⚠️ <c>::1</c> NÃO é coberto por declarar <c>127.0.0.1</c> — são endereços distintos.
    /// </summary>
    /// <remarks>
    /// Este teste afirma uma limitação, não uma capacidade, e é o que mantém honesto o aviso
    /// emitido no boot: se um dia alguém "resolver" o problema adicionando o outro loopback
    /// automaticamente, este teste falha e obriga a decisão a ser consciente — ampliar confiança
    /// para um endereço que o operador não declarou é decisão de segurança, não detalhe.
    /// </remarks>
    [Fact]
    public void MontarOpcoes_LoopbackIPv4_NaoCobreOLoopbackIPv6()
    {
        var opcoes = ProxyReversoSetup.MontarOpcoes(new OpcoesDeProxyReverso
        {
            Habilitado = true,
            ProxiesConhecidos = ["127.0.0.1"]
        });

        Assert.DoesNotContain(opcoes.KnownProxies, ip => ip.Equals(IPAddress.IPv6Loopback));
    }
}
