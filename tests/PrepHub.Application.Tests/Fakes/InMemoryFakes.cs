using PrepHub.Application.Abstractions;
using PrepHub.Application.Autenticacao;
using PrepHub.Application.Contracts;
using PrepHub.Application.Sorteios;
using PrepHub.Domain.Entidades;
using PrepHub.Domain.Enums;
using PrepHub.Domain.Sorteio;

namespace PrepHub.Application.Tests.Fakes;

/// <summary>Usuário logado controlável — trocar o Id simula outra pessoa na mesma sessão.</summary>
public sealed class FakeUsuarioAtual : IUsuarioAtual
{
    public FakeUsuarioAtual(Guid? id = null) => Id = id ?? Guid.NewGuid();
    public Guid? Id { get; set; }
}

public sealed class InMemoryUsuarioRepository : IUsuarioRepository
{
    private readonly List<Usuario> _usuarios = new();
    private readonly List<TokenDeRedefinicaoDeSenha> _tokens = new();
    private readonly List<TokenDeConfirmacaoDeEmail> _confirmacoes = new();

    public IReadOnlyList<Usuario> Todos => _usuarios;

    public IReadOnlyList<TokenDeRedefinicaoDeSenha> Tokens => _tokens;

    public IReadOnlyList<TokenDeConfirmacaoDeEmail> Confirmacoes => _confirmacoes;

    public Task<Usuario?> ObterPorProvedorAsync(
        ProvedorDeLogin provider,
        string providerKey,
        CancellationToken cancellationToken = default)
        => Task.FromResult(_usuarios.FirstOrDefault(u => u.Provider == provider && u.ProviderKey == providerKey));

    public Task<Usuario?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(_usuarios.FirstOrDefault(u => u.Id == id));

    // Em memória não existe diferença entre rastreado e não rastreado: a instância é a mesma.
    public Task<Usuario?> ObterPorIdParaAtualizacaoAsync(Guid id, CancellationToken cancellationToken = default)
        => ObterPorIdAsync(id, cancellationToken);

    public Task AdicionarAsync(Usuario usuario, CancellationToken cancellationToken = default)
    {
        _usuarios.Add(usuario);
        return Task.CompletedTask;
    }

    public Task AdicionarTokenDeRedefinicaoAsync(
        TokenDeRedefinicaoDeSenha token,
        CancellationToken cancellationToken = default)
    {
        _tokens.Add(token);
        return Task.CompletedTask;
    }

    public Task<TokenDeRedefinicaoDeSenha?> ObterTokenDeRedefinicaoAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
        => Task.FromResult(_tokens.FirstOrDefault(t => t.TokenHash == tokenHash));

    public Task<IReadOnlyList<TokenDeRedefinicaoDeSenha>> ObterTokensAtivosDoUsuarioAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<TokenDeRedefinicaoDeSenha>>(
            _tokens.Where(t => t.UserId == userId && t.UsedAt is null).ToList());

    public Task AdicionarTokenDeConfirmacaoAsync(
        TokenDeConfirmacaoDeEmail token,
        CancellationToken cancellationToken = default)
    {
        _confirmacoes.Add(token);
        return Task.CompletedTask;
    }

    public Task<TokenDeConfirmacaoDeEmail?> ObterTokenDeConfirmacaoAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
        => Task.FromResult(_confirmacoes.FirstOrDefault(t => t.TokenHash == tokenHash));

    public Task<IReadOnlyList<TokenDeConfirmacaoDeEmail>> ObterTokensDeConfirmacaoAtivosDoUsuarioAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<TokenDeConfirmacaoDeEmail>>(
            _confirmacoes.Where(t => t.UserId == userId && t.UsedAt is null).ToList());
}

/// <summary>
/// Gerador previsível: token sequencial e hash com prefixo. Determinístico para o teste poder
/// afirmar qual token foi emitido — a aleatoriedade de verdade é do
/// <c>GeradorDeTokenSeguro</c>, coberto na Infrastructure.
/// </summary>
public sealed class FakeGeradorDeTokenSeguro : IGeradorDeTokenSeguro
{
    private int _contador;

    public string UltimoGerado { get; private set; } = string.Empty;

    public string Gerar() => UltimoGerado = $"token-{++_contador}";

    public string Hash(string token) => $"hash:{token}";
}

/// <summary>
/// Hasher determinístico e barato. O PBKDF2 real custa centenas de milissegundos por chamada
/// de propósito — usá-lo aqui tornaria a suíte lenta sem testar nada da regra de cadastro.
/// A derivação de verdade é coberta em <c>HasherDeSenhaPbkdf2Tests</c>.
/// </summary>
public sealed class FakeHasherDeSenha : IHasherDeSenha
{
    public int ChamadasDeVerificacao { get; private set; }

    public string Hash(string senha) => $"fake:{senha}";

