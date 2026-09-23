using System.Globalization;
using System.Text;

namespace PrepHub.Infrastructure.Doacao;

/// <summary>
/// Monta o payload "BR Code" do Pix — o texto que vira QR Code e que também funciona como
/// Pix copia-e-cola.
/// </summary>
/// <remarks>
/// O formato é o EMV MPM (Merchant Presented Mode) adotado pelo Banco Central: uma sequência de
/// campos <c>ID (2 dígitos) + tamanho (2 dígitos) + valor</c>, em ordem crescente de ID, fechada
/// por um CRC16 sobre tudo que veio antes. É determinístico e não faz I/O nenhum: nada aqui
/// depende de banco, rede ou conta — por isso é testável por igualdade de string.
///
/// Este é um QR **estático e reutilizável**: sem valor obrigatório e sem identificador de
/// transação (<c>txid</c> = <c>***</c>), então a mesma imagem serve para qualquer pessoa,
/// quantas vezes quiser. É o que se quer numa página de doação — e é também por isso que o
/// recebimento não é identificado: quem doa não fica registrado em lugar nenhum.
/// </remarks>
public static class BrCodePix
{
    // Identificador do arranjo Pix dentro do campo 26, definido pelo Banco Central.
    private const string GuiPix = "BR.GOV.BCB.PIX";

    /// <summary>Sem txid, o padrão manda preencher com três asteriscos.</summary>
    private const string SemIdentificador = "***";

    /// <summary>Limites do padrão. Nome e cidade acima disso são truncados, não rejeitados.</summary>
    private const int TamanhoMaximoDoNome = 25;
    private const int TamanhoMaximoDaCidade = 15;

    /// <summary>
    /// Gera o payload para a <paramref name="chavePix"/> informada. <paramref name="valor"/> nulo
    /// (ou não positivo) produz um código de valor aberto — quem doa digita quanto quiser no app
    /// do banco, que é o comportamento desejado numa doação.
    /// </summary>
    public static string Gerar(string chavePix, string nomeDoRecebedor, string cidade, decimal? valor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chavePix);

        var conta = Campo("00", GuiPix) + Campo("01", chavePix.Trim());

        var payload = new StringBuilder();
        payload.Append(Campo("00", "01"));                    // versão do payload
        payload.Append(Campo("26", conta));                   // dados da conta Pix
        payload.Append(Campo("52", "0000"));                  // categoria do recebedor: não informada
        payload.Append(Campo("53", "986"));                   // moeda: BRL (ISO 4217)

        if (valor is > 0m)
        {
            // Sempre com ponto decimal e duas casas, independentemente da cultura do servidor.
            payload.Append(Campo("54", valor.Value.ToString("0.00", CultureInfo.InvariantCulture)));
        }

        payload.Append(Campo("58", "BR"));                    // país
        payload.Append(Campo("59", Normalizar(nomeDoRecebedor, TamanhoMaximoDoNome)));
        payload.Append(Campo("60", Normalizar(cidade, TamanhoMaximoDaCidade)));
        payload.Append(Campo("62", Campo("05", SemIdentificador)));

        // O CRC cobre o próprio cabeçalho "6304", então ele entra antes de calcular.
        payload.Append("6304");
        payload.Append(Crc16(payload.ToString()));

        return payload.ToString();
    }

    private static string Campo(string id, string valor) => $"{id}{valor.Length:D2}{valor}";

    /// <summary>
    /// Reduz o texto ao ASCII imprimível e ao tamanho máximo do campo. Acento vira a letra sem
    /// acento (não é removido) e o que sobra de fora da tabela some — alguns aplicativos de banco
    /// recusam o código inteiro ao encontrar byte não-ASCII nesses dois campos.
    /// </summary>
    private static string Normalizar(string texto, int tamanhoMaximo)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return "N/A";
        }

        var decomposto = texto.Trim().Normalize(NormalizationForm.FormD);
        var limpo = new StringBuilder(decomposto.Length);

        foreach (var caractere in decomposto)
        {
            // Marca de acento: descartada, sobrando a letra base que a antecede.
            if (CharUnicodeInfo.GetUnicodeCategory(caractere) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (caractere is >= ' ' and <= '~')
            {
                limpo.Append(caractere);
            }
        }

        var resultado = limpo.ToString().Normalize(NormalizationForm.FormC).Trim();

        if (resultado.Length == 0)
        {
            return "N/A";
        }

        return resultado.Length <= tamanhoMaximo
            ? resultado
            : resultado[..tamanhoMaximo].Trim();
    }

    /// <summary>
    /// CRC-16/CCITT-FALSE (polinômio 0x1021, valor inicial 0xFFFF, sem reflexão e sem XOR final),
    /// em quatro dígitos hexadecimais maiúsculos — exatamente a variante exigida pelo BR Code.
    /// </summary>
    private static string Crc16(string texto)
    {
        ushort crc = 0xFFFF;

        foreach (var octeto in Encoding.ASCII.GetBytes(texto))
        {
            crc ^= (ushort)(octeto << 8);

            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x8000) != 0
                    ? (ushort)((crc << 1) ^ 0x1021)
                    : (ushort)(crc << 1);
            }
        }

        return crc.ToString("X4", CultureInfo.InvariantCulture);
    }
}
