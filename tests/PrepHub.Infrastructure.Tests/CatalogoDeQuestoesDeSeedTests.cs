using PrepHub.Domain.Enums;
using PrepHub.Infrastructure.Persistence;
using PrepHub.Infrastructure.Persistence.Seed;
using Xunit;

namespace PrepHub.Infrastructure.Tests;

/// <summary>
/// A validação do catálogo é a última barreira antes de um gabarito errado chegar ao usuário — e
/// só era exercitada de lado, pelo seed que lança quando ela reclama. Isso deixava passar a
/// regressão que importa: uma <c>Validar</c> que parasse de reclamar não quebraria teste nenhum.
/// Aqui cada regra é verificada pelo que ela precisa REJEITAR.
/// </summary>
public class CatalogoDeQuestoesDeSeedTests
{
    private const string AreaConhecida = "conceitos-de-nuvem";

    private static readonly string[] Areas = { AreaConhecida };

    // O validador exige explicação longa o bastante para justificar a correta e cada distrator.
    private const string ExplicacaoValida =
        "A alternativa correta é a primeira porque descreve exatamente o comportamento do serviço no " +
        "cenário apresentado. A segunda confunde com um recurso parecido, a terceira resolve um " +
        "problema diferente e a quarta contraria a restrição enunciada.";

    // ------------------------------------------------------------------- catálogo de verdade

    [Fact]
    public void Carregar_LeTodosOsLotesEmbutidosNoAssembly()
    {
        var arquivos = CatalogoDeQuestoesDeSeed.Carregar();

        Assert.NotEmpty(arquivos);
        Assert.All(arquivos, a => Assert.False(string.IsNullOrWhiteSpace(a.ExameCode)));
        Assert.All(arquivos, a => Assert.False(string.IsNullOrWhiteSpace(a.Area)));
        Assert.All(arquivos, a => Assert.NotEmpty(a.Questoes));
    }

    /// <summary>
    /// O banco de questões que vai ao ar, validado contra as áreas que existem de verdade.
    /// </summary>
    /// <remarks>
    /// Sem este teste, a única barreira era o seed derrubando a aplicação no startup — ou seja, o
    /// erro aparecia no deploy, não no commit. Uma questão nova com gabarito faltando, id repetido
    /// ou alvo duplicado passa em tudo mais: compila, e os testes de regra abaixo usam lotes
    /// sintéticos que nunca tocam nos arquivos reais.
    /// </remarks>
    [Fact]
    public void Validar_CatalogoRealEmbutido_EstaIntegro()
    {
        // Com os fornecedores: um lote AWS com Sim/Não ou com múltipla resposta de quatro
        // alternativas só é reprovado quando o validador sabe de que exame ele é.
        var problemas = CatalogoDeQuestoesDeSeed.Validar(
            CatalogoDeQuestoesDeSeed.Carregar(),
            PrepHubDbSeeder.AreasPorExame,
            PrepHubDbSeeder.FornecedorPorExame);

        Assert.Empty(problemas);
    }

