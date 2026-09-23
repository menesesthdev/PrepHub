// Seletor "Esquema de cores" das telas AWS — o mesmo recurso de acessibilidade que a entrega real
// oferece na faixa de ferramentas (preto sobre amarelo, branco sobre preto...). O esquema muda só a
// área de conteúdo; as barras continuam azuis, como no demo.
//
// A escolha é uma conveniência de quem está vendo, então fica no navegador (localStorage): não é
// dado da prova e não precisa ir ao servidor. O acesso é protegido porque o armazenamento pode
// estar bloqueado (janela privada, cookies desligados) — e aí o seletor funciona só na página atual.
(function () {
    "use strict";

    const CHAVE = "prephub.aws.esquema";
    const select = document.getElementById("color-scheme");
    if (!select) return;

    function aplicar(esquema) {
        if (esquema) {
            document.body.setAttribute("data-scheme", esquema);
        } else {
            document.body.removeAttribute("data-scheme");
        }
    }

    let salvo = "";
    try { salvo = localStorage.getItem(CHAVE) || ""; } catch (e) { salvo = ""; }

    if (salvo && select.querySelector(`option[value="${salvo}"]`)) {
        select.value = salvo;
        aplicar(salvo);
    }

    select.addEventListener("change", function () {
        aplicar(select.value);
        try { localStorage.setItem(CHAVE, select.value); } catch (e) { /* sem armazenamento: vale só aqui */ }
    });
})();
