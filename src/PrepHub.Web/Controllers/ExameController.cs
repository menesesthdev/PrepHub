using PrepHub.Application.Contracts;
using PrepHub.Application.Exames;
using PrepHub.Application.Sessoes;
using PrepHub.Domain.Enums;
using PrepHub.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace PrepHub.Web.Controllers;

[Route("exam")]
public class ExameController : Controller
{
    private readonly ISessaoDeProvaService _session;
    private readonly ICatalogoDeExamesService _catalogo;

    public ExameController(ISessaoDeProvaService session, ICatalogoDeExamesService catalogo)
    {
        _session = session;
        _catalogo = catalogo;
    }

    // Tela de instruções da AWS, antes da primeira questão e sem relógio — como no demo oficial.
    // É GET e não cria nada: a tentativa (e o tempo) só começa no POST de "Próxima", que é o mesmo
    // Iniciar de sempre. Assim ler as instruções não consome tempo de prova.
    [HttpGet("start/{examId:guid}")]
    public async Task<IActionResult> Instrucoes(Guid examId, CancellationToken cancellationToken)
    {
        var exame = await _catalogo.ObterExameDisponivelAsync(examId, cancellationToken);
        if (exame is null)
        {
            return NotFound();
        }

        // A entrega da Microsoft não tem esta etapa: quem chega aqui por URL segue para o catálogo.
        if (exame.Vendor != FornecedorDoExame.Aws)
        {
            return RedirectToAction("Index", "Home");
        }

        return View("InstrucoesAws", new InstrucoesAwsViewModel(
            exame.Id, exame.Code, exame.Name, exame.TotalQuestions, exame.TimeLimitMinutes));
    }

    // Inicia uma nova tentativa e leva para a tela de prova.
    [HttpPost("start")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Iniciar(Guid examId, CancellationToken cancellationToken)
    {
        var attemptId = await _session.IniciarTentativaAsync(examId, cancellationToken);
        return RedirectToAction(nameof(Realizar), new { attemptId });
    }

    // Shell da prova: header fixo, painel de navegação e a primeira questão.
    [HttpGet("{attemptId:guid}")]
    public async Task<IActionResult> Realizar(Guid attemptId, CancellationToken cancellationToken)
    {
        var state = await _session.ObterEstadoAsync(attemptId, cancellationToken);
        if (state is null)
        {
            return NotFound();
        }

        if (state.IsFinished)
        {
            return RedirectToAction(nameof(Resultado), new { attemptId });
        }

        var firstQuestion = await _session.ObterQuestaoAsync(attemptId, 1, cancellationToken);
        if (firstQuestion is null)
        {
            return NotFound();
        }

        // Cada fornecedor entrega a prova numa tela diferente, e fidelidade à tela real é o produto:
        // a da AWS não é um tema da da Microsoft, é outra disposição, outro fluxo de revisão e outro
        // formato de resposta (lista suspensa em vez de arrastar).
        var view = state.Vendor == FornecedorDoExame.Aws ? "RealizarAws" : "Realizar";
        return View(view, new RealizarProvaViewModel(state, firstQuestion));
    }

    // Partial de uma questão (navegação sem recarregar a página — fidelidade estilo SPA).
    [HttpGet("{attemptId:guid}/question/{number:int}")]
    public async Task<IActionResult> Questao(Guid attemptId, int number, CancellationToken cancellationToken)
    {
        var question = await _session.ObterQuestaoAsync(attemptId, number, cancellationToken);
        if (question is null)
        {
            return NotFound();
        }

        return PartialView(question.Vendor == FornecedorDoExame.Aws ? "_QuestaoAws" : "_Questao", question);
    }

    // Estado atual (painel lateral + tempo restante) em JSON para o front-end sincronizar.
    [HttpGet("{attemptId:guid}/state")]
    public async Task<IActionResult> State(Guid attemptId, CancellationToken cancellationToken)
    {
        var state = await _session.ObterEstadoAsync(attemptId, cancellationToken);
        return state is null ? NotFound() : Json(state);
    }

    // Grava/atualiza a resposta de uma questão (AJAX).
    //
    // O antiforgery vem por cabeçalho (ver AddAntiforgery em Program.cs), porque aqui não há
    // formulário: o exam.js lê o token do documento e o envia junto. Não depender do
    // Content-Type para isso é deliberado — o `[FromBody]` já obriga application/json, o que
    // por acidente bloqueia formulário cross-site, mas acidente não é defesa: bastaria alguém
    // trocar o binding no futuro para o endpoint ficar aberto sem ninguém notar.
    [HttpPost("{attemptId:guid}/answer")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SalvarResposta(Guid attemptId, [FromBody] SalvarRespostaInput input, CancellationToken cancellationToken)
    {
        if (input is null)
        {
            return BadRequest();
        }

        var resultado = await _session.SalvarRespostaAsync(
            new SalvarRespostaRequest(
                attemptId,
                input.QuestionId,
                input.SelectedOptionIds,
                input.IsFlaggedForReview,
                input.TimeSpentSeconds),
            cancellationToken);

        return resultado switch
        {
            ResultadoDeSalvarResposta.Gravada => Ok(),

            // 409 e não 400: o pedido estava correto, só chegou depois de a prova fechar (caso
            // normal quando o tempo vence entre uma navegação e o envio).
            ResultadoDeSalvarResposta.TentativaEncerrada => Conflict(),

            _ => BadRequest()
        };
    }

    // Finaliza e corrige. Retorna a URL do resultado para o front-end navegar
    // (funciona tanto no "Finalizar prova" manual quanto na submissão automática por tempo).
    //
    // Antiforgery é obrigatório aqui: a ação não recebe corpo, então sem o token um formulário
    // em site de terceiro encerraria a prova de quem está logado — e encerrar é irreversível.
    [HttpPost("{attemptId:guid}/finish")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Finalizar(Guid attemptId, CancellationToken cancellationToken)
    {
        await _session.FinalizarTentativaAsync(attemptId, cancellationToken);
        return Json(new { redirectUrl = Url.Action(nameof(Resultado), new { attemptId }) });
    }

    // Score report: nota na escala 1–1000 e desempenho por domínio — o que a prova real entrega.
    [HttpGet("{attemptId:guid}/result")]
    public async Task<IActionResult> Resultado(Guid attemptId, CancellationToken cancellationToken)
    {
        var result = await _session.ObterResultadoAsync(attemptId, cancellationToken);
        if (result is null)
        {
            return RedirectToAction(nameof(Realizar), new { attemptId });
        }

        return View(result);
    }

    // Modo de estudo: revisão questão a questão com gabarito e explicação. Fica fora do score
    // report de propósito — a prova real não revela quais itens o candidato errou.
    [HttpGet("{attemptId:guid}/review")]
    public async Task<IActionResult> Revisao(Guid attemptId, CancellationToken cancellationToken)
    {
        var result = await _session.ObterResultadoAsync(attemptId, cancellationToken);
        if (result is null)
        {
            return RedirectToAction(nameof(Realizar), new { attemptId });
        }

        return View(result);
    }
}