    /// <summary>
    /// Todo lote embutido tem de apontar para um exame que existe de verdade.
    /// </summary>
    /// <remarks>
    /// O seed aplica as questões filtrando por <c>exameCode</c>. Um código que não casa com exame
    /// nenhum não entra em iteração nenhuma: o lote some sem erro, sem log e sem questão no banco.
    /// Com um exame só isso era teórico; com quatro, é um dígito trocado.
    /// </remarks>
    [Fact]
    public void CatalogoRealEmbutido_SoReferenciaExamesDefinidos()
    {
        var codigosDefinidos = PrepHubDbSeeder.Exames.Select(e => e.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var orfaos = CatalogoDeQuestoesDeSeed.Carregar()
            .Where(a => !codigosDefinidos.Contains(a.ExameCode))
            .Select(a => $"{a.Origem} → '{a.ExameCode}'")
            .ToList();

        Assert.True(orfaos.Count == 0, "Lotes apontando para exame inexistente: " + string.Join(", ", orfaos));
    }

    /// <summary>
    /// Cada exame <b>publicado</b> precisa de pool que sustente o tamanho declarado da prova.
    /// </summary>
    /// <remarks>
    /// O sorteio corta o total para o tamanho do pool (<c>Math.Min</c>) sem reclamar. Um exame
    /// declarado com 50 itens e 30 questões escritas entrega uma prova de 30 — mais curta, sempre
    /// a mesma, e sem nada que ligue o sintoma à causa. Como fidelidade à prova real é o produto,
    /// isso é defeito e não obra em andamento.
    ///
    /// Exame com <c>Publicado: false</c> fica fora daqui de propósito: é exatamente o estado de
    /// "banco sendo escrito", e é o que permite semear e testar um exame desde a primeira questão.
    /// Publicá-lo antes da hora é que passa a falhar — que é o momento certo para falhar.
    /// </remarks>
    [Fact]
    public void ExamesPublicados_TemQuestoesSuficientesParaMontarUmaProva()
    {
        var questoesPorExame = CatalogoDeQuestoesDeSeed.Carregar()
            .GroupBy(a => a.ExameCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.Questoes.Count), StringComparer.OrdinalIgnoreCase);

        var magros = PrepHubDbSeeder.Exames
            .Where(e => e.Publicado)
            .Select(e => (e.Code, e.TotalQuestions, Escritas: questoesPorExame.GetValueOrDefault(e.Code)))
            .Where(e => e.Escritas < e.TotalQuestions)
            .Select(e => $"{e.Code}: {e.Escritas} escritas para prova de {e.TotalQuestions}")
            .ToList();

        Assert.True(magros.Count == 0, "Exame publicado sem pool suficiente: " + string.Join("; ", magros));
    }

