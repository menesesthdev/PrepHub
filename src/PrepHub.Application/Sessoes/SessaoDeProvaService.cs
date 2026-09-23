using PrepHub.Application.Abstractions;
using PrepHub.Application.Contracts;
using PrepHub.Application.Sorteios;
using PrepHub.Domain.Entidades;
using PrepHub.Domain.Correcao;
using PrepHub.Domain.Enums;
using PrepHub.Domain.Sorteio;

namespace PrepHub.Application.Sessoes;

public sealed class SessaoDeProvaService : ISessaoDeProvaService
{
    private readonly IExameRepository _examRepository;
    private readonly ITentativaDeProvaRepository _attemptRepository;
    private readonly ISorteadorDeQuestoes _sorteador;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly IUsuarioAtual _usuarioAtual;
    private readonly IMetricasDeNegocio _metricas;

    public SessaoDeProvaService(
        IExameRepository examRepository,
        ITentativaDeProvaRepository attemptRepository,
        ISorteadorDeQuestoes sorteador,
        IUnitOfWork unitOfWork,
        IClock clock,
        IUsuarioAtual usuarioAtual,
        IMetricasDeNegocio metricas)
    {
        _examRepository = examRepository;
        _attemptRepository = attemptRepository;
        _sorteador = sorteador;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _usuarioAtual = usuarioAtual;
        _metricas = metricas;
    }

