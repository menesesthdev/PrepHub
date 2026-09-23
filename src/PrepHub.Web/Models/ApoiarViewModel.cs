namespace PrepHub.Web.Models;

/// <summary>Dados da página <c>/apoiar</c>. Tudo já resolvido no controller — a view não calcula nada.</summary>
public sealed class ApoiarViewModel
{
    /// <summary>Payload BR Code: é ao mesmo tempo o conteúdo do QR e o texto do copia-e-cola.</summary>
    public required string Payload { get; init; }

    /// <summary>Markup SVG do QR Code, embutido direto na página.</summary>
    public required string QrCodeSvg { get; init; }

    /// <summary>Valor selecionado, ou <c>null</c> para o código de valor aberto.</summary>
    public decimal? ValorSelecionado { get; init; }

    public required IReadOnlyList<decimal> ValoresSugeridos { get; init; }

    public string? CustoMensal { get; init; }

    public string? LinkExterno { get; init; }

    public required string LinkExternoRotulo { get; init; }
}