    /// <summary>
    /// Exame publicado precisa ter questões em <b>todos</b> os domínios que declara.
    /// </summary>
    /// <remarks>
    /// Contar o total não basta, e este teste existe porque a contagem sozinha deixa passar o caso
    /// pior. <c>SorteioDeQuestoes.DistribuirCotas</c> só considera as áreas que têm questão no pool
    /// e redistribui a cota das demais entre elas: um exame com dois dos cinco domínios vazios
    /// monta provas completas, do tamanho certo, em que 30% do blueprint simplesmente não aparece —
    /// e nada reclama, porque a prova sai com a contagem esperada. É a falha calada mais cara que
    /// este banco pode ter, já que fidelidade ao Skills Measured é o produto.
    /// </remarks>
    [Fact]
    public void ExamesPublicados_TemQuestoesEmTodosOsDominiosDeclarados()
    {
        var porExameEArea = CatalogoDeQuestoesDeSeed.Carregar()
            .GroupBy(a => (a.ExameCode, a.Area), TuplaSemDiferenciarMaiusculas)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.Questoes.Count), TuplaSemDiferenciarMaiusculas);

        var vazios = PrepHubDbSeeder.Exames
            .Where(e => e.Publicado)
            .SelectMany(e => e.Areas.Select(a => (e.Code, a.Key)))
            .Where(par => porExameEArea.GetValueOrDefault(par) == 0)
            .Select(par => $"{par.Code}/{par.Key}")
            .ToList();

        Assert.True(vazios.Count == 0, "Domínio sem questão em exame publicado: " + string.Join(", ", vazios));
    }

    private static readonly IEqualityComparer<(string, string)> TuplaSemDiferenciarMaiusculas =
        new ComparadorDeTupla();

    private sealed class ComparadorDeTupla : IEqualityComparer<(string, string)>
    {
        public bool Equals((string, string) x, (string, string) y)
            => StringComparer.OrdinalIgnoreCase.Equals(x.Item1, y.Item1)
               && StringComparer.OrdinalIgnoreCase.Equals(x.Item2, y.Item2);

        public int GetHashCode((string, string) obj)
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item1),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item2));
    }

    // ----------------------------------------------------------------- escopo de área por exame

    [Fact]
    public void Validar_ExameCodeDesconhecido_Acusa()
    {
        var lote = Lote(QuestaoValida()) with { ExameCode = "AZ-999" };

        var problemas = CatalogoDeQuestoesDeSeed.Validar(new[] { lote }, PrepHubDbSeeder.AreasPorExame);

        Assert.Contains(problemas, p => p.Contains("não corresponde a exame nenhum"));
    }

    /// <summary>
    /// A área é escopada ao exame: um slug válido em OUTRO exame não vale aqui.
    /// </summary>
    /// <remarks>
    /// É o erro que a lista plana de slugs deixava passar. Com todas as áreas de todos os exames
    /// num balde só, um lote do AZ-104 apontando para <c>conceitos-de-nuvem</c> (área do AZ-900)
    /// passaria na validação — e as questões cairiam num domínio que não é o delas, com peso de
    /// blueprint errado no sorteio.
    /// </remarks>
    [Fact]
    public void Validar_AreaDeOutroExame_Acusa()
    {
        var areasPorExame = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["AZ-900"] = new[] { "conceitos-de-nuvem" },
            ["AZ-104"] = new[] { "redes" }
        };

        // Lote do AZ-104 usando a área do AZ-900.
        var lote = Lote(QuestaoValida()) with { ExameCode = "AZ-104", Area = "conceitos-de-nuvem" };

        var problemas = CatalogoDeQuestoesDeSeed.Validar(new[] { lote }, areasPorExame);

        Assert.Contains(problemas, p => p.Contains("não existe no exame AZ-104"));
    }

    [Fact]
    public void Validar_AreaCorretaDoExame_NaoAcusa()
    {
        var areasPorExame = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["AZ-104"] = new[] { "redes" }
        };

        var lote = Lote(QuestaoValida()) with { ExameCode = "AZ-104", Area = "redes" };

        Assert.Empty(CatalogoDeQuestoesDeSeed.Validar(new[] { lote }, areasPorExame));
    }

    [Fact]
    public void Validar_LoteBemFormado_NaoAcusaNada()
    {
        var problemas = CatalogoDeQuestoesDeSeed.Validar(new[] { Lote(QuestaoValida()) }, Areas);

        Assert.Empty(problemas);
    }

    // ------------------------------------------------------------------------- lote e chaves

    [Fact]
    public void Validar_AreaInexistenteNoExame_Acusa()
    {
        var lote = Lote(QuestaoValida()) with { Area = "dominio-que-nao-existe" };

        Assert.Contains(CatalogoDeQuestoesDeSeed.Validar(new[] { lote }, Areas), p => p.Contains("não existe no exame"));
    }

    [Fact]
    public void Validar_LoteSemQuestoes_Acusa()
    {
        var lote = Lote() with { Questoes = Array.Empty<QuestaoDeSeed>() };

        Assert.Contains(CatalogoDeQuestoesDeSeed.Validar(new[] { lote }, Areas), p => p.Contains("nenhum item"));
    }

    [Fact]
    public void Validar_MesmoIdEmDoisArquivos_Acusa()
    {
        // O Guid da questão é derivado do id: chave repetida faria dois enunciados disputarem a
        // mesma linha do banco, e o último a ser importado sobrescreveria o outro.
        var primeiro = Lote(QuestaoValida() with { Id = "az900-repetida" }) with { Origem = "lote-a.json" };
        var segundo = Lote(QuestaoValida() with { Id = "az900-repetida", Enunciado = "Outro enunciado?" })
            with { Origem = "lote-b.json" };

        Assert.Contains(
            CatalogoDeQuestoesDeSeed.Validar(new[] { primeiro, segundo }, Areas),
            p => p.Contains("id repetido"));
    }

    [Fact]
    public void Validar_EnunciadoDuplicadoEntreArquivos_Acusa()
    {
        var primeiro = Lote(QuestaoValida() with { Id = "az900-a" });
        var segundo = Lote(QuestaoValida() with { Id = "az900-b" });

        Assert.Contains(
            CatalogoDeQuestoesDeSeed.Validar(new[] { primeiro, segundo }, Areas),
            p => p.Contains("enunciado duplicado"));
    }

    // ----------------------------------------------------------------------------- conteúdo

    [Fact]
    public void Validar_EnunciadoVazio_Acusa()
    {
        var lote = Lote(QuestaoValida() with { Enunciado = "   " });

        Assert.Contains(CatalogoDeQuestoesDeSeed.Validar(new[] { lote }, Areas), p => p.Contains("enunciado vazio"));
    }

    [Fact]
    public void Validar_ExplicacaoVazia_Acusa()
    {
        var lote = Lote(QuestaoValida() with { Explicacao = "" });

        Assert.Contains(CatalogoDeQuestoesDeSeed.Validar(new[] { lote }, Areas), p => p.Contains("explicação vazia"));
    }

    [Fact]
    public void Validar_ExplicacaoQueSoJustificaOGabarito_Acusa()
    {
        // Explicação por distrator é o que faz o simulado ensinar em vez de só pontuar.
        var lote = Lote(QuestaoValida() with { Explicacao = "A primeira está correta." });

        Assert.Contains(CatalogoDeQuestoesDeSeed.Validar(new[] { lote }, Areas), p => p.Contains("curta demais"));
    }

    [Fact]
    public void Validar_TipoDesconhecido_Acusa()
    {
        var lote = Lote(QuestaoValida() with { Tipo = "Dissertativa" });

        Assert.Contains(CatalogoDeQuestoesDeSeed.Validar(new[] { lote }, Areas), p => p.Contains("desconhecido"));
    }

    // -------------------------------------------------------------------------- alternativas

    [Fact]
    public void Validar_SemGabarito_Acusa()
    {
        var lote = Lote(QuestaoValida() with
        {
            Opcoes = Opcoes(("Primeira", false), ("Segunda", false), ("Terceira", false), ("Quarta", false))
        });

        Assert.Contains(
            CatalogoDeQuestoesDeSeed.Validar(new[] { lote }, Areas),
            p => p.Contains("nenhuma alternativa marcada como correta"));
    }

    [Fact]
    public void Validar_AlternativaComTextoVazio_Acusa()
    {
        var lote = Lote(QuestaoValida() with
        {
            Opcoes = Opcoes(("Primeira", true), ("", false), ("Terceira", false), ("Quarta", false))
        });

        Assert.Contains(CatalogoDeQuestoesDeSeed.Validar(new[] { lote }, Areas), p => p.Contains("texto vazio"));
    }

    [Fact]
    public void Validar_AlternativaRepetida_Acusa()
    {
        var lote = Lote(QuestaoValida() with
        {
            Opcoes = Opcoes(("Primeira", true), ("Segunda", false), ("segunda", false), ("Quarta", false))
        });

        Assert.Contains(CatalogoDeQuestoesDeSeed.Validar(new[] { lote }, Areas), p => p.Contains("alternativa repetida"));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public void Validar_EscolhaUnicaSemQuatroAlternativas_Acusa(int quantidade)
    {
        var opcoes = Enumerable.Range(0, quantidade)
            .Select(i => new OpcaoDeSeed { Texto = $"Alternativa {i}", Correta = i == 0 })
            .ToList();

        var lote = Lote(QuestaoValida() with { Opcoes = opcoes });

        Assert.Contains(CatalogoDeQuestoesDeSeed.Validar(new[] { lote }, Areas), p => p.Contains("exige 4 alternativas"));
    }

    [Fact]
    public void Validar_EscolhaUnicaComDuasCorretas_Acusa()
    {
        var lote = Lote(QuestaoValida() with
        {
            Opcoes = Opcoes(("Primeira", true), ("Segunda", true), ("Terceira", false), ("Quarta", false))
        });

        Assert.Contains(
            CatalogoDeQuestoesDeSeed.Validar(new[] { lote }, Areas),
            p => p.Contains("exatamente 1 correta"));
    }

    [Fact]
    public void Validar_EscolhaMultiplaComUmaSoCorreta_Acusa()
    {
        var lote = Lote(QuestaoMultipla() with
        {
            Opcoes = Opcoes(("Primeira", true), ("Segunda", false), ("Terceira", false), ("Quarta", false))
        });

        Assert.Contains(CatalogoDeQuestoesDeSeed.Validar(new[] { lote }, Areas), p => p.Contains("ao menos 2 corretas"));
    }

    [Fact]
    public void Validar_EscolhaMultiplaComTodasCorretas_Acusa()
    {
        var lote = Lote(QuestaoMultipla() with
        {
            Opcoes = Opcoes(("Primeira", true), ("Segunda", true), ("Terceira", true), ("Quarta", true))
        });

        Assert.Contains(
            CatalogoDeQuestoesDeSeed.Validar(new[] { lote }, Areas),
            p => p.Contains("todas as alternativas corretas"));
    }

    [Fact]
    public void Validar_EscolhaMultiplaSemDizerQuantasMarcar_Acusa()
    {
        // A UI imprime "Escolha duas." a partir da contagem, mas quem lê o enunciado tem de
        // encontrar a instrução no texto também — é assim na prova real.
        var lote = Lote(QuestaoMultipla() with { Enunciado = "Quais servicos atendem ao requisito?" });

        Assert.Contains(
            CatalogoDeQuestoesDeSeed.Validar(new[] { lote }, Areas),
            p => p.Contains("sem instrução de quantas alternativas"));
    }

    [Fact]
    public void Validar_SimNaoSemDuasAlternativas_Acusa()
    {
        var lote = Lote(QuestaoValida() with { Tipo = "SimNao" });

        Assert.Contains(
            CatalogoDeQuestoesDeSeed.Validar(new[] { lote }, Areas),
            p => p.Contains("SimNao exige exatamente 2 alternativas"));
    }

    // ------------------------------------------------------- arrastar e soltar (Associacao)

    /// <summary>
    /// A expansão é o contrato mais frágil do formato: ela decide a POSIÇÃO de cada alternativa, e
    /// a posição decide o Id, e o Id é o que as respostas já gravadas apontam. Um refactor que
    /// reordenasse os pares reescreveria o gabarito de tentativas antigas sem quebrar nada.
    /// </summary>
    [Fact]
    public void Expandir_GeraUmParPorCombinacaoDeAlvoComItem_NaOrdemDeclarada()
    {
        var expandida = CatalogoDeQuestoesDeSeed.Expandir(QuestaoDeAssociacao());

        // 3 alvos × 3 itens no painel (2 respostas distintas + 1 extra).
        Assert.Equal(9, expandida.Opcoes.Count);

        // Alvo a alvo, e dentro do alvo na ordem das respostas seguida dos extras.
        Assert.Equal(
            new[] { "Alvo A", "Alvo A", "Alvo A", "Alvo B", "Alvo B", "Alvo B", "Alvo C", "Alvo C", "Alvo C" },
            expandida.Opcoes.Select(o => o.Alvo));

        Assert.Equal(
            new[] { "Item 1", "Item 2", "Distrator", "Item 1", "Item 2", "Distrator", "Item 1", "Item 2", "Distrator" },
            expandida.Opcoes.Select(o => o.Texto));
    }

    [Fact]
    public void Expandir_MarcaComoCorretoApenasOParDoGabarito()
    {
        var expandida = CatalogoDeQuestoesDeSeed.Expandir(QuestaoDeAssociacao());

        var corretos = expandida.Opcoes.Where(o => o.Correta).Select(o => $"{o.Alvo}={o.Texto}");

        // Um item pode responder a mais de um alvo — é o que tira a resolução por eliminação.
        Assert.Equal(new[] { "Alvo A=Item 1", "Alvo B=Item 2", "Alvo C=Item 1" }, corretos);
    }

    [Fact]
    public void Expandir_QuestaoQueNaoEAssociacao_PassaIntacta()
    {
        var original = QuestaoValida();

        Assert.Same(original, CatalogoDeQuestoesDeSeed.Expandir(original));
    }

    [Fact]
    public void Expandir_QuestaoJaExpandida_NaoExpandeDeNovo()
    {
        // Validar chama Expandir para aceitar lote cru; sem idempotência, um lote já carregado
        // (que chega expandido) teria os pares multiplicados a cada passagem.
        var expandida = CatalogoDeQuestoesDeSeed.Expandir(QuestaoDeAssociacao());

        Assert.Equal(expandida.Opcoes.Count, CatalogoDeQuestoesDeSeed.Expandir(expandida).Opcoes.Count);
    }

    [Fact]
    public void Validar_AssociacaoBemFormada_NaoAcusaNada()
    {
        Assert.Empty(CatalogoDeQuestoesDeSeed.Validar(new[] { Lote(QuestaoDeAssociacao()) }, Areas));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(7)]
    public void Validar_AssociacaoForaDaFaixaDeAlvos_Acusa(int quantidade)
    {
        var associacoes = Enumerable.Range(0, quantidade)
            .Select(i => new AssociacaoDeSeed { Alvo = $"Alvo {i}", Item = $"Item {i}" })
            .ToList();

        var lote = Lote(QuestaoDeAssociacao() with { Associacoes = associacoes });

        Assert.Contains(CatalogoDeQuestoesDeSeed.Validar(new[] { lote }, Areas), p => p.Contains("de 3 a 6 alvos"));
    }

    [Fact]
    public void Validar_AssociacaoComAlvoRepetido_Acusa()
    {
        // Dois alvos de mesmo texto seriam uma caixa só na tela, com dois gabaritos.
        var lote = Lote(QuestaoDeAssociacao() with
        {
            Associacoes = new[]
            {
                new AssociacaoDeSeed { Alvo = "Alvo A", Item = "Item 1" },
                new AssociacaoDeSeed { Alvo = "alvo a", Item = "Item 2" },
                new AssociacaoDeSeed { Alvo = "Alvo C", Item = "Item 1" }
            }
        });

        Assert.Contains(CatalogoDeQuestoesDeSeed.Validar(new[] { lote }, Areas), p => p.Contains("alvo repetido"));
    }

    [Fact]
    public void Validar_AssociacaoComUmItemPorAlvoESemDistrator_Acusa()
    {
        // Painel com exatamente um item por alvo se resolve por eliminação: quem sabe todos menos
        // um acerta o último de graça, e a questão deixa de medir o que se propôs a medir.
        var lote = Lote(QuestaoDeAssociacao() with
        {
            Associacoes = new[]
            {
                new AssociacaoDeSeed { Alvo = "Alvo A", Item = "Item 1" },
                new AssociacaoDeSeed { Alvo = "Alvo B", Item = "Item 2" },
                new AssociacaoDeSeed { Alvo = "Alvo C", Item = "Item 3" }
            },
            ItensExtras = Array.Empty<string>()
        });

        Assert.Contains(CatalogoDeQuestoesDeSeed.Validar(new[] { lote }, Areas), p => p.Contains("sem distrator"));
    }

    [Fact]
    public void Validar_ItemExtraQueTambemEResposta_Acusa()
    {
        var lote = Lote(QuestaoDeAssociacao() with { ItensExtras = new[] { "Item 2" } });

        Assert.Contains(
            CatalogoDeQuestoesDeSeed.Validar(new[] { lote }, Areas),
            p => p.Contains("também é resposta"));
    }

    [Fact]
    public void Validar_AssociacoesEmTipoQueNaoEAssociacao_Acusa()
    {
        var lote = Lote(QuestaoValida() with { Associacoes = QuestaoDeAssociacao().Associacoes });

        Assert.Contains(
            CatalogoDeQuestoesDeSeed.Validar(new[] { lote }, Areas),
            p => p.Contains("só vale para o tipo Associacao"));
    }

    [Fact]
    public void Validar_AssociacaoComItemRepetidoEntreAlvos_NaoAcusaAlternativaRepetida()
    {
        // O mesmo item aparece uma vez por alvo — a repetição É o formato. Se a regra genérica de
        // alternativa repetida valesse aqui, nenhuma questão de arrastar passaria na validação.
        var problemas = CatalogoDeQuestoesDeSeed.Validar(new[] { Lote(QuestaoDeAssociacao()) }, Areas);

        Assert.DoesNotContain(problemas, p => p.Contains("alternativa repetida"));
    }

    [Fact]
    public void Validar_AcumulaTodosOsProblemasEmVezDePararNoPrimeiro()
    {
        // Devolver a lista inteira é o que deixa corrigir um lote de uma vez, em vez de descobrir
        // um erro por execução.
        var lote = Lote(QuestaoValida() with { Enunciado = "", Explicacao = "", Tipo = "Dissertativa" });

        Assert.True(CatalogoDeQuestoesDeSeed.Validar(new[] { lote }, Areas).Count >= 3);
    }

    // ------------------------------------------------------------------- regras da AWS

    [Fact]
    public void Validar_SimNaoEmExameAws_Acusa()
    {
        var lote = Lote(QuestaoValida() with
        {
            Tipo = "SimNao",
            Opcoes = Opcoes(("Sim", true), ("Não", false))
        });

        Assert.Contains(
            CatalogoDeQuestoesDeSeed.Validar(new[] { lote }, Areas, FornecedorDoExame.Aws),
            p => p.Contains("SimNao não existe nos exames AWS"));
    }

    /// <summary>
    /// "Two or more correct responses out of five or more response options": com quatro
    /// alternativas e duas corretas, a questão é mais fácil do que a da prova real.
    /// </summary>
    [Fact]
    public void Validar_EscolhaMultiplaComQuatroAlternativas_AcusaNaAwsENaoNaMicrosoft()
    {
        var questao = QuestaoMultipla() with { Enunciado = "Quais serviços atendem ao requisito?" };

        Assert.Contains(
            CatalogoDeQuestoesDeSeed.Validar(new[] { Lote(questao) }, Areas, FornecedorDoExame.Aws),
            p => p.Contains("ao menos 5 alternativas"));

        Assert.DoesNotContain(
            CatalogoDeQuestoesDeSeed.Validar(new[] { Lote(QuestaoMultipla()) }, Areas),
            p => p.Contains("alternativas (tem"));
    }

    [Fact]
    public void Validar_InstrucaoDeQuantidadeNoEnunciadoAws_Acusa()
    {
        // A tela AWS acrescenta "(Selecione DUAS.)" sozinha; escrita também no texto, sai duplicada.
        var lote = Lote(QuestaoMultipla() with
        {
            Enunciado = "Quais serviços atendem ao requisito? (Selecione DUAS.)",
            Opcoes = Opcoes(("A", true), ("B", true), ("C", false), ("D", false), ("E", false))
        });

        Assert.Contains(
            CatalogoDeQuestoesDeSeed.Validar(new[] { lote }, Areas, FornecedorDoExame.Aws),
            p => p.Contains("não escreva '(Selecione"));
    }

    [Fact]
    public void Validar_AssociacaoComSeteAlvos_PassaNaAwsEFalhaNaMicrosoft()
    {
        var associacoes = Enumerable.Range(0, 7)
            .Select(i => new AssociacaoDeSeed { Alvo = $"Alvo {i}", Item = $"Item {i % 3}" })
            .ToList();
        var lote = Lote(QuestaoDeAssociacao() with { Associacoes = associacoes });

        Assert.DoesNotContain(
            CatalogoDeQuestoesDeSeed.Validar(new[] { lote }, Areas, FornecedorDoExame.Aws),
            p => p.Contains("alvos (tem"));
        Assert.Contains(CatalogoDeQuestoesDeSeed.Validar(new[] { lote }, Areas), p => p.Contains("de 3 a 6 alvos"));
    }

    // ------------------------------------------------------------------------ ordenação

    /// <summary>
    /// Mesmo contrato de posição da associação: etapa a etapa, e dentro da etapa os passos da
    /// sequência seguidos dos extras. A posição decide o Id da alternativa.
    /// </summary>
    [Fact]
    public void Expandir_Ordenacao_GeraUmParPorEtapaEPasso_ComOGabaritoNaPosicaoCerta()
    {
        var expandida = CatalogoDeQuestoesDeSeed.Expandir(QuestaoDeOrdenacao());

        // 3 etapas × 4 passos (3 da sequência + 1 extra).
        Assert.Equal(12, expandida.Opcoes.Count);
        Assert.Equal(
            new[] { "Etapa 1", "Etapa 2", "Etapa 3" },
            expandida.Opcoes.Select(o => o.Alvo).Distinct());
        Assert.Equal(
            new[] { "Passo 1", "Passo 2", "Passo 3", "Passo extra" },
            expandida.Opcoes.Take(4).Select(o => o.Texto));
        Assert.Equal(
            new[] { "Etapa 1=Passo 1", "Etapa 2=Passo 2", "Etapa 3=Passo 3" },
            expandida.Opcoes.Where(o => o.Correta).Select(o => $"{o.Alvo}={o.Texto}"));
    }

    [Fact]
    public void Validar_OrdenacaoBemFormada_NaoAcusaNada()
    {
        Assert.Empty(CatalogoDeQuestoesDeSeed.Validar(new[] { Lote(QuestaoDeOrdenacao()) }, Areas, FornecedorDoExame.Aws));
    }

    [Fact]
    public void Validar_OrdenacaoSemPassoDistrator_Acusa()
    {
        // Sem passo sobrando, ninguém precisa decidir QUAIS passos pertencem à solução — só a ordem.
        var lote = Lote(QuestaoDeOrdenacao() with { ItensExtras = Array.Empty<string>() });

        Assert.Contains(
            CatalogoDeQuestoesDeSeed.Validar(new[] { lote }, Areas, FornecedorDoExame.Aws),
            p => p.Contains("sem passo distrator"));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(6)]
    public void Validar_OrdenacaoForaDaFaixaDePassos_Acusa(int quantidade)
    {
        var lote = Lote(QuestaoDeOrdenacao() with
        {
            Sequencia = Enumerable.Range(1, quantidade).Select(i => $"Passo {i}").ToList()
        });

        Assert.Contains(
            CatalogoDeQuestoesDeSeed.Validar(new[] { lote }, Areas, FornecedorDoExame.Aws),
            p => p.Contains("de 3 a 5 passos"));
    }

    [Fact]
    public void Validar_OrdenacaoComPassoRepetidoOuExtraNaSequencia_Acusa()
    {
        var lote = Lote(QuestaoDeOrdenacao() with
        {
            Sequencia = new[] { "Passo 1", "passo 1", "Passo 3" },
            ItensExtras = new[] { "Passo 3" }
        });

        var problemas = CatalogoDeQuestoesDeSeed.Validar(new[] { lote }, Areas, FornecedorDoExame.Aws);

        Assert.Contains(problemas, p => p.Contains("passo repetido"));
        Assert.Contains(problemas, p => p.Contains("também está na 'sequencia'"));
    }

    [Fact]
    public void Validar_SequenciaEmTipoQueNaoEOrdenacao_Acusa()
    {
        var lote = Lote(QuestaoValida() with { Sequencia = new[] { "Passo 1", "Passo 2", "Passo 3" } });

        Assert.Contains(
            CatalogoDeQuestoesDeSeed.Validar(new[] { lote }, Areas),
            p => p.Contains("só vale para o tipo Ordenacao"));
    }

    // --------------------------------------------------------------------------- auxiliares

    private static QuestaoDeSeed QuestaoDeOrdenacao() => new()
    {
        Id = "aifc01-teste-ordenacao-01",
        Topico = "Ciclo de vida de ML",
        Tipo = "Ordenacao",
        Enunciado = "Selecione e ordene os passos que colocam o modelo em produção.",
        Explicacao = ExplicacaoValida,
        Sequencia = new[] { "Passo 1", "Passo 2", "Passo 3" },
        ItensExtras = new[] { "Passo extra" }
    };

    private static ArquivoDeQuestoes Lote(params QuestaoDeSeed[] questoes) => new()
    {
        Origem = "lote-de-teste.json",
        ExameCode = "AZ-900",
        Area = AreaConhecida,
        Questoes = questoes
    };

    private static QuestaoDeSeed QuestaoValida() => new()
    {
        Id = "az900-teste-01",
        Topico = "Modelos de serviço",
        Tipo = "EscolhaUnica",
        Enunciado = "Uma empresa precisa de X restrição. Qual abordagem atende ao requisito?",
        Explicacao = ExplicacaoValida,
        Opcoes = Opcoes(("Primeira", true), ("Segunda", false), ("Terceira", false), ("Quarta", false))
    };

    private static QuestaoDeSeed QuestaoMultipla() => QuestaoValida() with
    {
        Id = "az900-teste-multipla-01",
        Tipo = "EscolhaMultipla",
        Enunciado = "Uma equipe precisa de X restrição. Selecione duas opções que atendem ao requisito.",
        Opcoes = Opcoes(("Primeira", true), ("Segunda", true), ("Terceira", false), ("Quarta", false))
    };

    /// <summary>
    /// Três alvos, dois itens de resposta (um deles reutilizado) e um distrator — o mínimo que
    /// passa em todas as regras do tipo.
    /// </summary>
    private static QuestaoDeSeed QuestaoDeAssociacao() => new()
    {
        Id = "az900-teste-associacao-01",
        Topico = "Modelos de serviço",
        Tipo = "Associacao",
        Enunciado = "Associe cada cenário à abordagem que o atende.",
        Explicacao = ExplicacaoValida,
        Associacoes = new[]
        {
            new AssociacaoDeSeed { Alvo = "Alvo A", Item = "Item 1" },
            new AssociacaoDeSeed { Alvo = "Alvo B", Item = "Item 2" },
            new AssociacaoDeSeed { Alvo = "Alvo C", Item = "Item 1" }
        },
        ItensExtras = new[] { "Distrator" }
    };

    private static IReadOnlyList<OpcaoDeSeed> Opcoes(params (string Texto, bool Correta)[] opcoes)
        => opcoes.Select(o => new OpcaoDeSeed { Texto = o.Texto, Correta = o.Correta }).ToList();
}
