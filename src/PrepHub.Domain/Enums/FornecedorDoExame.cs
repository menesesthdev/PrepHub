namespace PrepHub.Domain.Enums;

/// <summary>
/// Quem emite a certificação. Não é rótulo de catálogo: decide a escala da nota, as regras de
/// formato das questões e a interface inteira da prova, porque Microsoft e AWS entregam a prova
/// em telas diferentes — e fidelidade à tela real é o produto.
/// </summary>
/// <remarks>
/// Persistido como inteiro (coluna <c>Vendor</c>). Microsoft é o zero de propósito: a coluna
/// nasceu depois dos quatro exames Azure, e o valor padrão da migration os mantém como estavam.
/// </remarks>
public enum FornecedorDoExame
{
    Microsoft = 0,

    Aws = 1
}
