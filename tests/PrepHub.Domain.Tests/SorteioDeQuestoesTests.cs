using PrepHub.Domain.Sorteio;
using Xunit;

namespace PrepHub.Domain.Tests;

/// <summary>
/// O sorteio é a peça mais densa do domínio — repartição por maior resto, redistribuição de
/// excedente, quatro passes de prioridade e dispersão por tópico — e é puro, com o
/// <see cref="Random"/> injetado justamente para ser verificável aqui. Cada teste abaixo fixa uma
/// das promessas que a documentação do algoritmo faz.
/// </summary>
public class SorteioDeQuestoesTests
{
    // Pesos reais do Skills Measured do AZ-900. Somam 97,5% (são os pontos médios das faixas
    // publicadas), e é de propósito: o algoritmo tem de normalizar em vez de assumir 100.
    private static readonly Guid AreaConceitos = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AreaArquitetura = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AreaGovernanca = new("33333333-3333-3333-3333-333333333333");

    private static readonly AreaSorteavel[] AreasAz900 =
    {
        new(AreaConceitos, 27.5m),
        new(AreaArquitetura, 37.5m),
        new(AreaGovernanca, 32.5m)
    };

    private static PoliticaDeSorteio Politica => PoliticaDeSorteio.Padrao;

    // ---------------------------------------------------------------- fidelidade ao blueprint

    [Fact]
    public void Sortear_DistribuiAsCotasPelosPesosDoBlueprint_NaoPeloTamanhoDoPool()
    {
        var pool = new ConstrutorDePool();
        pool.Adicionar(AreaConceitos, 50);
        pool.Adicionar(AreaArquitetura, 100);   // o dobro de conteúdo escrito...
        pool.Adicionar(AreaGovernanca, 65);

        var prova = Sortear(pool, AreasAz900, total: 40);

        // ...e ainda assim a prova segue o peso: 27,5/37,5/32,5 normalizados sobre 97,5.
        Assert.Equal(40, prova.Count);
        Assert.Equal(11, pool.ContarDaArea(prova, AreaConceitos));
        Assert.Equal(16, pool.ContarDaArea(prova, AreaArquitetura));
        Assert.Equal(13, pool.ContarDaArea(prova, AreaGovernanca));
    }

    [Fact]
    public void Sortear_NaoRepeteQuestaoDentroDaMesmaProva()
    {
        var pool = PoolPadrao();

        var prova = Sortear(pool, AreasAz900, total: 40);

        Assert.Equal(prova.Count, prova.Distinct().Count());
    }

    [Fact]
    public void Sortear_MisturaOsDominiosNaOrdemFinal()
    {
        var pool = PoolPadrao();

        var prova = Sortear(pool, AreasAz900, total: 40);

        // A prova real não agrupa itens por assunto. Se as áreas saíssem em blocos haveria 2
        // trocas de domínio ao longo dos 40 itens; embaralhado, são dezenas.
        var trocas = prova.Zip(prova.Skip(1)).Count(par => pool.AreaDe(par.First) != pool.AreaDe(par.Second));
        Assert.True(trocas > 10, $"apenas {trocas} trocas de domínio — a prova saiu agrupada por área");
    }

    [Fact]
    public void Sortear_SemPesoDeclarado_DivideIgualmenteEntreOsDominios()
    {
        var pool = new ConstrutorDePool();
        pool.Adicionar(AreaConceitos, 30);
        pool.Adicionar(AreaArquitetura, 30);
        pool.Adicionar(AreaGovernanca, 30);

        var semPeso = AreasAz900.Select(a => a with { WeightPercent = 0m }).ToArray();
        var prova = Sortear(pool, semPeso, total: 30);

        Assert.Equal(10, pool.ContarDaArea(prova, AreaConceitos));
        Assert.Equal(10, pool.ContarDaArea(prova, AreaArquitetura));
        Assert.Equal(10, pool.ContarDaArea(prova, AreaGovernanca));
    }

    [Fact]
    public void Sortear_AreaSemQuestoesNoPool_NaoConsomeCota()
    {
        var pool = new ConstrutorDePool();
        pool.Adicionar(AreaConceitos, 60);
        pool.Adicionar(AreaArquitetura, 60);
        // Governança existe no exame mas ainda não tem questão escrita.

        var prova = Sortear(pool, AreasAz900, total: 40);

        // A cota dela não pode virar buraco na prova: os 40 itens saem mesmo assim.
        Assert.Equal(40, prova.Count);
        Assert.Equal(0, pool.ContarDaArea(prova, AreaGovernanca));
    }

