using PrepHub.Domain.Enums;

namespace PrepHub.Domain.Correcao;

/// <summary>
/// Converte o percentual de acertos na nota escalada exibida no score report, onde a aprovação é
/// sempre 700 — independentemente de quantos acertos isso representa.
/// </summary>
/// <remarks>
/// A escala real (Microsoft e AWS) é derivada de modelos psicométricos: cada questão tem peso
/// próprio, calibrado estatisticamente, e o mapeamento nunca é divulgado. Aqui usamos uma
/// aproximação linear por partes, ancorada no ponto que importa: o percentual de corte do exame
/// vira exatamente 700. Assim a nota exibida e o veredito aprovado/reprovado nunca se contradizem.
///
/// Os dois fornecedores cortam em 700 e terminam em 1000; o que muda é o piso — a Microsoft
/// publica a escala como 1–1000, a AWS como 100–1000. Ver <see cref="NotaMinimaPara"/>.
/// </remarks>
public static class EscalaDeNota
{
    /// <summary>Nota mínima de aprovação (igual nos dois fornecedores).</summary>
    public const int NotaDeCorte = 700;

    /// <summary>Menor nota possível na escala Microsoft (a escala não começa em zero).</summary>
    public const int NotaMinima = 1;

    /// <summary>Menor nota possível na escala AWS ("scaled score of 100–1,000").</summary>
    public const int NotaMinimaAws = 100;

    /// <summary>Maior nota possível na escala.</summary>
    public const int NotaMaxima = 1000;

    /// <summary>O piso da escala publicada pelo fornecedor do exame.</summary>
    public static int NotaMinimaPara(FornecedorDoExame fornecedor)
        => fornecedor == FornecedorDoExame.Aws ? NotaMinimaAws : NotaMinima;

    /// <summary>
    /// Converte <paramref name="scorePercent"/> (0–100) para a escala
    /// <paramref name="notaMinima"/>–1000, ancorando <paramref name="passingScorePercent"/> em
    /// <see cref="NotaDeCorte"/>.
    /// </summary>
    public static int Converter(decimal scorePercent, int passingScorePercent, int notaMinima = NotaMinima)
    {
        var percent = Math.Clamp(scorePercent, 0m, 100m);
        var passing = Math.Clamp(passingScorePercent, 0, 100);
        var piso = Math.Clamp(notaMinima, 0, NotaDeCorte - 1);

        decimal scaled;

        if (passing <= 0)
        {
            // Sem corte: qualquer acerto já aprova, então toda a faixa vive acima de 700.
            scaled = NotaDeCorte + (percent / 100m * (NotaMaxima - NotaDeCorte));
        }
        else if (passing >= 100)
        {
            // Corte em 100%: só a prova perfeita atinge 700; o resto é reprovado.
            scaled = percent >= 100m
                ? NotaMaxima
                : piso + (percent / 100m * (NotaDeCorte - 1 - piso));
        }
        else if (percent >= passing)
        {
            // Faixa aprovada: 700 no corte, 1000 na prova perfeita.
            var above = (percent - passing) / (100m - passing);
            scaled = NotaDeCorte + (above * (NotaMaxima - NotaDeCorte));
        }
        else
        {
            // Faixa reprovada: o piso com zero acertos, 699 imediatamente abaixo do corte.
            var below = percent / passing;
            scaled = piso + (below * (NotaDeCorte - 1 - piso));
        }

        return Math.Clamp((int)Math.Round(scaled, MidpointRounding.AwayFromZero), piso, NotaMaxima);
    }
}