    public async Task<Guid> IniciarTentativaAsync(Guid examId, CancellationToken cancellationToken = default)
    {
        var userId = _usuarioAtual.Id
                     ?? throw new InvalidOperationException("É preciso estar autenticado para iniciar uma tentativa.");

        var exam = await _examRepository.ObterPorIdAsync(examId, cancellationToken)
                   ?? throw new InvalidOperationException($"Exame {examId} não encontrado.");

        // Some do catálogo NÃO é o mesmo que estar indisponível: o id do exame trafega no
        // formulário de "Iniciar simulado", então esconder o botão não impede quem já o tem.
        // Sem esta guarda, um exame em construção entregaria uma prova de meia dúzia de itens.
        if (!exam.IsPublished)
        {
            throw new InvalidOperationException(
                $"O exame {exam.Code} ainda está em construção e não aceita tentativas.");
        }

        // A composição da prova é decidida agora e gravada com a tentativa: é o que garante que
        // reabrir o simulado mostre exatamente os mesmos itens, e o que permite ao próximo
        // sorteio saber o que esta pessoa já viu.
        var questionIds = await _sorteador.SortearAsync(exam.Id, userId, cancellationToken);

        var attempt = new TentativaDeProva(exam.Id, userId, _clock.UtcNow);
        attempt.DefinirQuestoes(questionIds);

        await _attemptRepository.AdicionarAsync(attempt, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _metricas.ProvaIniciada(exam.Code);

        return attempt.Id;
    }

    /// <summary>
    /// Carrega a tentativa só se ela pertencer a quem está logado. Devolver <c>null</c> para
    /// tentativa de outro dono faz o Web responder 404 — não confirma que o id existe.
    /// </summary>
    private async Task<TentativaDeProva?> ObterTentativaDoUsuarioAsync(Guid attemptId, CancellationToken cancellationToken)
    {
        var attempt = await _attemptRepository.ObterPorIdAsync(attemptId, cancellationToken);
        if (attempt is null || attempt.UserId != _usuarioAtual.Id)
        {
            return null;
        }

        return attempt;
    }

    public async Task<EstadoDaTentativaDto?> ObterEstadoAsync(Guid attemptId, CancellationToken cancellationToken = default)
    {
        var contexto = await CarregarContextoAsync(attemptId, cancellationToken);
        if (contexto is null)
        {
            return null;
        }

        var (attempt, exam, questions) = contexto.Value;
        var answersByQuestion = attempt.Answers.ToDictionary(a => a.QuestionId);

        var statuses = new List<StatusDaQuestaoDto>(questions.Count);
        for (var i = 0; i < questions.Count; i++)
        {
            var question = questions[i];
            answersByQuestion.TryGetValue(question.Id, out var answer);
            statuses.Add(new StatusDaQuestaoDto(
                question.Id,
                i + 1,
                answer?.IsAnswered ?? false,
                answer?.IsFlaggedForReview ?? false,
                answer?.SelectedOptionIds.Count ?? 0,
                SelecoesExigidas(question)));
        }

        return new EstadoDaTentativaDto(
            attempt.Id,
            exam.Code,
            exam.Name,
            exam.TimeLimitMinutes,
            attempt.StartedAt,
            CalcularSegundosRestantes(exam, attempt),
            attempt.IsFinished,
            statuses,
            exam.Vendor);
    }

    public async Task<QuestaoDto?> ObterQuestaoAsync(Guid attemptId, int number, CancellationToken cancellationToken = default)
    {
        var contexto = await CarregarContextoAsync(attemptId, cancellationToken);
        if (contexto is null)
        {
            return null;
        }

        var (attempt, exam, questions) = contexto.Value;
        if (number < 1 || number > questions.Count)
        {
            return null;
        }

        var question = questions[number - 1];
        var answer = attempt.Answers.FirstOrDefault(a => a.QuestionId == question.Id);

        // A ordem das alternativas é a da TENTATIVA, não a do banco de questões — ver OrdemDasOpcoes.
        var options = OrdemDasOpcoes.Para(question, attempt.Id)
            .Select((o, i) => new OpcaoDeQuestaoDto(o.Id, o.Text, i, o.TargetText))
            .ToList();

        return new QuestaoDto(
            question.Id,
            number,
            question.Text,
            question.Type,
            options,
            answer?.SelectedOptionIds.ToList() ?? new List<Guid>(),
            answer?.IsFlaggedForReview ?? false,
            questions.Count,
            SelecoesExigidas(question),
            exam.Vendor);
    }

    public async Task<ResultadoDeSalvarResposta> SalvarRespostaAsync(
        SalvarRespostaRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contexto = await CarregarContextoAsync(request.AttemptId, cancellationToken)
                       ?? throw new InvalidOperationException($"Tentativa {request.AttemptId} não encontrada.");

        var (attempt, exam, questions) = contexto;

        // Pode ter sido encerrada agora mesmo, pelo próprio CarregarContextoAsync, se o prazo
        // venceu entre a última navegação e este envio.
        if (attempt.IsFinished)
        {
            return ResultadoDeSalvarResposta.TentativaEncerrada;
        }

        // A questão tem de estar na composição SORTEADA desta tentativa, e cada alternativa tem
        // de pertencer à questão. Sem essa checagem, um POST direto grava resposta para um Guid
        // inventado: a nota não muda (a correção percorre a composição, não as respostas), mas
        // fica uma linha órfã — e QuestionId não tem foreign key nesta tabela, então o banco não
        // barra. Como cada Guid novo é uma linha nova, era o único caminho por onde um usuário
        // autenticado conseguia escrever sem limite.
        var question = questions.FirstOrDefault(q => q.Id == request.QuestionId) ?? questions[0];
        if (question is null)
        {
            return ResultadoDeSalvarResposta.RespostaInvalida;
        }

        var selecao = (request.SelectedOptionIds ?? Array.Empty<Guid>()).Distinct().ToList();
        var opcoesDaQuestao = question.Options.Select(o => o.Id).ToHashSet();
        if (selecao.Exists(id => !opcoesDaQuestao.Contains(id)))
        {
            return ResultadoDeSalvarResposta.RespostaInvalida;
        }

        // Teto no tempo informado pelo cliente: nenhum item pode ter consumido mais que a prova
        // inteira. O valor é acumulado a cada gravação, então sem teto a soma de envios forjados
        // estoura o int em silêncio (a aritmética é unchecked).
        var tempoGasto = Math.Clamp(request.TimeSpentSeconds, 0, exam.TimeLimitMinutes * 60);

        // Detecta se é a primeira resposta desta questão ANTES de mutar o agregado — respostas
        // novas precisam de Add explícito (chave gerada no domínio); atualizações o EF já rastreia.
        var isNewAnswer = attempt.Answers.All(a => a.QuestionId != request.QuestionId);

        var answer = attempt.DefinirResposta(
            request.QuestionId,
            selecao,
            request.IsFlaggedForReview,
            tempoGasto);

        if (isNewAnswer)
        {
            await _attemptRepository.AdicionarRespostaAsync(answer, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ResultadoDeSalvarResposta.Gravada;
    }

    public async Task<ResultadoDaProvaDto?> FinalizarTentativaAsync(Guid attemptId, CancellationToken cancellationToken = default)
    {
        var contexto = await CarregarContextoAsync(attemptId, cancellationToken);
        if (contexto is null)
        {
            return null;
        }

        var (attempt, exam, questions) = contexto.Value;

        if (!attempt.IsFinished)
        {
            var score = CorretorDeProva.Corrigir(exam, questions, attempt.Answers);
            attempt.Concluir(score.ScorePercent, score.Passed, _clock.UtcNow);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            RegistrarConclusao(exam, attempt, score, MotivoDeEncerramento.Manual);
        }

        return MontarResultado(exam, questions, attempt);
    }

    public async Task<ResultadoDaProvaDto?> ObterResultadoAsync(Guid attemptId, CancellationToken cancellationToken = default)
    {
        var contexto = await CarregarContextoAsync(attemptId, cancellationToken);
        if (contexto is null || !contexto.Value.Attempt.IsFinished)
        {
            return null;
        }

        var (attempt, exam, questions) = contexto.Value;
        return MontarResultado(exam, questions, attempt);
    }

    /// <summary>
    /// Tudo que qualquer operação da sessão precisa: a tentativa (já validada como do usuário),
    /// o exame com seus domínios e as questões DAQUELA prova, na ordem sorteada.
    /// </summary>
    /// <remarks>
    /// É também onde o fim do tempo é aplicado, pelo mesmo motivo da checagem de posse: nenhum
    /// chamador tem como esquecer, porque não é ele quem checa.
    /// </remarks>
    private async Task<(TentativaDeProva Attempt, Exame Exam, IReadOnlyList<Questao> Questions)?> CarregarContextoAsync(
        Guid attemptId,
        CancellationToken cancellationToken)
    {
        var attempt = await ObterTentativaDoUsuarioAsync(attemptId, cancellationToken);
        if (attempt is null)
        {
            return null;
        }

        var exam = await _examRepository.ObterComAreasAsync(attempt.ExamId, cancellationToken);
        if (exam is null)
        {
            return null;
        }

        var questions = await CarregarQuestoesAsync(attempt, cancellationToken);
        await EncerrarSePrazoEsgotadoAsync(attempt, exam, questions, cancellationToken);

        return (attempt, exam, questions);
    }

    /// <summary>
    /// Aplica "timer zerado = submissão automática" no SERVIDOR.
    /// </summary>
    /// <remarks>
    /// O timer da tela é conveniência, não controle: quem desliga o JavaScript ou edita o
    /// contador no navegador simplesmente deixa de receber o encerramento automático. Enquanto a
    /// regra existia só no cliente, o limite de tempo — que é o centro da fidelidade à prova
    /// real — era opcional para quem quisesse contorná-lo.
    /// </remarks>
    private async Task EncerrarSePrazoEsgotadoAsync(
        TentativaDeProva attempt,
        Exame exam,
        IReadOnlyList<Questao> questions,
        CancellationToken cancellationToken)
    {
        if (attempt.IsFinished || CalcularSegundosRestantes(exam, attempt) > 0)
        {
            return;
        }

        // Fecha no instante do VENCIMENTO, não em "agora": quem só reabre a página dias depois
        // não deve ver uma duração de dois dias no histórico. O tempo acabou quando acabou.
        var vencimento = attempt.StartedAt.AddMinutes(exam.TimeLimitMinutes);
        var score = CorretorDeProva.Corrigir(exam, questions, attempt.Answers);

        attempt.Concluir(score.ScorePercent, score.Passed, vencimento);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        RegistrarConclusao(exam, attempt, score, MotivoDeEncerramento.TempoEsgotado);
    }

    /// <summary>
    /// Publica o desfecho da prova nas métricas. Chamado nos DOIS caminhos de encerramento — o
    /// manual e o automático por tempo —, que é a razão de ser um método só: contar em apenas um
    /// deles daria um painel onde metade das provas some, e a metade que some é justamente a de
    /// quem não terminou a tempo.
    /// </summary>
    private void RegistrarConclusao(
        Exame exam,
        TentativaDeProva attempt,
        PlacarDaProva score,
        MotivoDeEncerramento motivo)
        => _metricas.ProvaConcluida(
            exam.Code,
            score.Passed,
            score.ScaledScore,

            // A duração vem do que ficou GRAVADO na tentativa, não de "agora": no encerramento por
            // tempo o fim é o instante do vencimento, e quem só reabriu a página dias depois não
            // deve aparecer no histograma como uma prova de dois dias.
            (attempt.FinishedAt ?? attempt.StartedAt) - attempt.StartedAt,
            motivo);

    /// <summary>
    /// As questões da tentativa, na ordem em que foram sorteadas.
    /// </summary>
    /// <remarks>
    /// O fallback é rede de segurança para tentativa sem composição gravada (anterior ao sorteio,
    /// quando a prova era literalmente "todas as questões do exame"). A migration reconstrói a
    /// composição dessas tentativas antigas justamente para que este caminho não seja usado — ele
    /// só sabe recuperar o que foi respondido, então item deixado em branco se perderia. O que ele
    /// impede é o desfecho pior: corrigir uma prova antiga de 8 itens contra um banco de centenas.
    /// </remarks>
    private async Task<IReadOnlyList<Questao>> CarregarQuestoesAsync(
        TentativaDeProva attempt,
        CancellationToken cancellationToken)
    {
        var ids = attempt.Questions.Select(q => q.QuestionId).ToList();

        if (ids.Count == 0)
        {
            var respondidas = attempt.Answers.Select(a => a.QuestionId).ToHashSet();
            var exam = await _examRepository.ObterComConteudoAsync(attempt.ExamId, cancellationToken);
            return exam is null
                ? Array.Empty<Questao>()
                : exam.Questions.Where(q => respondidas.Contains(q.Id)).OrderBy(q => q.Id).ToList();
        }

        var questions = await _examRepository.ObterQuestoesAsync(ids, cancellationToken);
        var byId = questions.ToDictionary(q => q.Id);

        return ids
            .Where(byId.ContainsKey)
            .Select(id => byId[id])
            .ToList();
    }

    // Quantas alternativas o candidato precisa marcar. Corresponde ao "Escolha duas." impresso
    // no enunciado da prova real — informa a quantidade, nunca quais são.
    private static int SelecoesExigidas(Questao question)
        => Math.Max(1, question.CorrectOptionIds.Count);

    private int CalcularSegundosRestantes(Exame exam, TentativaDeProva attempt)
    {
        if (attempt.IsFinished)
        {
            return 0;
        }

        var deadline = attempt.StartedAt.AddMinutes(exam.TimeLimitMinutes);
        var remaining = (int)Math.Floor((deadline - _clock.UtcNow).TotalSeconds);
        return Math.Max(0, remaining);
    }

    private static ResultadoDaProvaDto MontarResultado(Exame exam, IReadOnlyList<Questao> questions, TentativaDeProva attempt)
    {
        var score = CorretorDeProva.Corrigir(exam, questions, attempt.Answers);
        var answersByQuestion = attempt.Answers.ToDictionary(a => a.QuestionId);

        var skillAreas = score.SkillAreas
            .OrderByDescending(s => s.WeightPercent)
            .Select(s => new ResultadoPorAreaDto(
                s.SkillAreaName,
                s.WeightPercent,
                s.TotalQuestions,
                s.CorrectAnswers,
                s.ScorePercent))
            .ToList();

        var reviews = new List<RevisaoDeQuestaoDto>(questions.Count);
        for (var i = 0; i < questions.Count; i++)
        {
            var question = questions[i];
            answersByQuestion.TryGetValue(question.Id, out var answer);
            var selected = answer?.SelectedOptionIds.ToHashSet() ?? new HashSet<Guid>();

            // Mesma ordem que o candidato viu durante a prova: a revisão é releitura do que
            // aconteceu, e alternativa que troca de lugar depois atrapalha justamente quem está
            // tentando entender por que marcou o que marcou.
            // Arrastar e soltar gera uma alternativa por combinação de alvo com item — listar todas
            // na revisão seria despejar vinte linhas para uma questão de quatro alvos, quase todas
            // irrelevantes. Sobram o gabarito e o que a pessoa montou, que é o que se vai comparar.
            var ehDePares = question.Type is TipoDeQuestao.Associacao or TipoDeQuestao.Ordenacao;
            var relevantes = OrdemDasOpcoes.Para(question, attempt.Id)
                .Where(o => !ehDePares || o.IsCorrect || selected.Contains(o.Id));

            if (question.Type == TipoDeQuestao.Ordenacao)
            {
                // Na ordenação o alvo é a etapa ("Etapa 1", "Etapa 2"...), que tem ordem natural:
                // a revisão lê de cima para baixo na sequência, e não na ordem embaralhada dos pares.
                relevantes = relevantes
                    .GroupBy(o => o.TargetText)
                    .OrderBy(g => g.Key, StringComparer.Ordinal)
                    .SelectMany(g => g);
            }
            else if (question.Type == TipoDeQuestao.Associacao)
            {
                // Agrupar por alvo põe lado a lado "o que eu montei" e "o que era" de cada linha —
                // que é a comparação que a revisão existe para permitir. O GroupBy do LINQ preserva
                // a ordem de primeira aparição, então os alvos saem na ordem em que apareceram na
                // tela desta tentativa, não numa ordem nova.
                relevantes = relevantes.GroupBy(o => o.TargetText).SelectMany(g => g);
            }

            var options = relevantes
                .Select(o => new RevisaoDeOpcaoDto(o.Text, o.IsCorrect, selected.Contains(o.Id), o.TargetText))
                .ToList();

            reviews.Add(new RevisaoDeQuestaoDto(
                i + 1,
                question.Text,
                question.Type,
                question.RespondidaCorretamentePor(selected),
                question.Explanation,
                options));
        }

        return new ResultadoDaProvaDto(
            attempt.Id,
            exam.Code,
            exam.Name,
            attempt.ScorePercent ?? score.ScorePercent,
            attempt.Passed ?? score.Passed,
            exam.PassingScorePercent,
            score.TotalQuestions,
            score.CorrectAnswers,
            attempt.StartedAt,
            attempt.FinishedAt ?? attempt.StartedAt,
            skillAreas,
            reviews,
            score.ScaledScore,
            EscalaDeNota.NotaDeCorte,
            EscalaDeNota.NotaMinimaPara(exam.Vendor),
            exam.Vendor);
    }
}
