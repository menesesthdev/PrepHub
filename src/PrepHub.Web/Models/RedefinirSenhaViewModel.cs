using System.ComponentModel.DataAnnotations;
using PrepHub.Domain.Autenticacao;

namespace PrepHub.Web.Models;

/// <summary>
/// Nova senha, com o token do link viajando em campo oculto. As regras espelham a
/// <see cref="PoliticaDeSenha"/> do domínio, igual ao cadastro.
/// </summary>
public sealed class RedefinirSenhaViewModel
{
    /// <summary>Token recebido por e-mail. Some da tela, mas acompanha o POST.</summary>
    public string? Token { get; init; }

    [Display(Name = "Nova senha")]
    [Required(ErrorMessage = "Crie uma senha.")]
    [StringLength(
        PoliticaDeSenha.TamanhoMaximo,
        MinimumLength = PoliticaDeSenha.TamanhoMinimo,
        ErrorMessage = "A senha deve ter no mínimo {2} caracteres.")]
    public string? Senha { get; init; }

    [Display(Name = "Confirmar nova senha")]
    [Required(ErrorMessage = "Repita a senha.")]
    [Compare(nameof(Senha), ErrorMessage = "As senhas não conferem.")]
    public string? ConfirmacaoDeSenha { get; init; }

    /// <summary>
    /// Link inválido, vencido ou já usado. Quando verdadeiro a tela nem mostra o formulário —
    /// preencher senha para descobrir depois que o link morreu é trabalho jogado fora.
    /// </summary>
    public bool TokenInvalido { get; init; }
}
