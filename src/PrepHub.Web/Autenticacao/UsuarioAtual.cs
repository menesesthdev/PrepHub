using System.Security.Claims;
using PrepHub.Application.Abstractions;

namespace PrepHub.Web.Autenticacao;

/// <summary>
/// Lê o usuário logado do cookie de sessão. O <c>NameIdentifier</c> aqui é o Id do nosso
/// <c>Usuario</c> — não o id do provedor externo, que só é usado no momento do login.
/// </summary>
public sealed class UsuarioAtual : IUsuarioAtual
{
    private readonly IHttpContextAccessor _accessor;

    public UsuarioAtual(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public Guid? Id
    {
        get
        {
            var value = _accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }
}
