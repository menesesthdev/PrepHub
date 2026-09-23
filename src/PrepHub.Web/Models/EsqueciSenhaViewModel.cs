using System.ComponentModel.DataAnnotations;

namespace PrepHub.Web.Models;

/// <summary>
/// Pedido do link de redefinição. <see cref="Enviado"/> troca o conteúdo da tela pela
/// confirmação — que é sempre a mesma, exista ou não conta com o e-mail informado.
/// </summary>
public sealed class EsqueciSenhaViewModel
{
    [Display(Name = "E-mail")]
    [Required(ErrorMessage = "Informe seu e-mail.")]
    [EmailAddress(ErrorMessage = "E-mail inválido.")]
    [StringLength(320)]
    public string? Email { get; init; }

    public bool Enviado { get; init; }
}
