namespace PrepHub.Infrastructure.Doacao;

/// <summary>
/// Configuração da página de apoio, lida da seção <c>Doacao</c>. Segue a mesma disciplina do
/// e-mail e dos provedores OAuth: sem <see cref="ChavePix"/> configurada nada aparece — nem o
/// link no rodapé, nem a linha no score report, e a própria rota responde 404.
/// </summary>
/// <remarks>
/// Ao contrário das credenciais de OAuth e SMTP, a chave Pix <b>não é segredo</b>: ela existe
/// para ser exibida publicamente na página de doação. Por isso mora no <c>appsettings.json</c>
/// versionado, e não em <c>user-secrets</c> — guardá-la como segredo daria uma falsa sensação
/// de proteção a um dado que é publicado de propósito.
/// </remarks>
public sealed class OpcoesDeDoacao
{
    public const string Secao = "Doacao";

    /// <summary>
    /// Chave Pix que recebe as doações. Prefira uma <b>chave aleatória</b>: CPF, telefone e
    /// e-mail ficariam expostos numa página pública, e chave aleatória pode ser trocada a
    /// qualquer momento sem mexer em mais nada.
    /// </summary>
    public string? ChavePix { get; set; }

    /// <summary>
    /// Nome que viaja no payload (campo 59). Não precisa ser o nome civil do titular: quem
    /// resolve a titularidade é o banco, a partir da chave.
    /// </summary>
    public string NomeDoRecebedor { get; set; } = "PrepHub";

    /// <summary>Cidade do recebedor (campo 60). Sem acento e com no máximo 15 caracteres.</summary>
    public string Cidade { get; set; } = "SAO PAULO";

    /// <summary>
    /// Valores oferecidos como atalho, em reais. Vazio deixa só o código de valor aberto.
    /// </summary>
    public IReadOnlyList<decimal> ValoresSugeridos { get; set; } = new decimal[] { 5m, 15m, 30m };

    /// <summary>
    /// Custo mensal declarado na página, em texto livre (ex.: "cerca de R$ 60 por mês").
    /// É o argumento que mais pesa numa doação — dizer para onde o dinheiro vai. Nulo omite
    /// a frase inteira em vez de exibir um número inventado.
    /// </summary>
    public string? CustoMensal { get; set; }

    /// <summary>
    /// Alternativa para quem está fora do Brasil e não tem Pix (Ko-fi, PayPal). Opcional.
    /// </summary>
    public string? LinkExterno { get; set; }

    /// <summary>Rótulo do <see cref="LinkExterno"/>.</summary>
    public string LinkExternoRotulo { get; set; } = "Doar de fora do Brasil";

    public bool EstaConfigurado => !string.IsNullOrWhiteSpace(ChavePix);
}
