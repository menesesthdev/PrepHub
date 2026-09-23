namespace PrepHub.Application.Abstractions;

/// <summary>
/// Converte um texto em um QR Code vetorial. A Application não sabe qual biblioteca desenha —
/// e o SVG é escolha deliberada: vetor não borra em nenhuma densidade de tela e é lido pela
/// câmera do celular mesmo quando o visitante dá zoom na página.
/// </summary>
public interface IGeradorDeQrCode
{
    /// <summary>Devolve o markup <c>&lt;svg&gt;</c> completo, pronto para embutir na página.</summary>
    string GerarSvg(string conteudo);
}
