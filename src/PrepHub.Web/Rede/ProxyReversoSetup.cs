using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

// Desambigua o IPNetwork: o tipo do HttpOverrides está obsoleto no .NET 10 em favor deste.
using IPNetwork = System.Net.IPNetwork;

namespace PrepHub.Web.Rede;

/// <summary>
/// Configuração de confiança nos cabeçalhos <c>X-Forwarded-*</c> quando a aplicação roda atrás de
/// um proxy reverso. Lida da seção <c>ProxyReverso</c>.
/// </summary>
/// <remarks>
/// Fica desligada por padrão porque só é correta quando existe de fato um proxy à frente: ligá-la
/// sem proxy faria a aplicação acreditar em cabeçalhos que o próprio cliente pode escrever.
/// </remarks>
public sealed class OpcoesDeProxyReverso
{
    public const string Secao = "ProxyReverso";

    /// <summary>Liga a leitura de <c>X-Forwarded-Proto</c> e <c>X-Forwarded-For</c>.</summary>
    public bool Habilitado { get; set; }

    /// <summary>Endereços IP dos proxies em que se confia (ex.: <c>172.18.0.5</c>).</summary>
    public string[] ProxiesConhecidos { get; set; } = [];

    /// <summary>
    /// Faixas em notação CIDR de onde o proxy pode falar (ex.: <c>172.16.0.0/12</c> para a rede
    /// padrão do Docker). Mais estável que endereço fixo quando o proxy é um container, cujo IP
    /// muda a cada recriação.
    /// </summary>
    public string[] RedesConhecidas { get; set; } = [];

    /// <summary>
    /// ⚠️ Aceita <c>X-Forwarded-*</c> de <b>qualquer</b> origem. Último recurso, e inseguro.
    /// </summary>
    /// <remarks>
    /// Com isto ligado, quem alcançar a aplicação diretamente escolhe o próprio IP aparente: basta
    /// mandar <c>X-Forwarded-For</c> com um valor inventado. O limitador de tentativas por IP
    /// (<c>LimiteDeTentativasSetup</c>) usa exatamente esse endereço como chave, então a proteção
    /// contra força bruta passa a ser contornável trocando o cabeçalho a cada requisição — e os
    /// registros de acesso passam a apontar para endereços fictícios.
    ///
    /// Só é aceitável quando a aplicação é <b>inalcançável</b> fora do proxy (rede interna do
    /// Docker sem porta publicada, ou firewall fechado). Se a porta 8080 estiver exposta no host,
    /// não use.
    /// </remarks>
    public bool ConfiarEmQualquerProxy { get; set; }
}

/// <summary>
/// Faz a aplicação enxergar o esquema e o IP originais quando há um proxy reverso na frente.
/// </summary>
/// <remarks>
/// <para>
/// Resolve dois problemas que só aparecem em produção, e ambos de forma difícil de diagnosticar:
/// </para>
/// <list type="number">
/// <item>
/// <b>OAuth.</b> O TLS termina no proxy, então a aplicação recebe HTTP e monta os
/// <c>redirect_uri</c> com <c>http://</c> e o host interno. Google, GitHub e LinkedIn recusam o
/// endereço por não bater com o cadastrado, e o erro aparece no provedor — não no nosso log.
/// </item>
/// <item>
/// <b>Redirecionamento para HTTPS.</b> Sem o esquema corrigido, <c>UseHttpsRedirection</c> vê
/// HTTP e devolve um 307 para HTTPS; o proxy encaminha de novo como HTTP e o ciclo se repete,
/// produzindo laço de redirecionamento.
/// </item>
/// </list>
/// <para>
/// ⚠️ <b>A ordem no pipeline é o que faz isto funcionar.</b> Precisa ser o primeiro middleware:
/// tudo o que lê esquema, host ou IP — inclusive <c>UseHsts</c>, <c>UseHttpsRedirection</c>, a
/// autenticação e o limitador por IP — tem de rodar depois da correção, senão lê o valor do salto
/// interno em vez do valor do cliente.
/// </para>
/// <para>
/// <c>X-Forwarded-Host</c> fica deliberadamente de fora. O <c>Host</c> original já é preservado
/// pelos proxies usuais, e aceitar o cabeçalho abriria caminho para host forjado — que reaparece
/// nos links absolutos que a aplicação gera, inclusive nos e-mails de confirmação e de
/// redefinição de senha.
/// </para>
/// </remarks>
public static class ProxyReversoSetup
{
    public static WebApplication UseProxyReverso(this WebApplication app)
    {
        var opcoes = app.Configuration.GetSection(OpcoesDeProxyReverso.Secao).Get<OpcoesDeProxyReverso>()
                     ?? new OpcoesDeProxyReverso();

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(ProxyReversoSetup));

        if (!opcoes.Habilitado)
        {
            // Sem proxy declarado não há o que corrigir. Vale para desenvolvimento e para o
            // compose local, em que o navegador fala direto com a aplicação.
            return app;
        }

        var configuracao = MontarOpcoes(opcoes, logger);
        app.UseForwardedHeaders(configuracao);