    [Fact]
    public void Sortear_DominioSemQuestoesSuficientes_DevolveOExcedenteAosDemais()
    {
        var pool = new ConstrutorDePool();
        pool.Adicionar(AreaConceitos, 3);        // cota seria 11, só tem 3
        pool.Adicionar(AreaArquitetura, 100);
        pool.Adicionar(AreaGovernanca, 65);

        var prova = Sortear(pool, AreasAz900, total: 40);

        Assert.Equal(40, prova.Count);
        Assert.Equal(3, pool.ContarDaArea(prova, AreaConceitos));
    }

    [Fact]
    public void Sortear_PoolMenorQueOTotal_DevolveOPoolInteiroEmVezDeRepetir()
    {
        var pool = new ConstrutorDePool();
        pool.Adicionar(AreaConceitos, 10);
        pool.Adicionar(AreaArquitetura, 8);

        var prova = Sortear(pool, AreasAz900, total: 40);

        Assert.Equal(18, prova.Count);
        Assert.Equal(18, prova.Distinct().Count());
    }

    [Fact]
    public void Sortear_PoolVazio_DevolveNada()
    {
        var prova = SorteioDeQuestoes.Sortear(
            Array.Empty<QuestaoSorteavel>(), AreasAz900, 40,
            Array.Empty<HistoricoDeQuestao>(), Politica, new Random(1));

        Assert.Empty(prova);
    }

    // ------------------------------------------------------------------------ questões órfãs

    [Fact]
    public void Sortear_QuestaoDeAreaDesconhecida_ContinuaSendoSorteada()
    {
        var areaOrfa = new Guid("99999999-9999-9999-9999-999999999999");
        var pool = PoolPadrao();
        var orfas = pool.Adicionar(areaOrfa, 10);

        var prova = Sortear(pool, AreasAz900, total: 40);

        // Regressão: com peso zero (o caminho ingênuo) a parte exata E o resto da área órfã dão
        // zero, então ela perdia até o desempate do maior resto e NUNCA era sorteada. A cota agora
        // acompanha a fatia do pool — 10 de 225 questões em 40 itens ≈ 1,8.
        var sorteadas = prova.Count(orfas.Contains);
        Assert.InRange(sorteadas, 1, 3);
        Assert.Equal(40, prova.Count);
    }

    [Fact]
    public void Sortear_PoolInteiramenteOrfao_AindaMontaAProva()
    {
        var areaOrfa = new Guid("99999999-9999-9999-9999-999999999999");
        var pool = new ConstrutorDePool();
        pool.Adicionar(areaOrfa, 50);

        var prova = Sortear(pool, AreasAz900, total: 40);

        Assert.Equal(40, prova.Count);
    }

    // ------------------------------------------------------------ variedade e reforço

    [Fact]
    public void Sortear_ReservaExatamenteOTetoGlobalParaOQueOUsuarioErrou()
    {
        var pool = PoolPadrao();
        var erradas = pool.PrimeirasDeCadaArea(10);

        var prova = Sortear(pool, AreasAz900, total: 40, historico: Historico(erradas, 1, acertou: false));

        // Regressão: o teto era aplicado como fração DENTRO de cada domínio, e o arredondamento
        // para baixo (1+2+1) comia um terço do orçamento. Sobre a prova inteira são 15% de 40 = 6.
        Assert.Equal(6, prova.Count(erradas.Contains));
    }

    [Fact]
    public void Sortear_QuestoesAcertadasNaoVoltamEnquantoHouverIneditas()
    {
        var pool = PoolPadrao();
        var acertadas = pool.PrimeirasDeCadaArea(10);

        var prova = Sortear(pool, AreasAz900, total: 40, historico: Historico(acertadas, 1, acertou: true));

        Assert.DoesNotContain(prova, acertadas.Contains);
    }

    [Fact]
    public void Sortear_ItemDeTentativaAbandonada_NaoEntraNaFilaDeReforco()
    {
        var pool = PoolPadrao();
        var apresentadasSemResposta = pool.PrimeirasDeCadaArea(10);

        var prova = Sortear(pool, AreasAz900, total: 40,
            historico: Historico(apresentadasSemResposta, 1, acertou: null));

        // Abandonar a prova no item 1 marcaria 40 questões nunca lidas. Contá-las como erro
        // afogaria os erros de verdade — elas contam só como recentes.
        Assert.DoesNotContain(prova, apresentadasSemResposta.Contains);
    }

