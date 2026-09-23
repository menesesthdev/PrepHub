using System.Reflection;
using System.Text.Json;
using PrepHub.Domain.Enums;

namespace PrepHub.Infrastructure.Persistence.Seed;

/// <summary>
/// Lê e valida os arquivos de questões embutidos no assembly.
///
/// Adicionar questão ao projeto é adicionar um arquivo JSON aqui — nenhum código muda. A
/// validação é dura de propósito: um banco de questões com gabarito ausente, duplicata de
/// enunciado ou tipo inconsistente estraga a prova de forma silenciosa, e o teste que roda esta
/// validação é a única barreira antes disso chegar no usuário.
/// </summary>
public static class CatalogoDeQuestoesDeSeed
{
    private const string PastaDeRecursos = ".Persistence.Seed.Questoes.";

    private static readonly JsonSerializerOptions Opcoes = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>Todos os lotes embutidos, já desserializados. Não valida — use <see cref="Validar"/>.</summary>
    public static IReadOnlyList<ArquivoDeQuestoes> Carregar()
    {
        var assembly = typeof(CatalogoDeQuestoesDeSeed).Assembly;

        return assembly.GetManifestResourceNames()
            .Where(nome => nome.Contains(PastaDeRecursos, StringComparison.Ordinal)
                           && nome.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(nome => nome, StringComparer.Ordinal)
            .Select(nome => Desserializar(assembly, nome))
            .ToList();
    }

    private static ArquivoDeQuestoes Desserializar(Assembly assembly, string nomeDoRecurso)
    {
        using var stream = assembly.GetManifestResourceStream(nomeDoRecurso)
                           ?? throw new InvalidOperationException($"Recurso {nomeDoRecurso} não pôde ser aberto.");

        var arquivo = JsonSerializer.Deserialize<ArquivoDeQuestoes>(stream, Opcoes)
                      ?? throw new InvalidOperationException($"Recurso {nomeDoRecurso} não contém um lote de questões válido.");

        return arquivo with
        {
            Origem = NomeCurto(nomeDoRecurso),
            Questoes = arquivo.Questoes.Select(Expandir).ToList()
        };
    }

    /// <summary>
    /// Traduz o gabarito legível de uma questão de arrastar e soltar nos <b>pares candidatos</b>
    /// que vão para o banco: uma alternativa para cada combinação de alvo com item, correta apenas
    /// na combinação escrita em <c>associacoes</c>. Os outros tipos passam intactos.
    /// </summary>
    /// <remarks>
    /// ⚠️ A ordem da expansão é <b>contrato</b>, não detalhe: o Id de cada alternativa vem da
    /// posição (<see cref="GuidDeterministico.DeOpcao"/>), e as respostas já gravadas apontam para
    /// esses Ids. Ela é alvo-a-alvo, e dentro de cada alvo na ordem dos itens — os corretos na
    /// ordem em que aparecem em <c>associacoes</c>, depois os de <c>itensExtras</c>. Mudar isso
    /// reescreveria o gabarito de tentativas antigas em silêncio.
    ///
    /// Idempotente: questão que já chega com <c>opcoes</c> preenchidas passa direto, o que deixa
    /// <see cref="Validar"/> receber tanto a forma escrita à mão quanto a já expandida.
    /// </remarks>
    public static QuestaoDeSeed Expandir(QuestaoDeSeed questao)
    {
        ArgumentNullException.ThrowIfNull(questao);

        if (!TentarConverterTipo(questao.Tipo, out var tipo)
            || tipo is not (TipoDeQuestao.Associacao or TipoDeQuestao.Ordenacao)
            || questao.Opcoes.Count > 0)
        {
            return questao;
        }

        // A ordenação é uma associação cujos alvos são as posições: "Etapa 1" recebe o primeiro
        // passo da sequência, e assim por diante. Converter aqui mantém uma expansão só.
        var gabarito = tipo == TipoDeQuestao.Ordenacao
            ? questao.Sequencia
                .Select((passo, i) => new AssociacaoDeSeed { Alvo = RotuloDaEtapa(i + 1), Item = passo })
                .ToList()
            : questao.Associacoes.ToList();

        if (gabarito.Count == 0)
        {
            return questao;
        }

        var itens = ItensDoPainel(gabarito, questao.ItensExtras);

        var pares = new List<OpcaoDeSeed>(gabarito.Count * itens.Count);
        foreach (var associacao in gabarito)
        {
            foreach (var item in itens)
            {
                pares.Add(new OpcaoDeSeed
                {
                    Texto = item,
                    Alvo = associacao.Alvo,
                    Correta = string.Equals(item, associacao.Item, StringComparison.Ordinal)
                });
            }
        }

        return questao with { Opcoes = pares };
    }

    /// <summary>
    /// Rótulo do alvo de uma posição numa questão de ordenação ("Etapa 1"). É também o que a tela
    /// usa para ordenar as etapas — por isso é público e único: dois textos para a mesma etapa
    /// fariam a tela e o carregador discordarem da ordem.
    /// </summary>
    public static string RotuloDaEtapa(int posicao) => $"Etapa {posicao}";

    /// <summary>
    /// O painel de itens da questão: os que respondem a algum alvo, na ordem do gabarito, seguidos
    /// dos distratores. Sem repetição — um item que responde a dois alvos aparece uma vez só.
    /// </summary>
    private static IReadOnlyList<string> ItensDoPainel(IEnumerable<AssociacaoDeSeed> gabarito, IEnumerable<string> extras)
    {
        var itens = new List<string>();

        foreach (var texto in gabarito.Select(a => a.Item).Concat(extras))
        {
            if (!string.IsNullOrWhiteSpace(texto) && !itens.Contains(texto, StringComparer.Ordinal))
            {
                itens.Add(texto);
            }
        }

        return itens;
    }

    private static string NomeCurto(string nomeDoRecurso)
    {
        var indice = nomeDoRecurso.LastIndexOf(PastaDeRecursos, StringComparison.Ordinal);
        return indice < 0 ? nomeDoRecurso : nomeDoRecurso[(indice + PastaDeRecursos.Length)..];
    }

    /// <summary>
    /// Verifica tudo que o formato exige e devolve a lista de problemas — vazia quando o banco
    /// está íntegro. Devolver em vez de lançar deixa o teste mostrar todos os erros de uma vez,
    /// em vez de um por execução.
    /// </summary>
    public static IReadOnlyList<string> Validar(
        IReadOnlyList<ArquivoDeQuestoes> arquivos,
        IReadOnlyCollection<string> areasConhecidas,
        FornecedorDoExame fornecedor = FornecedorDoExame.Microsoft)
        => Validar(arquivos, _ => areasConhecidas, _ => fornecedor);

    /// <summary>
    /// Valida um catálogo de <b>vários exames</b>, cada lote contra as áreas do exame que ele
    /// declara em <c>exameCode</c>.
    /// </summary>
    /// <remarks>
    /// Além do escopo correto das áreas, esta sobrecarga fecha um buraco que só existe com mais de
    /// um exame: um lote cujo <c>exameCode</c> não casa com exame nenhum. O seeder aplica as
    /// questões filtrando por código, então esse lote não pertenceria a iteração nenhuma e
    /// <b>sumiria em silêncio</b> — sem erro, sem log, sem questão no banco. Com um exame só o
    /// risco era teórico; com quatro, um dígito trocado é questão de tempo.
    /// </remarks>
    /// <param name="fornecedorPorExame">
    /// Fornecedor de cada exame, que decide as regras de formato. Exame ausente do dicionário (ou
    /// dicionário nulo) é tratado como Microsoft, que era a única regra antes de a AWS existir.
    /// </param>
    public static IReadOnlyList<string> Validar(
        IReadOnlyList<ArquivoDeQuestoes> arquivos,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> areasPorExame,
        IReadOnlyDictionary<string, FornecedorDoExame>? fornecedorPorExame = null)
    {
        ArgumentNullException.ThrowIfNull(areasPorExame);

        return Validar(
            arquivos,
            codigo => areasPorExame.TryGetValue(codigo ?? string.Empty, out var areas) ? areas : null,
            codigo => fornecedorPorExame is not null
                      && fornecedorPorExame.TryGetValue(codigo ?? string.Empty, out var fornecedor)
                ? fornecedor
                : FornecedorDoExame.Microsoft);
    }

    /// <param name="areasDoExame">
    /// Devolve as áreas válidas para um código de exame, ou <c>null</c> quando o exame é
    /// desconhecido. A sobrecarga de lista plana ignora o código e devolve sempre as mesmas áreas
    /// — é o que mantém os testes de regra escrevendo lotes sintéticos sem declarar exame nenhum.
    /// </param>
    private static IReadOnlyList<string> Validar(
        IReadOnlyList<ArquivoDeQuestoes> arquivos,
        Func<string, IReadOnlyCollection<string>?> areasDoExame,
        Func<string, FornecedorDoExame> fornecedorDoExame)
    {
        var problemas = new List<string>();
        var idsVistos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var enunciadosVistos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var arquivo in arquivos)
        {
            var areasConhecidas = areasDoExame(arquivo.ExameCode);
            var fornecedor = fornecedorDoExame(arquivo.ExameCode);

            if (areasConhecidas is null)
            {
                problemas.Add($"{arquivo.Origem}: exameCode '{arquivo.ExameCode}' não corresponde a " +
                              "exame nenhum — o lote inteiro seria ignorado pelo seed sem nenhum aviso.");
            }
            else if (!areasConhecidas.Contains(arquivo.Area, StringComparer.OrdinalIgnoreCase))
            {
                problemas.Add($"{arquivo.Origem}: área '{arquivo.Area}' não existe no exame " +
                              $"{arquivo.ExameCode} (conhecidas: {string.Join(", ", areasConhecidas)}).");
            }

            if (arquivo.Questoes.Count == 0)
            {
                problemas.Add($"{arquivo.Origem}: nenhum item em 'questoes'.");
            }

            foreach (var original in arquivo.Questoes)
            {
                // Aceita tanto a forma escrita à mão quanto a já expandida — o carregador expande
                // ao ler o recurso, mas um lote montado em teste chega cru.
                var questao = Expandir(original);
                var rotulo = $"{arquivo.Origem}[{questao.Id}]";

                if (string.IsNullOrWhiteSpace(questao.Id))
                {
                    problemas.Add($"{arquivo.Origem}: questão sem 'id'.");
                    continue;
                }

                if (idsVistos.TryGetValue(questao.Id, out var origemAnterior))
                {
                    problemas.Add($"{rotulo}: id repetido (já usado em {origemAnterior}).");
                }
                else
                {
                    idsVistos[questao.Id] = arquivo.Origem;
                }

                ValidarConteudo(questao, rotulo, problemas);
                ValidarOpcoes(questao, rotulo, fornecedor, problemas);

                var chaveDeTexto = NormalizarTexto(questao.Enunciado);
                if (chaveDeTexto.Length > 0)
                {
                    if (enunciadosVistos.TryGetValue(chaveDeTexto, out var duplicata))
                    {
                        problemas.Add($"{rotulo}: enunciado duplicado de {duplicata}.");
                    }
                    else
                    {
                        enunciadosVistos[chaveDeTexto] = rotulo;
                    }
                }
            }
        }

        return problemas;
    }

