namespace PrepHub.Web.Autenticacao;

/// <summary>
/// Nomes dos esquemas de autenticação.
/// </summary>
/// <remarks>
/// São dois cookies de propósito diferente: <see cref="Aplicacao"/> é a sessão de fato, e
/// <see cref="Externo"/> é um cookie temporário onde o handler OAuth deposita as claims do
/// provedor até nós as trocarmos por um usuário local. Sem essa separação, o handler assinaria
/// direto a sessão com as claims do Google/GitHub e nunca teríamos onde encaixar o nosso Usuario.
/// </remarks>
public static class EsquemasDeAutenticacao
{
    public const string Aplicacao = "PrepHub.Cookie";
    public const string Externo = "PrepHub.External";
}
