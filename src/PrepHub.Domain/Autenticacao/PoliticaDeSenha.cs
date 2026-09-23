namespace PrepHub.Domain.Autenticacao;

/// <summary>
/// Regra de senha aceitável no cadastro local. Vive no domínio para que Application e Web
/// (mensagem e atributo de validação) leiam o MESMO número — política duplicada em dois
/// lugares é a que sai de sincronia e deixa passar senha que o serviço depois rejeita.
/// </summary>
/// <remarks>
/// Comprimento mínimo em vez de exigência de símbolo/maiúscula: as recomendações atuais
/// (NIST SP 800-63B) apontam que regras de composição empurram a pessoa para variações
/// previsíveis ("Senha@1") sem ganho real, enquanto o tamanho ganha entropia de verdade.
/// </remarks>
public static class PoliticaDeSenha
{
    public const int TamanhoMinimo = 8;

    /// <summary>
    /// Teto que existe só para não hashear payload arbitrário: o PBKDF2 processa a senha
    /// inteira, então um campo sem limite viraria custo de CPU controlado por quem envia.
    /// </summary>
    public const int TamanhoMaximo = 128;

    public static bool EhAceitavel(string? senha)
        => senha is not null
           && senha.Length >= TamanhoMinimo
           && senha.Length <= TamanhoMaximo;
}
