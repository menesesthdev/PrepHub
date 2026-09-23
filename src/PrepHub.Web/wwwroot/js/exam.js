// Lógica da tela de prova, fiel à entrega real (Pearson VUE): navegação linear sem recarregar a
// página, tela de revisão como único meio de saltar entre itens, timer com submissão automática e
// gravação incremental das respostas.
//
// Serve as duas telas — Microsoft e AWS (body[data-vendor="aws"]). O que é igual (gravação, timer,
// marcação, respostas por par) fica num caminho só; o que muda é o FLUXO da revisão: na Microsoft a
// tela de revisão está sempre a um botão; na AWS ela aparece depois do último item, troca a moldura
// (faixa com Instruções, fundo cinza) e "Revisar todas" percorre as questões em sequência. Nas duas,
// os filtros só filtram a tabela.
(function () {
    "use strict";

    const data = JSON.parse(document.getElementById("exam-data").textContent);
    const total = data.totalQuestions;
    const isAws = document.body.getAttribute("data-vendor") === "aws";
    const counterEl = document.getElementById("question-counter");

    // AWS: o botão "Tela de revisão" só existe depois que a revisão apareceu uma vez, e os botões
    // de revisão percorrem uma fila de questões (todas, incompletas ou marcadas) em sequência.
    let reviewReached = false;
    let reviewQueue = null;

    // Status por item (index 0 = item 1). "seen" não vem do servidor: um item respondido
    // obrigatoriamente foi visto, e o item 1 é visto assim que a prova abre.
    const statuses = new Array(total);
    data.statuses.forEach(function (s) {
        statuses[s.number - 1] = {
            answered: s.answered,
            flagged: s.flagged,
            selectedCount: s.selectedCount,
            required: s.required,
            seen: s.answered
        };
    });

    // Comentários por item — a prova real permite anotar feedback sobre a questão.
    const comments = new Map();

    let currentNumber = 1;
    let remaining = data.remainingSeconds;
    let questionEnteredAt = Date.now();
    let finished = false;
    let reviewFilter = "all";
    let inReview = false;

    const container = document.getElementById("question-container");
    const questionView = document.getElementById("question-view");
    const reviewView = document.getElementById("review-view");
    const timerEl = document.getElementById("timer");
    const flagToggle = document.getElementById("flag-toggle");
    const reviewRows = document.getElementById("review-rows");
    const reviewEmpty = document.getElementById("review-empty");

    const btnPrev = document.getElementById("btn-prev");
    const btnNext = document.getElementById("btn-next");
    const btnReview = document.getElementById("btn-review");
    const btnEnd = document.getElementById("btn-end");
    const btnComments = document.getElementById("btn-comments");
    const markToggleEl = flagToggle.closest(".mark-toggle");

    // ---- Helpers de rede -------------------------------------------------

    // Token antiforgery do documento. Vai por cabeçalho porque estas chamadas não têm
    // formulário; sem ele o servidor recusa gravar resposta e encerrar a prova.
    const antiforgeryToken = document.querySelector("input[name='__RequestVerificationToken']").value;

    function questionUrl(n) { return data.urls.question.replace("{n}", n); }

    async function postAnswer(payload) {
        await fetch(data.urls.answer, {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "RequestVerificationToken": antiforgeryToken
            },
            credentials: "same-origin",
            body: JSON.stringify(payload)
        });
    }

    // ---- Estado ----------------------------------------------------------
    function statusOf(n) {
        return statuses[n - 1] || { answered: false, flagged: false, selectedCount: 0, required: 1, seen: false };
    }

    // Classificação idêntica à da tela de revisão real: um item de múltipla resposta com
    // seleção parcial é "Incompleto", não "Completo".
    function classify(n) {
        const st = statusOf(n);
        if (st.selectedCount >= st.required && st.selectedCount > 0) return "complete";
        if (st.seen || st.selectedCount > 0) return "incomplete";
        return "unseen";
    }

    const CLASSIFICATION_LABELS = { complete: "Completo", incomplete: "Incompleto", unseen: "Não visto" };

    // ---- Leitura do DOM da questão atual ---------------------------------
    function currentQuestionEl() { return container.querySelector(".question"); }

    function collectSelection() {
        const el = currentQuestionEl();
        if (!el) return { questionId: null, selected: [] };
        const inputs = el.querySelectorAll(".option__input:checked");
        return {
            questionId: el.getAttribute("data-question-id"),
            selected: Array.from(inputs).map(function (i) { return i.value; })
        };
    }

    // Grava a resposta do item atual (seleção + marcação + tempo gasto no intervalo).
    async function saveCurrent() {
        const selection = collectSelection();
        if (!selection.questionId) return;

        const idx = currentNumber - 1;
        const st = statusOf(currentNumber);
        const spent = Math.max(0, Math.round((Date.now() - questionEnteredAt) / 1000));
        questionEnteredAt = Date.now();

        statuses[idx] = {
            answered: selection.selected.length > 0,
            flagged: st.flagged,
            selectedCount: selection.selected.length,
            required: st.required,
            seen: true
        };

        await postAnswer({
            questionId: selection.questionId,
            selectedOptionIds: selection.selected,
            isFlaggedForReview: st.flagged,
            timeSpentSeconds: spent
        });
    }

    // ---- Navegação -------------------------------------------------------
    async function goTo(n) {
        if (finished || n < 1 || n > total) return;
        await saveCurrent();

        const resp = await fetch(questionUrl(n), { credentials: "same-origin" });
        if (!resp.ok) return;
        container.innerHTML = await resp.text();

        currentNumber = n;
        questionEnteredAt = Date.now();
        if (counterEl) counterEl.textContent = `Questão ${n} de ${total}`;
        pickedItem = null; // item pendente de arrastar não atravessa a navegação

        // Sincroniza "required" com o que o servidor devolveu para este item.
        const el = currentQuestionEl();
        const st = statusOf(n);
        st.required = parseInt(el.getAttribute("data-required"), 10) || 1;
        st.seen = true;
        statuses[n - 1] = st;

        showQuestionView();
        window.scrollTo(0, 0);
    }

    // ---- Alternância entre questão e tela de revisão ---------------------
    function showQuestionView() {
        inReview = false;
        questionView.hidden = false;
        reviewView.hidden = true;
        flagToggle.checked = statusOf(currentNumber).flagged;
        renderCommentsButton();
        renderFooter();
    }

    async function showReviewView() {
        await saveCurrent();
        inReview = true;
        reviewReached = true;
        reviewQueue = null;
        questionView.hidden = true;
        reviewView.hidden = false;
        renderReviewRows();
        renderFooter();
        window.scrollTo(0, 0);
    }

    // A barra inferior muda conforme a tela — igual à prova real.
    function renderFooter() {
        if (isAws) { renderFooterAws(); return; }

        markToggleEl.hidden = inReview;
        btnComments.hidden = inReview;

        if (inReview) {
            btnPrev.hidden = true;
            btnNext.hidden = true;
            btnReview.hidden = false;
            btnReview.textContent = "Voltar à prova";
            btnEnd.hidden = false;
            return;
        }

        btnReview.hidden = false;
        btnReview.textContent = "Tela de revisão";
        btnPrev.hidden = false;
        btnPrev.disabled = currentNumber === 1;

        // No último item, "Próxima" dá lugar a "Encerrar prova".
        const isLast = currentNumber === total;
        btnNext.hidden = isLast;
        btnEnd.hidden = !isLast;
    }

    const reviewButtons = Array.from(document.querySelectorAll(".btn--filter"));
    const btnInstructions = document.getElementById("btn-instructions");
    const counterLine = document.getElementById("counter-line");

    // AWS: rodapé só com Anterior/Próxima durante a prova; na revisão, os três "Revisar..." e
    // "Encerrar revisão". "Tela de revisão" volta a estar à mão depois da primeira passagem por ela.
    function renderFooterAws() {
        markToggleEl.hidden = inReview;
        btnComments.hidden = inReview;
        btnInstructions.hidden = !inReview;
        counterLine.hidden = inReview;
        document.body.classList.toggle("is-review", inReview);
        btnEnd.hidden = !inReview;

        // Na primeira questão o demo não mostra "Anterior" — o botão some, não fica desabilitado.
        const isFirst = reviewQueue ? reviewQueue.indexOf(currentNumber) <= 0 : currentNumber === 1;
        btnPrev.hidden = inReview || isFirst;
        btnNext.hidden = inReview;
        btnReview.hidden = inReview || !reviewReached;
    }

    // Revisão da AWS: selo de status, "Sim/Não" para marcada e link "Revisar" por linha; as abas
    // levam a contagem, e "Marcadas" só aparece quando existe alguma.
    function renderReviewRowsAws() {
        reviewRows.innerHTML = "";
        const counts = { all: total, incomplete: 0, marked: 0 };
        for (let n = 1; n <= total; n++) {
            if (classify(n) !== "complete") counts.incomplete++;
            if (statusOf(n).flagged) counts.marked++;
        }

        const markedTab = document.querySelector('.aws-review__tab[data-filter="marked"]');
        markedTab.hidden = counts.marked === 0;
        if (reviewFilter === "marked" && counts.marked === 0) {
            reviewFilter = "all";
            reviewButtons.forEach(function (b) { b.classList.toggle("is-active", b.getAttribute("data-filter") === "all"); });
        }
        document.querySelectorAll("[data-count]").forEach(function (el) {
            el.textContent = counts[el.getAttribute("data-count")];
        });

        let shown = 0;
        for (let n = 1; n <= total; n++) {
            const complete = classify(n) === "complete";
            const st = statusOf(n);
            if (reviewFilter === "incomplete" && complete) continue;
            if (reviewFilter === "marked" && !st.flagged) continue;
            shown++;

            const tr = document.createElement("tr");
            tr.className = "review-row";
            tr.tabIndex = 0;
            tr.setAttribute("role", "button");
            tr.setAttribute("data-number", n);

            const tdNumber = document.createElement("td");
            tdNumber.textContent = n;

            const tdTitle = document.createElement("td");
            tdTitle.textContent = "Questão";

            const tdStatus = document.createElement("td");
            const badge = document.createElement("span");
            badge.className = "aws-badge " + (complete ? "aws-badge--complete" : "aws-badge--incomplete");
            badge.textContent = complete ? "Completa" : "Incompleta";
            tdStatus.appendChild(badge);

            const tdMarked = document.createElement("td");
            tdMarked.textContent = st.flagged ? "Sim" : "Não";

            const tdAction = document.createElement("td");
            tdAction.className = "aws-review__action";
            tdAction.textContent = "Revisar";

            tr.append(tdNumber, tdTitle, tdStatus, tdMarked, tdAction);
            reviewRows.appendChild(tr);
        }

        reviewEmpty.hidden = shown > 0;
    }

    function nextAws() {
        if (reviewQueue) {
            const next = reviewQueue[reviewQueue.indexOf(currentNumber) + 1];
            if (next) { goTo(next); } else { showReviewView(); }
            return;
        }
        // Depois do último item vem a tela de revisão, não o encerramento.
        if (currentNumber === total) { showReviewView(); } else { goTo(currentNumber + 1); }
    }

    function prevAws() {
        if (reviewQueue) {
            const prev = reviewQueue[reviewQueue.indexOf(currentNumber) - 1];
            if (prev) goTo(prev);
            return;
        }
        goTo(currentNumber - 1);
    }

    function startReviewQueue(filter) {
        const queue = [];
        for (let n = 1; n <= total; n++) {
            if (filter === "incomplete" && classify(n) === "complete") continue;
            if (filter === "marked" && !statusOf(n).flagged) continue;
            queue.push(n);
        }
        if (queue.length === 0) {
            reviewEmpty.hidden = false;
            return;
        }
        reviewQueue = queue;
        goTo(queue[0]);
    }

    function renderCommentsButton() {
        btnComments.classList.toggle("has-comment", comments.has(currentNumber));
    }

    // ---- Tela de revisão -------------------------------------------------
    function renderReviewRows() {
        if (isAws) { renderReviewRowsAws(); return; }
        reviewRows.innerHTML = "";
        let shown = 0;

        for (let n = 1; n <= total; n++) {
            const kind = classify(n);
            const st = statusOf(n);

            if (reviewFilter === "incomplete" && kind === "complete") continue;
            if (reviewFilter === "marked" && !st.flagged) continue;
            shown++;

            const tr = document.createElement("tr");
            tr.className = "review-row review-row--" + kind;
            tr.tabIndex = 0;
            tr.setAttribute("role", "button");
            tr.setAttribute("data-number", n);

            const tdItem = document.createElement("td");
            tdItem.className = "review-row__item";
            tdItem.textContent = n;

            const tdStatus = document.createElement("td");
            tdStatus.textContent = CLASSIFICATION_LABELS[kind];

            const tdMark = document.createElement("td");
            tdMark.className = "review-row__mark";
            tdMark.textContent = st.flagged ? "✔" : "";

            tr.append(tdItem, tdStatus, tdMark);
            reviewRows.appendChild(tr);
        }

        reviewEmpty.hidden = shown > 0;
    }

    // ---- Timer -----------------------------------------------------------
    function formatTime(sec) {
        const s = Math.max(0, sec);
        const h = Math.floor(s / 3600);
        const m = Math.floor((s % 3600) / 60);
        const ss = s % 60;
        const pad = function (v) { return String(v).padStart(2, "0"); };
        // A AWS mostra minutos e segundos ("29:45"); com uma hora ou mais, a hora vem na frente.
        if (isAws) return h > 0 ? `${h}:${pad(m)}:${pad(ss)}` : `${pad(m)}:${pad(ss)}`;
        return `${pad(h)}:${pad(m)}:${pad(ss)}`;
    }

    function tickTimer() {
        timerEl.textContent = formatTime(remaining);
        timerEl.classList.toggle("is-low", remaining <= 300);
        if (remaining <= 0 && !finished) {
            // Tempo zerado = submissão automática, sem exceção.
            doFinish(true);
            return;
        }
        remaining -= 1;
    }

    // ---- Finalização -----------------------------------------------------
    async function doFinish(auto) {
        if (finished) return;
        finished = true;
        if (!auto) {
            await saveCurrent();
        }
        const resp = await fetch(data.urls.finish, {
            method: "POST",
            headers: { "RequestVerificationToken": antiforgeryToken },
            credentials: "same-origin"
        });
        const json = await resp.json();
        window.location.href = json.redirectUrl;
    }

    // ---- Modais ----------------------------------------------------------
    const finishModal = document.getElementById("finish-modal");
    const finishSummary = document.getElementById("finish-summary");
    const commentsModal = document.getElementById("comments-modal");
    const commentsText = document.getElementById("comments-text");

    async function openFinishModal() {
        await saveCurrent();

        let complete = 0, incomplete = 0, unseen = 0, marked = 0;
        for (let n = 1; n <= total; n++) {
            const kind = classify(n);
            if (kind === "complete") complete++;
            else if (kind === "incomplete") incomplete++;
            else unseen++;
            if (statusOf(n).flagged) marked++;
        }

        finishSummary.textContent = isAws
            ? `Respondidas: ${complete} de ${total}. Incompletas: ${incomplete + unseen}. Marcadas para revisão: ${marked}.`
            : `Completos: ${complete} de ${total}. ` +
              `Incompletos: ${incomplete}. Não vistos: ${unseen}. Marcados: ${marked}.`;
        finishModal.hidden = false;
    }

    function openCommentsModal() {
        commentsText.value = comments.get(currentNumber) || "";
        commentsModal.hidden = false;
        commentsText.focus();
    }

    // ---- Arrastar e soltar (questões de associação) -----------------------
    //
    // A resposta continua sendo os checkboxes ocultos que o servidor renderizou: um por par
    // candidato (alvo × item). Arrastar, clicar ou usar o teclado são só três formas de marcar a
    // caixa certa — por isso nada abaixo fala com a rede, e saveCurrent/collectSelection seguem
    // sem saber que este tipo existe.
    //
    // Tudo é delegado no container porque o HTML da questão é substituído a cada navegação:
    // handler preso ao elemento morreria junto com ele, silenciosamente, a partir do item 2.

    const PLACEHOLDER = "Solte um item aqui";
    let pickedItem = null;

    function pairInputs() {
        const el = currentQuestionEl();
        return el ? Array.from(el.querySelectorAll(".dnd__pairs .option__input")) : [];
    }

    function slotFor(target) {
        const el = currentQuestionEl();
        if (!el) return null;
        return Array.from(el.querySelectorAll(".dnd__slot"))
            .find(function (s) { return s.getAttribute("data-target") === target; }) || null;
    }

    // Um alvo guarda um item, nunca dois: marcar é sempre desmarcar o par anterior daquele alvo.
    function setPair(target, item) {
        pairInputs().forEach(function (input) {
            if (input.getAttribute("data-target") !== target) return;
            input.checked = item !== null && input.getAttribute("data-item") === item;
        });

        const slot = slotFor(target);
        if (slot) {
            slot.classList.toggle("is-filled", item !== null);
            slot.querySelector(".dnd__slot-text").textContent = item === null ? PLACEHOLDER : item;
        }

        // Marcar por script não dispara "change", então a gravação é chamada aqui na mão — é o
        // mesmo saveCurrent que o clique num radio dispara pelo caminho normal.
        saveCurrent();
    }

    function setPicked(item) {
        pickedItem = item;
        const el = currentQuestionEl();
        if (!el) return;
        el.querySelectorAll(".dnd__item").forEach(function (btn) {
            const isPicked = item !== null && btn.getAttribute("data-item") === item;
            btn.classList.toggle("is-picked", isPicked);
            btn.setAttribute("aria-pressed", isPicked ? "true" : "false");
        });
    }

    container.addEventListener("click", function (e) {
        const item = e.target.closest(".dnd__item");
        if (item) {
            const texto = item.getAttribute("data-item");
            setPicked(pickedItem === texto ? null : texto);
            return;
        }

        const slot = e.target.closest(".dnd__slot");
        if (!slot) return;

        if (pickedItem !== null) {
            setPair(slot.getAttribute("data-target"), pickedItem);
            setPicked(null);
        } else if (slot.classList.contains("is-filled")) {
            // Clicar num alvo já preenchido sem item selecionado devolve o item ao painel — é o
            // equivalente a arrastá-lo de volta, que é como se corrige um engano.
            setPair(slot.getAttribute("data-target"), null);
        }
    });

    container.addEventListener("dragstart", function (e) {
        const item = e.target.closest(".dnd__item");
        if (!item) return;
        const texto = item.getAttribute("data-item");
        e.dataTransfer.setData("text/plain", texto);
        e.dataTransfer.effectAllowed = "copy";
        setPicked(texto);
    });

    container.addEventListener("dragover", function (e) {
        const slot = e.target.closest(".dnd__slot");
        if (!slot) return;
        e.preventDefault(); // sem isso o navegador recusa o drop
        e.dataTransfer.dropEffect = "copy";
        slot.classList.add("is-over");
    });

    container.addEventListener("dragleave", function (e) {
        const slot = e.target.closest(".dnd__slot");
        if (slot) slot.classList.remove("is-over");
    });

    container.addEventListener("drop", function (e) {
        const slot = e.target.closest(".dnd__slot");
        if (!slot) return;
        e.preventDefault();
        slot.classList.remove("is-over");

        const texto = e.dataTransfer.getData("text/plain") || pickedItem;
        if (texto) setPair(slot.getAttribute("data-target"), texto);
        setPicked(null);
    });

    // ---- Ligações de eventos --------------------------------------------
    container.addEventListener("change", function (e) {
        // Lista suspensa da AWS (ordenação e associação): escolher um item é marcar o par
        // alvo × item — o mesmo setPair que o arrastar da Microsoft usa.
        if (e.target.classList.contains("dd-select")) {
            setPair(e.target.getAttribute("data-target"), e.target.value || null);
            return;
        }
        if (e.target.classList.contains("option__input")) {
            saveCurrent();
        }
    });

    flagToggle.addEventListener("change", function () {
        const st = statusOf(currentNumber);
        st.flagged = flagToggle.checked;
        statuses[currentNumber - 1] = st;
        saveCurrent();
    });

    btnPrev.addEventListener("click", function () { if (isAws) { prevAws(); } else { goTo(currentNumber - 1); } });
    btnNext.addEventListener("click", function () { if (isAws) { nextAws(); } else { goTo(currentNumber + 1); } });
    btnEnd.addEventListener("click", openFinishModal);

    btnReview.addEventListener("click", function () {
        if (inReview) { showQuestionView(); } else { showReviewView(); }
    });

    reviewButtons.forEach(function (btn) {
        btn.addEventListener("click", function () {
            reviewFilter = btn.getAttribute("data-filter");
            reviewButtons.forEach(function (b) {
                b.classList.toggle("is-active", b === btn);
            });
            renderReviewRows();
        });
    });

    function jumpFromReview(e) {
        const row = e.target.closest(".review-row");
        if (!row) return;
        reviewQueue = null;
        goTo(parseInt(row.getAttribute("data-number"), 10));
    }

    reviewRows.addEventListener("click", jumpFromReview);
    reviewRows.addEventListener("keydown", function (e) {
        if (e.key === "Enter" || e.key === " ") {
            e.preventDefault();
            jumpFromReview(e);
        }
    });

    btnComments.addEventListener("click", openCommentsModal);

    if (isAws) {
        const instructionsModal = document.getElementById("instructions-modal");
        document.getElementById("btn-review-all").addEventListener("click", function () { startReviewQueue("all"); });
        btnInstructions.addEventListener("click", function () { instructionsModal.hidden = false; });
        document.getElementById("btn-instructions-close").addEventListener("click", function () {
            instructionsModal.hidden = true;
        });
    }
    document.getElementById("btn-comments-cancel").addEventListener("click", function () {
        commentsModal.hidden = true;
    });
    document.getElementById("btn-comments-save").addEventListener("click", function () {
        const text = commentsText.value.trim();
        if (text) { comments.set(currentNumber, text); } else { comments.delete(currentNumber); }
        commentsModal.hidden = true;
        renderCommentsButton();
    });

    document.getElementById("btn-finish-cancel").addEventListener("click", function () {
        finishModal.hidden = true;
    });
    document.getElementById("btn-finish-confirm").addEventListener("click", function () {
        doFinish(false);
    });

    // ---- Início ----------------------------------------------------------
    const firstEl = currentQuestionEl();
    if (firstEl) {
        const st = statusOf(1);
        st.required = parseInt(firstEl.getAttribute("data-required"), 10) || 1;
        st.seen = true;
        statuses[0] = st;
    }

    showQuestionView();
    tickTimer();
    setInterval(tickTimer, 1000);
})();
