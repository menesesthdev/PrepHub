namespace PrepHub.Domain.Autenticacao;

/// <summary>Validade do link que confirma o e-mail de uma conta local recém-criada.</summary>
/// <remarks>
/// Vinte e quatro horas, e não uma hora como na redefinição de senha, porque os dois links
/// correm riscos diferentes. O de redefinição, se vazar, TROCA a senha e toma a conta — daí o
/// prazo curto. Este só prova que o endereço existe e pertence a quem cadastrou; vazado, no pior
/// caso confirma um e-mail que já era da própria pessoa. O que aperta aqui é o outro lado: a
/// conta não entra enquanto não confirmar, então expirar durante a noite deixaria de fora quem
/// se cadastrou tarde e só abriu o e-mail no dia seguinte.
///
/// Expirar não tranca ninguém para sempre: <c>/conta/reenviar-confirmacao</c> emite um link novo
/// e invalida o anterior.
/// </remarks>
public static class PoliticaDeConfirmacaoDeEmail
{
    public static readonly TimeSpan Validade = TimeSpan.FromHours(24);
}