    public bool Verificar(string senha, string hash)
    {
        ChamadasDeVerificacao++;
        return hash == $"fake:{senha}";
    }
}

/// <summary>
/// Guarda tudo que foi medido, para o teste afirmar sobre a instrumentação como afirma sobre
/// qualquer outro efeito do caso de uso.
/// </summary>
/// <remarks>
/// Vale a pena testar métrica porque ela falha calada: um contador que deixa de ser chamado não
/// quebra nada, não lança nada e não aparece em nenhum log — só produz um painel plano, que é
/// exatamente o que se espera ver quando não está acontecendo nada.
/// </remarks>
public sealed class FakeMetricasDeNegocio : IMetricasDeNegocio
{
    public List<ProvedorDeLogin> ContasCriadas { get; } = new();

    public List<MotivoDeRecusaDeCadastro> CadastrosRecusados { get; } = new();

    public List<(ProvedorDeLogin Provedor, ResultadoDeLogin Resultado)> Logins { get; } = new();

    public List<EtapaDeRedefinicao> Redefinicoes { get; } = new();

    public List<EtapaDeConfirmacaoDeEmail> Confirmacoes { get; } = new();

    public List<string> ProvasIniciadas { get; } = new();

    public List<(string Exame, bool Aprovado, int Nota, TimeSpan Duracao, MotivoDeEncerramento Motivo)> ProvasConcluidas { get; } = new();

    public void ContaCriada(ProvedorDeLogin provedor) => ContasCriadas.Add(provedor);

    public void CadastroRecusado(MotivoDeRecusaDeCadastro motivo) => CadastrosRecusados.Add(motivo);

    public void LoginRegistrado(ProvedorDeLogin provedor, ResultadoDeLogin resultado)
        => Logins.Add((provedor, resultado));

    public void RedefinicaoDeSenha(EtapaDeRedefinicao etapa) => Redefinicoes.Add(etapa);

    public void ConfirmacaoDeEmail(EtapaDeConfirmacaoDeEmail etapa) => Confirmacoes.Add(etapa);

    public void ProvaIniciada(string codigoDoExame) => ProvasIniciadas.Add(codigoDoExame);

    public void ProvaConcluida(
        string codigoDoExame,
        bool aprovado,
        int notaEscalada,
        TimeSpan duracao,
        MotivoDeEncerramento motivo)
        => ProvasConcluidas.Add((codigoDoExame, aprovado, notaEscalada, duracao, motivo));

    /// <summary>
    /// Esquece o que foi medido até aqui. Serve para separar a PREPARAÇÃO do cenário do que o
    /// teste realmente quer afirmar — criar a conta antes de testar o login também mede coisas.
    /// </summary>
    public void Limpar()
    {
        ContasCriadas.Clear();
        CadastrosRecusados.Clear();
        Logins.Clear();
        Redefinicoes.Clear();
        Confirmacoes.Clear();
        ProvasIniciadas.Clear();
        ProvasConcluidas.Clear();
    }
}

/// <summary>
/// Um <see cref="AutenticacaoService"/> montado com todos os dublês, com as peças que os testes
/// precisam inspecionar à mão.
/// </summary>
public sealed record AutenticacaoServiceHarness(
    AutenticacaoService Service,
    InMemoryUsuarioRepository Repo,
    FixedClock Clock,
    FakeHasherDeSenha Hasher,
    FakeGeradorDeTokenSeguro Tokens,
    FakeMetricasDeNegocio Metricas)
{
    public static AutenticacaoServiceHarness Novo(DateTime? agora = null)
    {
        var repo = new InMemoryUsuarioRepository();
        var clock = new FixedClock(agora ?? new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc));
        var hasher = new FakeHasherDeSenha();
        var tokens = new FakeGeradorDeTokenSeguro();
        var metricas = new FakeMetricasDeNegocio();

        return new AutenticacaoServiceHarness(
            new AutenticacaoService(repo, new FakeUnitOfWork(), clock, hasher, tokens, metricas),
            repo,
            clock,
            hasher,
            tokens,
            metricas);
    }
}

/// <summary>Relógio controlável para testar tempo restante e finalização.</summary>
public sealed class FixedClock : IClock
{
    public FixedClock(DateTime utcNow) => UtcNow = utcNow;
    public DateTime UtcNow { get; set; }
    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}

/// <summary>Unit of work no-op: as mutações já ocorrem nos objetos em memória.</summary>
public sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.FromResult(0);
    }
}

public sealed class InMemoryExamRepository : IExameRepository
{
    private readonly Dictionary<Guid, Exame> _exams;

    public InMemoryExamRepository(params Exame[] exams)
        => _exams = exams.ToDictionary(e => e.Id);

    public Task<IReadOnlyList<Exame>> ObterTodosAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Exame>>(_exams.Values.ToList());

    public Task<Exame?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(_exams.GetValueOrDefault(id));