    private static void ValidarConteudo(QuestaoDeSeed questao, string rotulo, List<string> problemas)
    {
        if (string.IsNullOrWhiteSpace(questao.Enunciado))
        {
            problemas.Add($"{rotulo}: enunciado vazio.");
        }

        if (string.IsNullOrWhiteSpace(questao.Explicacao))
        {
            problemas.Add($"{rotulo}: explicação vazia.");
        }
        else if (questao.Explicacao.Trim().Length < 120)
        {
            // Explicação curta demais não tem como justificar a correta E cada distrator — que é
            // o que o formato exige, e o que faz o simulado ensinar em vez de só pontuar.
            problemas.Add($"{rotulo}: explicação curta demais ({questao.Explicacao.Trim().Length} caracteres) " +
                          "para justificar a resposta certa e cada distrator.");
        }

        if (!TentarConverterTipo(questao.Tipo, out _))
        {
            problemas.Add($"{rotulo}: tipo '{questao.Tipo}' desconhecido " +
                          $"(esperado: {string.Join(", ", Enum.GetNames<TipoDeQuestao>())}).");
        }
    }

    private static void ValidarOpcoes(
        QuestaoDeSeed questao,
        string rotulo,
        FornecedorDoExame fornecedor,
        List<string> problemas)
    {
        var corretas = questao.Opcoes.Count(o => o.Correta);
        var tipoConhecido = TentarConverterTipo(questao.Tipo, out var tipoLido);
        var ehAssociacao = tipoConhecido && tipoLido is TipoDeQuestao.Associacao or TipoDeQuestao.Ordenacao;
        var ehAws = fornecedor == FornecedorDoExame.Aws;

        if (questao.Opcoes.Any(o => string.IsNullOrWhiteSpace(o.Texto)))
        {
            problemas.Add($"{rotulo}: alternativa com texto vazio.");
        }

        // Numa questão de arrastar, o mesmo item aparece em todos os alvos de propósito — a
        // repetição É o formato. A unicidade que importa ali é entre alvos e entre itens, e quem
        // a verifica é ValidarAssociacoes.
        if (!ehAssociacao)
        {
            var textosRepetidos = questao.Opcoes
                .GroupBy(o => NormalizarTexto(o.Texto))
                .Where(g => g.Key.Length > 0 && g.Count() > 1)
                .Select(g => g.First().Texto);

            foreach (var texto in textosRepetidos)
            {
                problemas.Add($"{rotulo}: alternativa repetida ('{texto}').");
            }
        }

        if (corretas == 0)
        {
            problemas.Add($"{rotulo}: nenhuma alternativa marcada como correta.");
        }

        if (!TentarConverterTipo(questao.Tipo, out var tipo))
        {
            return;
        }

        if (questao.Associacoes.Count > 0 && tipo != TipoDeQuestao.Associacao)
        {
            problemas.Add($"{rotulo}: 'associacoes' só vale para o tipo Associacao (tipo é {questao.Tipo}).");
        }

        if (questao.Sequencia.Count > 0 && tipo != TipoDeQuestao.Ordenacao)
        {
            problemas.Add($"{rotulo}: 'sequencia' só vale para o tipo Ordenacao (tipo é {questao.Tipo}).");
        }

        // Na AWS a instrução de quantidade é impressa pela tela, no fim do enunciado — "(Selecione
        // DUAS.)", "(Selecione e ordene TRÊS.)" —, como no demo oficial. Escrita também no texto,
        // sairia duplicada na prova.
        if (ehAws
            && tipo is TipoDeQuestao.EscolhaMultipla or TipoDeQuestao.Ordenacao
            && questao.Enunciado.Contains("(Selecione", StringComparison.OrdinalIgnoreCase))
        {
            problemas.Add($"{rotulo}: não escreva '(Selecione ...)' no enunciado — nos exames AWS a tela " +
                          "acrescenta a instrução a partir do gabarito.");
        }

        if (tipo == TipoDeQuestao.Ordenacao)
        {
            ValidarOrdenacao(questao, rotulo, problemas);
            return;
        }

        if (tipo == TipoDeQuestao.Associacao)
        {
            ValidarAssociacoes(questao, rotulo, ehAws ? 7 : 6, problemas);
            return;
        }

        if (ehAws && tipo == TipoDeQuestao.SimNao)
        {
            // O exam guide da AWS lista quatro formatos: escolha única, múltipla resposta, ordenação
            // e associação. Um item Sim/Não seria um formato que o candidato nunca verá na prova.
            problemas.Add($"{rotulo}: SimNao não existe nos exames AWS — use EscolhaUnica, EscolhaMultipla, " +
                          "Ordenacao ou Associacao.");
            return;
        }

        switch (tipo)
        {
            case TipoDeQuestao.EscolhaUnica when questao.Opcoes.Count != 4:
                problemas.Add($"{rotulo}: EscolhaUnica exige 4 alternativas (tem {questao.Opcoes.Count}).");
                break;

            case TipoDeQuestao.EscolhaUnica when corretas != 1:
                problemas.Add($"{rotulo}: EscolhaUnica exige exatamente 1 correta (tem {corretas}).");
                break;

            case TipoDeQuestao.EscolhaMultipla when questao.Opcoes.Count < (ehAws ? 5 : 4):
                // AWS: "two or more correct responses out of five or more response options".
                problemas.Add($"{rotulo}: EscolhaMultipla exige ao menos {(ehAws ? 5 : 4)} alternativas " +
                              $"(tem {questao.Opcoes.Count}).");
                break;

            case TipoDeQuestao.EscolhaMultipla when corretas < 2:
                problemas.Add($"{rotulo}: EscolhaMultipla exige ao menos 2 corretas (tem {corretas}).");
                break;

            case TipoDeQuestao.EscolhaMultipla when corretas >= questao.Opcoes.Count:
                problemas.Add($"{rotulo}: EscolhaMultipla com todas as alternativas corretas.");
                break;

            // O enunciado precisa dizer quantas marcar — a UI imprime "Escolha duas." a partir da
            // contagem, mas quem lê a questão tem de encontrar a instrução no texto também.
            case TipoDeQuestao.EscolhaMultipla when !ehAws && !MencionaQuantidade(questao.Enunciado):
                problemas.Add($"{rotulo}: EscolhaMultipla sem instrução de quantas alternativas selecionar.");
                break;

            case TipoDeQuestao.SimNao when questao.Opcoes.Count != 2:
                problemas.Add($"{rotulo}: SimNao exige exatamente 2 alternativas (tem {questao.Opcoes.Count}).");
                break;

            case TipoDeQuestao.SimNao when corretas != 1:
                problemas.Add($"{rotulo}: SimNao exige exatamente 1 correta (tem {corretas}).");
                break;
        }
    }