    [Fact]
    public void Sortear_AbandonoRecenteNaoApagaErroAnterior()
    {
        var pool = new ConstrutorDePool();
        var questoes = pool.Adicionar(AreaConceitos, 60);
        var errada = questoes[0];

        var historico = new[]
        {
            new HistoricoDeQuestao(errada, 2, false),   // errou de verdade
            new HistoricoDeQuestao(errada, 1, null)     // depois abriu e abandonou a prova
        };

        var prova = Sortear(pool, UmaArea(AreaConceitos), total: 20, historico: historico);

        // A consolidação prefere a ocorrência mais recente COM resultado: o erro continua valendo.
        Assert.Contains(errada, prova);
    }

    [Fact]
    public void Sortear_SemNenhumResultadoConhecido_NaoVaiParaOReforco()
    {
        var pool = new ConstrutorDePool();
        var questoes = pool.Adicionar(AreaConceitos, 60);
        var soAbandonada = questoes[0];

        var prova = Sortear(pool, UmaArea(AreaConceitos), total: 20,
            historico: new[] { new HistoricoDeQuestao(soAbandonada, 1, null) });

        Assert.DoesNotContain(soAbandonada, prova);
    }

    [Fact]
    public void Sortear_ForaDaJanelaDeMemoria_VoltaAContarComoInedita()
    {
        var pool = new ConstrutorDePool();
        var antigas = pool.Adicionar(AreaConceitos, 20);   // acertadas há 4 tentativas
        var recentesErradas = pool.Adicionar(AreaConceitos, 40);

        var historico = Historico(antigas, Politica.TentativasDeMemoria + 1, acertou: true)
            .Concat(Historico(recentesErradas, 1, acertou: false))
            .ToList();

        var prova = Sortear(pool, UmaArea(AreaConceitos), total: 20, historico: historico);

        // As 20 antigas saíram da janela e voltaram a ser inéditas, então preenchem o corpo da
        // prova; só as 3 vagas de reforço (15% de 20) vão para as erradas recentes.
        Assert.Equal(17, prova.Count(antigas.Contains));
        Assert.Equal(3, prova.Count(recentesErradas.Contains));
    }

    [Fact]
    public void Sortear_BancoTodoJaVisto_EntregaAProvaCompletaEmVezDeEncolher()
    {
        var pool = new ConstrutorDePool();
        var questoes = pool.Adicionar(AreaConceitos, 30);

        var prova = Sortear(pool, UmaArea(AreaConceitos), total: 20,
            historico: Historico(questoes, 1, acertou: false));

        // Passes 3 e 4 estouram o teto de propósito: prova curta quebraria a fidelidade.
        Assert.Equal(20, prova.Count);
        Assert.Equal(20, prova.Distinct().Count());
    }

    // ------------------------------------------------------------------ dispersão por tópico

    [Fact]
    public void Sortear_EspalhaACotaDoDominioEntreOsTopicos()
    {
        var pool = new ConstrutorDePool();
        var topicos = new[] { "CapEx e OpEx", "Escalabilidade", "Alta disponibilidade", "IaaS/PaaS/SaaS", "Nuvem híbrida" };
        foreach (var topico in topicos)
        {
            pool.Adicionar(AreaConceitos, 10, topico);
        }

        var prova = Sortear(pool, UmaArea(AreaConceitos), total: 10);

        // 10 itens, 5 tópicos, um de cada por rodada: exatamente 2 de cada assunto. Sem dispersão,
        // um sorteio uniforme poderia entregar 5 questões de CapEx/OpEx na mesma prova.
        foreach (var topico in topicos)
        {
            Assert.Equal(2, prova.Count(id => pool.TopicoDe(id) == topico));
        }
    }

    [Fact]
    public void Sortear_QuestoesSemTopico_ContinuamSendoSorteadas()
    {
        var pool = new ConstrutorDePool();
        pool.Adicionar(AreaConceitos, 20, "Escalabilidade");
        var semTopico = pool.Adicionar(AreaConceitos, 20);

        var prova = Sortear(pool, UmaArea(AreaConceitos), total: 20);

        Assert.Contains(prova, semTopico.Contains);
    }

    // ------------------------------------------------------------------------ determinismo

    [Fact]
    public void Sortear_MesmaSemente_ProduzExatamenteAMesmaProva()
    {
        var pool = PoolPadrao();

        var primeira = Sortear(pool, AreasAz900, total: 40, semente: 7);
        var segunda = Sortear(pool, AreasAz900, total: 40, semente: 7);

        Assert.Equal(primeira, segunda);
    }