    // Em memória o objeto já traz skill areas, questões e opções carregadas.
    public Task<Exame?> ObterComConteudoAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(_exams.GetValueOrDefault(id));

    public Task<Exame?> ObterComAreasAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(_exams.GetValueOrDefault(id));

    public Task<IReadOnlyList<QuestaoSorteavel>> ObterPoolAsync(
        Guid examId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<QuestaoSorteavel>>(
            _exams.TryGetValue(examId, out var exam)
                ? exam.Questions.Select(q => new QuestaoSorteavel(q.Id, q.SkillAreaId)).ToList()
                : new List<QuestaoSorteavel>());

    public Task<IReadOnlyList<Questao>> ObterQuestoesAsync(
        IReadOnlyCollection<Guid> questionIds,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Questao>>(_exams.Values
            .SelectMany(e => e.Questions)
            .Where(q => questionIds.Contains(q.Id))
            .ToList());

    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> ObterOpcoesCorretasAsync(
        IReadOnlyCollection<Guid> questionIds,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>>(_exams.Values
            .SelectMany(e => e.Questions)
            .Where(q => questionIds.Contains(q.Id))
            .ToDictionary(q => q.Id, q => (IReadOnlyList<Guid>)q.CorrectOptionIds.ToList()));
}

/// <summary>
/// Sorteio previsível: entrega as questões do exame na ordem em que foram cadastradas, até o
/// limite de <c>TotalQuestions</c>. Os testes de sessão querem verificar navegação, correção e
/// posse — não o sorteio, que tem suíte própria em <c>SorteioDeQuestoesTests</c>.
/// </summary>
public sealed class FakeSorteadorDeQuestoes : ISorteadorDeQuestoes
{
    private readonly IExameRepository _exames;

    public FakeSorteadorDeQuestoes(IExameRepository exames) => _exames = exames;

    public async Task<IReadOnlyList<Guid>> SortearAsync(
        Guid examId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var exam = await _exames.ObterComConteudoAsync(examId, cancellationToken);
        return exam is null
            ? Array.Empty<Guid>()
            : exam.Questions.Take(exam.TotalQuestions).Select(q => q.Id).ToList();
    }
}

/// <summary>Aleatoriedade com semente fixa — mesma prova a cada execução do teste.</summary>
public sealed class FakeGeradorDeAleatoriedade : IGeradorDeAleatoriedade
{
    private readonly int _semente;

    public FakeGeradorDeAleatoriedade(int semente = 12345) => _semente = semente;

    public Random Criar() => new(_semente);
}

public sealed class InMemoryExamAttemptRepository : ITentativaDeProvaRepository
{
    private readonly Dictionary<Guid, TentativaDeProva> _attempts = new();

    public Task AdicionarAsync(TentativaDeProva attempt, CancellationToken cancellationToken = default)
    {
        _attempts[attempt.Id] = attempt;
        return Task.CompletedTask;
    }

    // Em memória a resposta já está no agregado; nada a fazer.
    public Task AdicionarRespostaAsync(RespostaDaTentativa answer, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<TentativaDeProva?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(_attempts.GetValueOrDefault(id));

    // Mesma ordem do repositório real (mais recente primeiro) — o serviço de histórico depende
    // dela para saber qual é "a nota atual" e qual é "a anterior".
    public Task<IReadOnlyList<TentativaDeProva>> ObterDoUsuarioAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<TentativaDeProva>>(_attempts.Values
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.StartedAt)
            .ToList());

    // Mesma semântica do repositório real: conta o que foi APRESENTADO na tentativa, respondido
    // ou não, e a distância é a posição da tentativa na ordem decrescente por data.
    public Task<IReadOnlyList<QuestaoVistaDto>> ObterQuestoesVistasAsync(
        Guid userId,
        Guid examId,
        int maxTentativas,
        CancellationToken cancellationToken = default)
    {
        var recentes = _attempts.Values
            .Where(a => a.UserId == userId && a.ExamId == examId)
            .OrderByDescending(a => a.StartedAt)
            .Take(maxTentativas)
            .ToList();

        var vistas = new List<QuestaoVistaDto>();
        for (var i = 0; i < recentes.Count; i++)
        {
            var attempt = recentes[i];
            var respostas = attempt.Answers.ToDictionary(r => r.QuestionId);

            var questoes = attempt.Questions.Count > 0
                ? attempt.Questions.Select(q => q.QuestionId)
                : attempt.Answers.Select(r => r.QuestionId);

            vistas.AddRange(questoes.Select(questaoId => new QuestaoVistaDto(
                questaoId,
                i + 1,
                respostas.TryGetValue(questaoId, out var resposta)
                    ? resposta.SelectedOptionIds
                    : Array.Empty<Guid>(),
                attempt.IsFinished)));
        }

        return Task.FromResult<IReadOnlyList<QuestaoVistaDto>>(vistas);
    }
}