        return app;
    }

    /// <summary>
    /// Traduz a configuração declarada nas opções do middleware. Pura: não toca no pipeline.
    /// </summary>
    /// <remarks>
    /// Pública para ser testável sem subir um servidor. É aqui que moram todas as decisões que
    /// importam — limpar o padrão de loopback, quem é confiável, e o modo inseguro —, e cada uma
    /// delas falha em silêncio quando errada: o efeito de um proxy não confiado é um cabeçalho
    /// descartado, que não lança, não registra nada por conta própria e só aparece como login
    /// social recusado pelo provedor.
    /// </remarks>
    public static ForwardedHeadersOptions MontarOpcoes(OpcoesDeProxyReverso opcoes, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(opcoes);

        var configuracao = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor
        };

        // O padrão do ASP.NET Core já traz o loopback nas duas listas. Ele é correto quando o
        // proxy roda na mesma máquina, e é justamente o que precisa sair quando o proxy é outro
        // container — senão os cabeçalhos vindos dele são descartados sem aviso.
        configuracao.KnownProxies.Clear();
        configuracao.KnownIPNetworks.Clear();

        foreach (var texto in opcoes.ProxiesConhecidos)
        {
            if (IPAddress.TryParse(texto, out var endereco))
            {
                configuracao.KnownProxies.Add(endereco);

                // Kestrel escuta em soquete de pilha dupla, então um cliente IPv4 pode chegar
                // representado na forma mapeada (::ffff:127.0.0.1) e não bater com o literal
                // declarado. É o MESMO endereço — registrar as duas formas não amplia confiança
                // nenhuma e evita um descarte silencioso que dependeria de detalhe de rede.
                if (endereco.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    configuracao.KnownProxies.Add(endereco.MapToIPv6());
                }
            }
            else
            {
                // Não derruba a aplicação por causa de uma entrada malformada, mas também não a
                // ignora em silêncio: entrada inválida significa proxy não confiado, e o sintoma
                // seria OAuth quebrado sem nada apontando para a configuração.
                logger?.LogError("ProxyReverso: '{Valor}' não é um endereço IP válido e foi ignorado.", texto);
            }
        }

        foreach (var texto in opcoes.RedesConhecidas)
        {
            if (IPNetwork.TryParse(texto, out var rede))
            {
                configuracao.KnownIPNetworks.Add(rede);
            }
            else
            {
                logger?.LogError("ProxyReverso: '{Valor}' não é uma faixa CIDR válida e foi ignorada.", texto);
            }
        }

        if (opcoes.ConfiarEmQualquerProxy)
        {
            // Listas vazias com ForwardLimit nulo = aceita de qualquer origem, quantos saltos vierem.
            configuracao.ForwardLimit = null;

            logger?.LogWarning(
                "ProxyReverso: confiando em X-Forwarded-* de QUALQUER origem. O IP do cliente passa a " +
                "ser escolhido por quem envia a requisição, o que torna o limite de tentativas por IP " +
                "contornável. Só é aceitável se a aplicação for inalcançável fora do proxy.");

            return configuracao;
        }

        if (configuracao.KnownProxies.Count == 0 && configuracao.KnownIPNetworks.Count == 0)
        {
            // Estado mais perigoso da configuração: parece ligado e não faz nada. Sem origem
            // confiável, o middleware descarta todo X-Forwarded-* — e o sintoma é OAuth recusado
            // pelo provedor, longe daqui.
            logger?.LogError(
                "ProxyReverso está habilitado, mas nenhum proxy ou rede foi informado: os cabeçalhos " +
                "X-Forwarded-* serão DESCARTADOS e o login social vai falhar. Preencha " +
                "ProxyReverso:RedesConhecidas (ex.: 172.16.0.0/12 para a rede do Docker).");

            return configuracao;
        }

        AvisarSobreLoopbackIncompleto(configuracao, logger);

        logger?.LogInformation(
            "ProxyReverso ativo: {Proxies} proxy(s) e {Redes} rede(s) confiáveis.",
            configuracao.KnownProxies.Count,
            configuracao.KnownIPNetworks.Count);

        return configuracao;
    }

    /// <summary>
    /// Avisa quando só uma das duas formas do loopback foi declarada.
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>127.0.0.1</c> e <c>::1</c> são endereços diferentes, e um proxy no próprio host pode
    /// conectar por qualquer um dos dois — o Nginx com <c>proxy_pass http://localhost:8080</c>
    /// costuma resolver para IPv6, enquanto <c>http://127.0.0.1:8080</c> vai por IPv4. Declarar
    /// apenas um funciona nos testes de quem escolheu aquele caminho e falha calado no outro:
    /// os cabeçalhos são descartados, o esquema continua HTTP e o login social quebra.
    ///
    /// Comprovado em teste manual — com só <c>127.0.0.1</c> declarado, a requisição vinda de
    /// <c>::1</c> foi descartada e a resposta virou redirecionamento para HTTPS.
    /// </remarks>
    private static void AvisarSobreLoopbackIncompleto(ForwardedHeadersOptions configuracao, ILogger? logger)
    {
        var temV4 = configuracao.KnownProxies.Contains(IPAddress.Loopback);
        var temV6 = configuracao.KnownProxies.Contains(IPAddress.IPv6Loopback);

        if (temV4 == temV6)
        {
            return;
        }

        logger?.LogWarning(
            "ProxyReverso: apenas {Declarado} foi declarado como proxy confiável. O loopback tem duas " +
            "formas e um proxy no mesmo host pode usar a outra ({Faltante}) — nesse caso os cabeçalhos " +
            "são descartados sem erro e o login social falha. Declare as duas se o proxy roda no host.",
            temV4 ? "127.0.0.1" : "::1",
            temV4 ? "::1" : "127.0.0.1");
    }
}