    /// <summary>
    /// Regras do arrastar e soltar, verificadas sobre o gabarito escrito à mão (alvos e itens) e
    /// não sobre os pares já expandidos — é ali que o erro é cometido e é ali que a mensagem
    /// precisa apontar.
    /// </summary>
    /// <param name="maximoDeAlvos">
    /// 6 na Microsoft (o que cabe no painel de arrastar); 7 na AWS, que publica "3–7 prompts" e
    /// responde por lista suspensa, uma linha por enunciado.
    /// </param>
    private static void ValidarAssociacoes(QuestaoDeSeed questao, string rotulo, int maximoDeAlvos, List<string> problemas)
    {
        var alvos = questao.Associacoes;

        if (alvos.Count < 3 || alvos.Count > maximoDeAlvos)
        {
            // Menos de três alvos vira escolha única disfarçada; acima do teto a prova real não vai.
            problemas.Add($"{rotulo}: Associacao exige de 3 a {maximoDeAlvos} alvos (tem {alvos.Count}).");
        }

        if (alvos.Any(a => string.IsNullOrWhiteSpace(a.Alvo) || string.IsNullOrWhiteSpace(a.Item)))
        {
            problemas.Add($"{rotulo}: associação com alvo ou item vazio.");
            return;
        }

        var alvosRepetidos = alvos
            .GroupBy(a => NormalizarTexto(a.Alvo))
            .Where(g => g.Count() > 1)
            .Select(g => g.First().Alvo);

        foreach (var alvo in alvosRepetidos)
        {
            // Dois alvos com o mesmo texto seriam a mesma caixa na tela, com dois gabaritos.
            problemas.Add($"{rotulo}: alvo repetido ('{alvo}').");
        }

        // Item repetido ENTRE ALVOS é reuso legítimo (o painel o desduplica, e é assim que se tira
        // a eliminação do jogo). O que não pode é repetição dentro de 'itensExtras' nem um extra
        // que na verdade é resposta de algum alvo — aí o distrator não é distrator nenhum.
        var respostas = alvos.Select(a => NormalizarTexto(a.Item)).ToHashSet();
        var extrasVistos = new HashSet<string>();

        foreach (var extra in questao.ItensExtras)
        {
            var chave = NormalizarTexto(extra);

            if (chave.Length == 0)
            {
                problemas.Add($"{rotulo}: item extra vazio.");
            }
            else if (respostas.Contains(chave))
            {
                problemas.Add($"{rotulo}: item extra '{extra}' também é resposta de um alvo.");
            }
            else if (!extrasVistos.Add(chave))
            {
                problemas.Add($"{rotulo}: item extra repetido ('{extra}').");
            }
        }

        var itensDistintos = alvos.Select(a => NormalizarTexto(a.Item)).Distinct().Count();
        if (questao.ItensExtras.Count == 0 && itensDistintos == alvos.Count)
        {
            // Painel com exatamente um item por alvo se resolve por eliminação: quem sabe todos
            // menos um acerta o último de graça. Ou entra um distrator, ou um item responde a mais
            // de um alvo — que é o outro jeito de tirar a contagem do jogo.
            problemas.Add($"{rotulo}: Associacao sem distrator — inclua 'itensExtras' ou reutilize " +
                          "um item em mais de um alvo, senão a última associação sai por eliminação.");
        }

        if (questao.Opcoes.Count != alvos.Count * ItensDoPainel(alvos, questao.ItensExtras).Count)
        {
            problemas.Add($"{rotulo}: pares candidatos inconsistentes com os alvos e itens declarados.");
        }
    }

