using System.ComponentModel.DataAnnotations;
using PrepHub.Domain.Autenticacao;

namespace PrepHub.Web.Models;

/// <summary>
/// Dados da tela de login. <see cref="Provedores"/> traz só os esquemas efetivamente
/// registrados, para não oferecer um botão que quebraria por falta de credencial.
/// </summary>
public sealed class LoginViewModel
{
    public IReadOnlyList<string> Provedores { get; init; } = [];

    public string? ReturnUrl { get; init; }

    [Display(Name = "E-mail")]
    [Required(ErrorMessage = "Informe seu e-mail.")]
    [EmailAddress(ErrorMessage = "E-mail inválido.")]
    public string? Email { get; init; }

    /// <summary>
    /// Sem <c>[MinLength]</c> de propósito: no login o mínimo da política não se aplica (quem
    /// cadastrou antes de a regra endurecer continuaria entrando), e dizer "senha curta" antes
    /// de conferir já revelaria algo sobre a senha certa.
    /// </summary>
    [Display(Name = "Senha")]
    [Required(ErrorMessage = "Informe sua senha.")]
    [StringLength(PoliticaDeSenha.TamanhoMaximo)]
    public string? Senha { get; init; }

    /// <summary>
    /// Credenciais certas, mas o e-mail nunca foi confirmado. Fica fora do ModelState (ao
    /// contrário de "e-mail ou senha incorretos") porque a mensagem precisa carregar um LINK de
    /// reenvio, e mensagem de validação é texto puro.
    /// </summary>
    /// <remarks>
    /// Só chega aqui quem acertou a senha — ver <c>FalhaDeAutenticacao.EmailNaoConfirmado</c>.
    /// Por isso esta tela pode ser específica sem virar consulta de quem tem cadastro.
    /// </remarks>
    public bool EmailNaoConfirmado { get; init; }
}
