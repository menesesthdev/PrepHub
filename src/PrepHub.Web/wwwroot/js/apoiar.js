// Botão "copiar código Pix". É o caminho que a maioria usa no celular: colar no aplicativo do
// banco é mais rápido do que apontar a câmera para a própria tela.
(function () {
    "use strict";

    const payload = document.getElementById("pix-payload");
    const botao = document.getElementById("btn-copiar-pix");
    const aviso = document.getElementById("aviso-copia");

    if (!payload || !botao) {
        return;
    }

    // navigator.clipboard só existe em contexto seguro (HTTPS ou localhost). Em HTTP atrás de
    // proxy sem TLS ele simplesmente não está lá, e o botão precisa continuar funcionando —
    // daí a segunda tentativa com o textarea temporário.
    async function copiar(texto) {
        if (navigator.clipboard && window.isSecureContext) {
            await navigator.clipboard.writeText(texto);
            return true;
        }

        const campo = document.createElement("textarea");
        campo.value = texto;
        campo.setAttribute("readonly", "");
        campo.style.position = "fixed";
        campo.style.opacity = "0";
        document.body.appendChild(campo);
        campo.select();

        try {
            return document.execCommand("copy");
        } finally {
            document.body.removeChild(campo);
        }
    }

    function avisar(mensagem) {
        if (aviso) {
            aviso.textContent = mensagem;
            setTimeout(function () { aviso.textContent = ""; }, 4000);
        }
    }

    botao.addEventListener("click", async function () {
        const texto = payload.textContent.trim();

        try {
            const copiou = await copiar(texto);
            avisar(copiou ? "Código copiado." : "Não foi possível copiar — selecione o código acima.");
        } catch {
            // Falha silenciosa aqui seria pior do que o erro: a pessoa acharia que copiou e
            // colaria o conteúdo anterior da área de transferência no app do banco.
            avisar("Não foi possível copiar — selecione o código acima.");
        }
    });
})();