    /// <summary>
    /// Regras da ordenação, verificadas sobre a sequência escrita à mão.
    /// </summary>
    /// <remarks>
    /// A AWS publica "a list of 3–5 responses" a ordenar, e o demo oficial lista passos que não
    /// entram em etapa nenhuma ("Each step should be selected one time or not at all"). Sem passo
    /// sobrando, a questão vira só "coloque em ordem" — mais fácil que a real, porque ninguém
    /// precisa decidir quais passos pertencem à solução.
    /// </remarks>
    private static void ValidarOrdenacao(QuestaoDeSeed questao, string rotulo, List<string> problemas)
    {
        var passos = questao.Sequencia;

        if (passos.Count < 3 || passos.Count > 5)
        {
            problemas.Add($"{rotulo}: Ordenacao exige de 3 a 5 passos na 'sequencia' (tem {passos.Count}).");
        }

        if (passos.Any(string.IsNullOrWhiteSpace))
        {
            problemas.Add($"{rotulo}: passo vazio na 'sequencia'.");
            return;
        }

        // Um passo repetido seria a mesma opção em duas etapas — o formato diz "uma vez ou nenhuma".
        foreach (var repetido in passos.GroupBy(NormalizarTexto).Where(g => g.Count() > 1).Select(g => g.First()))
        {
            problemas.Add($"{rotulo}: passo repetido na 'sequencia' ('{repetido}').");
        }

        if (questao.ItensExtras.Count == 0)
        {
            problemas.Add($"{rotulo}: Ordenacao sem passo distrator — inclua 'itensExtras' com passos que " +
                          "não pertencem à solução, como na prova real.");
        }

        var noGabarito = passos.Select(NormalizarTexto).ToHashSet();
        var extrasVistos = new HashSet<string>();
        foreach (var extra in questao.ItensExtras)
        {
            var chave = NormalizarTexto(extra);
            if (chave.Length == 0)
            {
                problemas.Add($"{rotulo}: item extra vazio.");
            }
            else if (noGabarito.Contains(chave))
            {
                problemas.Add($"{rotulo}: item extra '{extra}' também está na 'sequencia'.");
            }
            else if (!extrasVistos.Add(chave))
            {
                problemas.Add($"{rotulo}: item extra repetido ('{extra}').");
            }
        }

        var totalDePassos = passos.Select(NormalizarTexto).Concat(extrasVistos).Distinct().Count();
        if (questao.Opcoes.Count != passos.Count * totalDePassos)
        {
            problemas.Add($"{rotulo}: pares candidatos inconsistentes com a sequência declarada.");
        }
    }

    private static bool MencionaQuantidade(string enunciado)
    {
        var texto = enunciado.ToLowerInvariant();
        return texto.Contains("duas") || texto.Contains("três") || texto.Contains("tres")
               || texto.Contains(" dois") || texto.Contains("selecione");
    }

    public static bool TentarConverterTipo(string tipo, out TipoDeQuestao resultado)
        => Enum.TryParse(tipo, ignoreCase: true, out resultado) && Enum.IsDefined(resultado);

    private static string NormalizarTexto(string? texto)
        => string.IsNullOrWhiteSpace(texto)
            ? string.Empty
            : string.Join(' ', texto.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
}
