using PrepHub.Application.Abstractions;
using PrepHub.Infrastructure.Doacao;
using PrepHub.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PrepHub.Web.Controllers;

/// <summary>
/// Página de apoio ao projeto. É <see cref="AllowAnonymousAttribute"/> de propósito: quem ainda
/// não criou conta é justamente quem pode ter usado o simulado e querer contribuir, e obrigar
/// login para doar seria um pedágio absurdo.
/// </summary>
/// <remarks>
/// Sem chave Pix configurada a rota inteira responde 404 — não existe estado "página de doação
/// meio pronta" pedindo contribuição para lugar nenhum.
/// </remarks>
[AllowAnonymous]
[Route("apoiar")]
public class ApoiarController : Controller
{
    private readonly OpcoesDeDoacao _opcoes;
    private readonly IGeradorDeQrCode _qrCode;

    public ApoiarController(OpcoesDeDoacao opcoes, IGeradorDeQrCode qrCode)
    {
        _opcoes = opcoes;
        _qrCode = qrCode;
    }

    [HttpGet("")]
    public IActionResult Index(decimal? valor)
    {
        if (!_opcoes.EstaConfigurado)
        {
            return NotFound();
        }

        // Só valor da lista entra no payload. Qualquer outra coisa vinda da query string vira
        // valor aberto — assim a URL não é capaz de gerar um QR de valor arbitrário em nome do
        // projeto, que seria um belo vetor de golpe se a página fosse compartilhada.
        var selecionado = valor is { } informado && _opcoes.ValoresSugeridos.Contains(informado)
            ? informado
            : (decimal?)null;

        var payload = BrCodePix.Gerar(
            _opcoes.ChavePix!,
            _opcoes.NomeDoRecebedor,
            _opcoes.Cidade,
            selecionado);

        return View(new ApoiarViewModel
        {
            Payload = payload,
            QrCodeSvg = _qrCode.GerarSvg(payload),
            ValorSelecionado = selecionado,
            ValoresSugeridos = _opcoes.ValoresSugeridos,
            CustoMensal = _opcoes.CustoMensal,
            LinkExterno = _opcoes.LinkExterno,
            LinkExternoRotulo = _opcoes.LinkExternoRotulo
        });
    }
}