    [Fact]
    public void Sortear_SementesDiferentes_ProduzemProvasDiferentes()
    {
        var pool = PoolPadrao();

        var primeira = Sortear(pool, AreasAz900, total: 40, semente: 7);
        var segunda = Sortear(pool, AreasAz900, total: 40, semente: 8);

        Assert.NotEqual(primeira, segunda);
    }

    // ------------------------------------------------------------------------- contrato

    [Fact]
    public void Sortear_TotalNaoPositivo_Falha()
    {
        var pool = PoolPadrao();

        Assert.Throws<ArgumentOutOfRangeException>(() => Sortear(pool, AreasAz900, total: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Sortear(pool, AreasAz900, total: -1));
    }

    [Fact]
    public void Sortear_ArgumentosNulos_Falham()
    {
        var pool = PoolPadrao();

        Assert.Throws<ArgumentNullException>(() => SorteioDeQuestoes.Sortear(
            null!, AreasAz900, 40, Array.Empty<HistoricoDeQuestao>(), Politica, new Random(1)));
        Assert.Throws<ArgumentNullException>(() => SorteioDeQuestoes.Sortear(
            pool.Questoes, null!, 40, Array.Empty<HistoricoDeQuestao>(), Politica, new Random(1)));
        Assert.Throws<ArgumentNullException>(() => SorteioDeQuestoes.Sortear(
            pool.Questoes, AreasAz900, 40, null!, Politica, new Random(1)));
        Assert.Throws<ArgumentNullException>(() => SorteioDeQuestoes.Sortear(
            pool.Questoes, AreasAz900, 40, Array.Empty<HistoricoDeQuestao>(), null!, new Random(1)));
        Assert.Throws<ArgumentNullException>(() => SorteioDeQuestoes.Sortear(
            pool.Questoes, AreasAz900, 40, Array.Empty<HistoricoDeQuestao>(), Politica, null!));
    }

    // --------------------------------------------------------------------------- auxiliares

    private static ConstrutorDePool PoolPadrao()
    {
        var pool = new ConstrutorDePool();
        pool.Adicionar(AreaConceitos, 50);
        pool.Adicionar(AreaArquitetura, 100);
        pool.Adicionar(AreaGovernanca, 65);
        return pool;
    }

    private static AreaSorteavel[] UmaArea(Guid areaId) => new[] { new AreaSorteavel(areaId, 100m) };

    private static IReadOnlyList<Guid> Sortear(
        ConstrutorDePool pool,
        IReadOnlyCollection<AreaSorteavel> areas,
        int total,
        IReadOnlyCollection<HistoricoDeQuestao>? historico = null,
        int semente = 20260727)
        => SorteioDeQuestoes.Sortear(
            pool.Questoes,
            areas,
            total,
            historico ?? Array.Empty<HistoricoDeQuestao>(),
            Politica,
            new Random(semente));

    private static List<HistoricoDeQuestao> Historico(IEnumerable<Guid> ids, int tentativasAtras, bool? acertou)
        => ids.Select(id => new HistoricoDeQuestao(id, tentativasAtras, acertou)).ToList();

    /// <summary>Monta pools com ids previsíveis e sabe responder de que área/tópico cada um veio.</summary>
    private sealed class ConstrutorDePool
    {
        private readonly List<QuestaoSorteavel> _questoes = new();
        private int _proximo;

        public IReadOnlyCollection<QuestaoSorteavel> Questoes => _questoes;

        public List<Guid> Adicionar(Guid areaId, int quantidade, string? topico = null)
        {
            var novos = new List<Guid>(quantidade);
            for (var i = 0; i < quantidade; i++)
            {
                var id = Guid.Parse($"{++_proximo:D8}-0000-0000-0000-000000000000");
                _questoes.Add(new QuestaoSorteavel(id, areaId, topico));
                novos.Add(id);
            }

            return novos;
        }

        /// <summary>As N primeiras questões de cada área — base estável para montar histórico.</summary>
        public List<Guid> PrimeirasDeCadaArea(int quantidade)
            => _questoes
                .GroupBy(q => q.AreaId)
                .SelectMany(g => g.Take(quantidade))
                .Select(q => q.QuestaoId)
                .ToList();

        public Guid AreaDe(Guid questaoId) => _questoes.First(q => q.QuestaoId == questaoId).AreaId;

        public string? TopicoDe(Guid questaoId) => _questoes.First(q => q.QuestaoId == questaoId).Topico;

        public int ContarDaArea(IEnumerable<Guid> ids, Guid areaId) => ids.Count(id => AreaDe(id) == areaId);
    }
}
