using System.ComponentModel.DataAnnotations;

namespace PrepHub.Web.Models;

/// <summary>
/// Tela mostrada logo depois do cadastro: a conta existe, mas só entra depois que o link
/// enviado por e-mail for aberto.
/// </summary>
/// <remarks>
/// O e-mail chega por querystring e serve apenas para a frase "enviamos para …". Não é dado de
/// confiança e não decide nada — quem sabe quem tem conta pendente é o servidor, e é ele que
/// decide se algum e-mail sai, no POST de reenvio.
/// </remarks>
public sealed class ConfirmeSeuEmailViewModel
{
    public string? Email { get; init; }

    /// <summary>Verdadeiro depois de um reenvio, para a tela confirmar que o pedido foi aceito.</summary>
    public bool Reenviado { get; init; }
}

/// <summary>
/// Pedido de um link de confirmação novo. Responde SEMPRE a mesma coisa, exista ou não conta
/// pendente com aquele e-mail — mesma disciplina de <see cref="EsqueciSenhaViewModel"/>.
/// </summary>
public sealed class ReenviarConfirmacaoViewModel
{
    [Display(Name = "E-mail")]
    [Required(ErrorMessage = "Informe seu e-mail.")]
    [EmailAddress(ErrorMessage = "E-mail inválido.")]
    [StringLength(320)]
    public string? Email { get; init; }

    public bool Enviado { get; init; }
}
