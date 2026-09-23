using PrepHub.Application.Historico;
using Microsoft.AspNetCore.Mvc;

namespace PrepHub.Web.Controllers;

/// <summary>
/// Histórico de simulados de quem está logado. Não recebe id de usuário em nenhuma rota — o
/// serviço resolve o dono pela sessão, então não existe URL capaz de pedir o histórico de outra
/// pessoa.
/// </summary>
[Route("historico")]
public class HistoricoController : Controller
{
    private readonly IHistoricoDeProvasService _historico;

    public HistoricoController(IHistoricoDeProvasService historico)
    {
        _historico = historico;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => View(await _historico.ObterHistoricoAsync(cancellationToken));
}
