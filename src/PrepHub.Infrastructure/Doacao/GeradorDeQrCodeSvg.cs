using System.Collections.Concurrent;
using PrepHub.Application.Abstractions;
using QRCoder;

namespace PrepHub.Infrastructure.Doacao;

/// <summary>
/// Gera o QR Code como SVG, sem dependência de rede nem de <c>System.Drawing</c>.
/// </summary>
/// <remarks>
/// Desenhar aqui dentro é uma decisão de privacidade, não só de arquitetura: as alternativas
/// "fáceis" (APIs públicas de QR) exigiriam mandar a chave Pix para um terceiro a cada visita e
/// colocariam uma página nossa refém da disponibilidade dele.
///
/// O resultado é memoizado porque o conjunto de payloads é minúsculo e fixo (um por valor
/// sugerido, mais o de valor aberto) e nunca muda enquanto o processo vive.
/// </remarks>
public sealed class GeradorDeQrCodeSvg : IGeradorDeQrCode
{
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.Ordinal);

    public string GerarSvg(string conteudo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conteudo);

        return _cache.GetOrAdd(conteudo, static texto =>
        {
            using var gerador = new QRCodeGenerator();

            // Correção de erro M (~15%): o suficiente para tolerar um brilho ou um dedo na tela
            // sem inflar o número de módulos, o que deixaria o desenho denso demais para a
            // câmera em telas pequenas.
            using var dados = gerador.CreateQrCode(texto, QRCodeGenerator.ECCLevel.M);

            var svg = new SvgQRCode(dados);

            // A zona de silêncio (margem branca) faz parte da especificação — sem ela, a leitura
            // falha quando o QR encosta em outro elemento da página.
            return svg.GetGraphic(
                pixelsPerModule: 8,
                darkColorHex: "#1f2933",
                lightColorHex: "#ffffff",
                drawQuietZones: true);
        });
    }
}
