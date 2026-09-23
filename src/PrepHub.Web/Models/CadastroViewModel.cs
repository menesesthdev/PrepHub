using System.ComponentModel.DataAnnotations;
using PrepHub.Domain.Autenticacao;

namespace PrepHub.Web.Models;

/// <summary>
/// Formulário de criação de conta com e-mail e senha. As regras aqui são espelho da
/// <see cref="PoliticaDeSenha"/> do domínio — o número do mínimo vem dela, não de um literal
/// solto na anotação, senão os dois lados saem de sincronia.
/// </summary>
public sealed class CadastroViewModel
{
    public string? ReturnUrl { get; init; }

    [Display(Name = "Nome")]
    [Required(ErrorMessage = "Informe seu nome.")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "O nome deve ter ao menos 2 caracteres.")]
    public string? Nome { get; init; }

    [Display(Name = "E-mail")]
    [Required(ErrorMessage = "Informe seu e-mail.")]
    [EmailAddress(ErrorMessage = "E-mail inválido.")]
    [StringLength(320)]
    public string? Email { get; init; }

    [Display(Name = "Senha")]
    [Required(ErrorMessage = "Crie uma senha.")]
    [StringLength(
        PoliticaDeSenha.TamanhoMaximo,
        MinimumLength = PoliticaDeSenha.TamanhoMinimo,
        ErrorMessage = "A senha deve ter no mínimo {2} caracteres.")]
    public string? Senha { get; init; }

    [Display(Name = "Confirmar senha")]
    [Required(ErrorMessage = "Repita a senha.")]
    [Compare(nameof(Senha), ErrorMessage = "As senhas não conferem.")]
    public string? ConfirmacaoDeSenha { get; init; }
}
