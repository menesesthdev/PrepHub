// Abas de fornecedor da tela inicial. Todos os exames já vêm no HTML, agrupados por fornecedor;
// trocar de aba só mostra e esconde os blocos, sem recarregar a página nem mudar a URL.
//
// As abas nascem com [hidden] e são reveladas aqui: sem JavaScript a pessoa vê os dois blocos
// completos, em vez de botões que não fazem nada.
(function () {
    "use strict";

    const abas = document.querySelector("[data-vendor-tabs]");
    if (!abas) return;

    const botoes = Array.from(abas.querySelectorAll("[data-vendor-filter]"));
    const grupos = Array.from(document.querySelectorAll(".exam-group[data-vendor]"));

    function selecionar(filtro) {
        botoes.forEach(function (botao) {
            const ativo = botao.getAttribute("data-vendor-filter") === filtro;
            botao.classList.toggle("is-active", ativo);
            botao.setAttribute("aria-pressed", ativo ? "true" : "false");
        });
        grupos.forEach(function (grupo) {
            grupo.hidden = filtro !== "todos" && grupo.getAttribute("data-vendor") !== filtro;
        });
    }

    abas.addEventListener("click", function (evento) {
        const botao = evento.target.closest("[data-vendor-filter]");
        if (botao) selecionar(botao.getAttribute("data-vendor-filter"));
    });

    abas.hidden = false;
})();
