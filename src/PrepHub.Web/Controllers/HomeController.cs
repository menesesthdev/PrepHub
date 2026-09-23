using System.Diagnostics;
using PrepHub.Application.Exames;
using PrepHub.Application.Historico;
using PrepHub.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PrepHub.Web.Controllers;

public class HomeController : Controller
{
    private readonly ICatalogoDeExamesService _catalog;
    private readonly IHistoricoDeProvasService _historico;

    public HomeController(ICatalogoDeExamesService catalog, IHistoricoDeProvasService historico)
    {
        _catalog = catalog;
        _historico = historico;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var exams = await _catalog.ObterExamesDisponiveisAsync(cancellationToken);
        var historico = await _historico.ObterHistoricoAsync(cancellationToken);

        return View(new InicioViewModel(exams, historico.EmAndamento));
    }

    // A página de erro precisa responder mesmo para quem não está logado — caso contrário
    // uma falha durante o login viraria um laço de redirecionamento para /conta/login.
    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
