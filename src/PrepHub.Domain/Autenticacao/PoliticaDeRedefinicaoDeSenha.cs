namespace PrepHub.Domain.Autenticacao;

/// <summary>Validade do link de redefinição de senha.</summary>
/// <remarks>
/// Uma hora é o meio entre dois riscos reais: link longo demais fica valendo na caixa de
/// entrada (e caixa de e-mail comprometida vira conta comprometida muito depois do fato), e
/// link curto demais expira antes de a pessoa ver o e-mail. O link também é de uso único, o
/// que importa mais que o prazo — depois de trocar a senha ele morre na hora.
/// </remarks>
public static class PoliticaDeRedefinicaoDeSenha
{
    public static readonly TimeSpan Validade = TimeSpan.FromHours(1);
}
