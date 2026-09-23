namespace PrepHub.Domain.Sorteio;

/// <summary>
/// Monta a prova de uma tentativa a partir do banco de questões. Puro: recebe o pool, os pesos
/// dos domínios e o histórico do usuário, e devolve os ids na ordem em que serão apresentados.
/// Sem banco, sem relógio, sem aleatoriedade escondida — o <see cref="Random"/> é injetado, o
/// que torna cada regra abaixo verificável em teste.
///
/// Três garantias, nessa ordem de prioridade:
/// 1. <b>Fidelidade ao blueprint</b> — a quantidade de questões por domínio segue o peso do
///    Skills Measured, não o tamanho do pool. Um domínio com 200 questões escritas não pode
///    dominar a prova só por ter mais conteúdo disponível.
/// 2. <b>Variedade entre tentativas</b> — questões vistas nas últimas tentativas cedem lugar às
///    inéditas, com uma cota pequena reservada para reencontrar o que a pessoa errou.
/// 3. <b>Dispersão por assunto</b> — dentro de um domínio, a cota se espalha entre os tópicos
///    disponíveis. Sem isso o peso do domínio seria respeitado e ainda assim a prova sairia
///    enviesada, com metade dos itens de "conceitos de nuvem" caindo sobre CapEx/OpEx.
/// </summary>
public static class SorteioDeQuestoes
{
    /// <summary>
    /// Sorteia as questões de uma tentativa.
    /// </summary>
    /// <returns>
    /// Ids na ordem de apresentação — já embaralhados entre domínios, como na prova real, que
    /// não agrupa os itens por assunto. Pode devolver menos que <paramref name="total"/> apenas
    /// se o pool inteiro for menor que isso.
    /// </returns>
    public static IReadOnlyList<Guid> Sortear(
        IReadOnlyCollection<QuestaoSorteavel> pool,
        IReadOnlyCollection<AreaSorteavel> areas,
        int total,
        IReadOnlyCollection<HistoricoDeQuestao> historico,
        PoliticaDeSorteio politica,
        Random rng)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(areas);
        ArgumentNullException.ThrowIfNull(historico);
        ArgumentNullException.ThrowIfNull(politica);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(total);

        if (pool.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        var vistas = ConsolidarHistorico(historico, politica);
        var porArea = pool.GroupBy(q => q.AreaId).ToDictionary(g => g.Key, g => (IReadOnlyList<QuestaoSorteavel>)g.ToList());

        var totalEfetivo = Math.Min(total, pool.Count);
        var cotas = DistribuirCotas(areas, porArea, totalEfetivo);
        var reforcos = DistribuirReforco(cotas, totalEfetivo, politica);

        var escolhidas = new List<Guid>(totalEfetivo);
        foreach (var (areaId, cota) in cotas)
        {
            escolhidas.AddRange(SortearNaArea(porArea[areaId], cota, reforcos[areaId], vistas, rng));
        }

        // Embaralha o conjunto final para que os domínios se misturem na navegação linear.
        var resultado = escolhidas.ToArray();
        rng.Shuffle(resultado);
        return resultado;
    }

    /// <summary>
    /// Reduz o histórico a uma linha por questão. Vale a ocorrência mais recente que TENHA
    /// resultado; só quando não há nenhuma é que sobra o registro sem resultado (a tentativa
    /// abandonada). Acertar hoje o que se errou três tentativas atrás tira a questão da fila de
    /// reforço — mas abandonar a prova sem responder não tira, porque isso apagaria um erro real
    /// registrado antes.
    /// </summary>
    private static Dictionary<Guid, HistoricoDeQuestao> ConsolidarHistorico(
        IReadOnlyCollection<HistoricoDeQuestao> historico,
        PoliticaDeSorteio politica)
        => historico
            .Where(h => h.TentativasAtras >= 1 && h.TentativasAtras <= politica.TentativasDeMemoria)
            .GroupBy(h => h.QuestaoId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(h => h.Acertou is null ? 1 : 0).ThenBy(h => h.TentativasAtras).First());

    /// <summary>
    /// Reparte o total entre os domínios proporcionalmente ao peso, pelo método do maior resto.
    /// Domínio sem questões suficientes devolve o excedente aos demais em vez de encolher a prova.
    /// </summary>
    private static List<(Guid AreaId, int Cota)> DistribuirCotas(
        IReadOnlyCollection<AreaSorteavel> areas,
        IReadOnlyDictionary<Guid, IReadOnlyList<QuestaoSorteavel>> porArea,
        int total)
    {
        // Só entram áreas que têm questão no pool — peso de área vazia seria cota perdida.
        var elegiveis = areas
            .Where(a => porArea.ContainsKey(a.AreaId))
            .OrderBy(a => a.AreaId)
            .ToList();

        AdicionarAreasOrfas(elegiveis, porArea);

        if (elegiveis.Count == 0)
        {
            return new List<(Guid, int)>();
        }

        var cotas = RepartirPorMaiorResto(elegiveis.Select(a => (a.AreaId, a.WeightPercent)), total);

        RedistribuirExcedente(cotas, porArea);

        return elegiveis
            .Select(a => (a.AreaId, Cota: cotas[a.AreaId]))
            .Where(c => c.Cota > 0)
            .ToList();
    }

    /// <summary>
    /// Questão órfã — cuja área não aparece na lista de pesos informada — não pode sumir do pool
    /// em silêncio. Recebe peso proporcional à fatia que ocupa no banco, de forma que a cota dela
    /// fique perto de <c>total × questõesÓrfãs / questõesTotais</c>.
    /// </summary>
    /// <remarks>
    /// Peso zero (o caminho ingênuo) NÃO resolve: com as demais áreas somando peso positivo, a
    /// parte exata da órfã é 0 e o resto é 0, então ela perde também o desempate do maior resto e
    /// nunca é sorteada. Isso é dado inconsistente, não fluxo normal — mas some calado, e questão
    /// que some calada do simulado é exatamente o defeito difícil de perceber.
    /// </remarks>
    private static void AdicionarAreasOrfas(
        List<AreaSorteavel> elegiveis,
        IReadOnlyDictionary<Guid, IReadOnlyList<QuestaoSorteavel>> porArea)
    {
        var orfas = porArea.Keys
            .Where(id => elegiveis.All(a => a.AreaId != id))
            .OrderBy(id => id)
            .ToList();

        if (orfas.Count == 0)
        {
            return;
        }

        var questoesConhecidas = elegiveis.Sum(a => porArea[a.AreaId].Count);
        var pesoConhecido = elegiveis.Sum(a => a.WeightPercent);

        foreach (var areaId in orfas)
        {
            // Sem área conhecida (ou sem peso declarado) o divisor sumiria: fica zero para todas e
            // a repartição cai no rateio igualitário, que é o desfecho correto nesse caso.
            var peso = questoesConhecidas == 0 || pesoConhecido <= 0m
                ? 0m
                : pesoConhecido * porArea[areaId].Count / questoesConhecidas;

            elegiveis.Add(new AreaSorteavel(areaId, peso));
        }
    }

    /// <summary>
    /// Reparte o teto de repetidas entre os domínios, proporcionalmente à cota de cada um.
    /// </summary>
    /// <remarks>
    /// O orçamento é calculado UMA vez sobre o total e depois dividido — e não aplicado como
    /// fração dentro de cada domínio. A diferença não é cosmética: com cotas de 11/16/13, arredondar
    /// para baixo dentro de cada domínio daria 1+2+1 = 4 vagas, e não as 6 que a política de 15%
    /// promete. Um terço do reforço se perderia no arredondamento, silenciosamente.
    /// </remarks>
    private static Dictionary<Guid, int> DistribuirReforco(
        IReadOnlyCollection<(Guid AreaId, int Cota)> cotas,
        int total,
        PoliticaDeSorteio politica)
    {
        var orcamento = (int)Math.Floor(total * politica.ProporcaoMaximaRepetidas);
        return RepartirPorMaiorResto(cotas.Select(c => (c.AreaId, (decimal)c.Cota)), orcamento);
    }

    /// <summary>
    /// Método do maior resto (Hare/Hamilton): reparte <paramref name="total"/> unidades inteiras
    /// entre chaves com peso, de forma que a soma bata exatamente com o total, sem sobra de
    /// arredondamento. Empate no resto é resolvido pela chave, para o sorteio continuar
    /// determinístico dado o mesmo <see cref="Random"/>.
    /// </summary>
    private static Dictionary<Guid, int> RepartirPorMaiorResto(
        IEnumerable<(Guid Chave, decimal Peso)> itens,
        int total)
    {
        var lista = itens.ToList();
        if (lista.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        if (total <= 0)
        {
            return lista.ToDictionary(i => i.Chave, _ => 0);
        }

        var pesoTotal = lista.Sum(i => i.Peso);
        var exatos = lista
            .Select(i => (i.Chave,
                Exato: pesoTotal <= 0m
                    ? (decimal)total / lista.Count            // sem peso declarado, divide igual
                    : i.Peso / pesoTotal * total))
            .ToList();

        var resultado = exatos.ToDictionary(e => e.Chave, e => (int)Math.Floor(e.Exato));

        var sobra = Math.Clamp(total - resultado.Values.Sum(), 0, lista.Count);
        foreach (var item in exatos
                     .OrderByDescending(e => e.Exato - Math.Floor(e.Exato))
                     .ThenBy(e => e.Chave)
                     .Take(sobra))
        {
            resultado[item.Chave]++;
        }

        return resultado;
    }

    /// <summary>
    /// Corta o que uma área não tem como entregar e realoca para as que ainda têm folga, da que
    /// tem MAIS vagas sobrando para a que tem menos (folga real, não peso do blueprint: realocar
    /// por peso reabriria o mesmo estouro que se está corrigindo). Para quando ninguém mais tem
    /// folga — nesse ponto o pool inteiro é menor que o total pedido e a prova sai mais curta,
    /// que é o único desfecho honesto.
    /// </summary>
    private static void RedistribuirExcedente(
        Dictionary<Guid, int> cotas,
        IReadOnlyDictionary<Guid, IReadOnlyList<QuestaoSorteavel>> porArea)
    {
        var excedente = 0;
        foreach (var areaId in cotas.Keys.ToList())
        {
            var disponivel = porArea[areaId].Count;
            if (cotas[areaId] > disponivel)
            {
                excedente += cotas[areaId] - disponivel;
                cotas[areaId] = disponivel;
            }
        }

        while (excedente > 0)
        {
            var comFolga = cotas.Keys
                .Where(id => cotas[id] < porArea[id].Count)
                .OrderByDescending(id => porArea[id].Count - cotas[id])
                .ThenBy(id => id)
                .ToList();

            if (comFolga.Count == 0)
            {
                return;
            }

            foreach (var areaId in comFolga)
            {
                if (excedente == 0)
                {
                    return;
                }

                cotas[areaId]++;
                excedente--;
            }
        }
    }

    /// <summary>
    /// Escolhe as questões de um domínio, em quatro passes de prioridade decrescente:
    /// <list type="number">
    /// <item>reforço — questões ERRADAS recentemente, até <paramref name="tetoDeReforco"/>;</item>
    /// <item>inéditas — o corpo da prova;</item>
    /// <item>erradas restantes — quando as inéditas acabam;</item>
    /// <item>demais vistas (acertadas ou apenas apresentadas), das mais antigas para as mais
    ///       novas — último recurso.</item>
    /// </list>
    /// Os passes 3 e 4 estouram o teto de propósito: com o banco esgotado, a alternativa seria
    /// entregar uma prova menor que o exame real, o que quebraria a fidelidade que é o produto.
    /// </summary>
    private static IEnumerable<Guid> SortearNaArea(
        IReadOnlyList<QuestaoSorteavel> candidatas,
        int cota,
        int tetoDeReforco,
        IReadOnlyDictionary<Guid, HistoricoDeQuestao> vistas,
        Random rng)
    {
        var ineditas = new List<QuestaoSorteavel>();
        var erradas = new List<QuestaoSorteavel>();
        var demaisVistas = new List<(QuestaoSorteavel Questao, int TentativasAtras)>();

        foreach (var questao in candidatas)
        {
            if (!vistas.TryGetValue(questao.QuestaoId, out var visto))
            {
                ineditas.Add(questao);
            }
            else if (visto.Acertou == false)
            {
                erradas.Add(questao);
            }
            else
            {
                // Acertou == true (não precisa de reforço) ou == null (apresentada numa tentativa
                // abandonada, sem resposta): em ambos os casos é só "recente", não é dívida.
                demaisVistas.Add((questao, visto.TentativasAtras));
            }
        }

        var ineditasEspalhadas = EspalharPorTopico(ineditas, rng);
        var erradasEspalhadas = EspalharPorTopico(erradas, rng);

        var escolhidas = new List<Guid>(cota);

        var reforco = Math.Min(Math.Min(tetoDeReforco, cota), erradasEspalhadas.Count);
        escolhidas.AddRange(erradasEspalhadas.Take(reforco).Select(q => q.QuestaoId));

        escolhidas.AddRange(ineditasEspalhadas
            .Take(cota - escolhidas.Count)
            .Select(q => q.QuestaoId));

        if (escolhidas.Count < cota)
        {
            escolhidas.AddRange(erradasEspalhadas
                .Skip(reforco)
                .Take(cota - escolhidas.Count)
                .Select(q => q.QuestaoId));
        }

        if (escolhidas.Count < cota)
        {
            // Embaralha antes de ordenar: OrderBy é estável, então o shuffle é o que desempata
            // entre questões vindas da mesma tentativa.
            Embaralhar(demaisVistas, rng);
            escolhidas.AddRange(demaisVistas
                .OrderByDescending(x => x.TentativasAtras)
                .Take(cota - escolhidas.Count)
                .Select(x => x.Questao.QuestaoId));
        }

        return escolhidas;
    }

    /// <summary>
    /// Ordena as candidatas de um domínio alternando os assuntos: um item de cada tópico por
    /// rodada. Como os passes de seleção sempre consomem o começo da lista, isso faz a cota do
    /// domínio varrer tópicos diferentes antes de repetir qualquer um deles.
    /// </summary>
    /// <remarks>
    /// A ordem dos tópicos e a ordem dentro de cada tópico são sorteadas, senão a dispersão viria
    /// sempre na mesma sequência e duas provas do mesmo usuário abririam pelo mesmo assunto.
    /// Questão sem tópico declarado cai num balde único — o que a torna o assunto mais concentrado
    /// do domínio, e é o incentivo certo para preencher o campo nos arquivos de seed.
    /// </remarks>
    private static List<QuestaoSorteavel> EspalharPorTopico(List<QuestaoSorteavel> questoes, Random rng)
    {
        if (questoes.Count <= 1)
        {
            return questoes;
        }

        var baldes = questoes
            .GroupBy(q => q.Topico ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var itens = g.ToList();
                Embaralhar(itens, rng);
                return itens;
            })
            .ToList();

        Embaralhar(baldes, rng);

        var resultado = new List<QuestaoSorteavel>(questoes.Count);
        for (var rodada = 0; resultado.Count < questoes.Count; rodada++)
        {
            foreach (var balde in baldes.Where(b => rodada < b.Count))
            {
                resultado.Add(balde[rodada]);
            }
        }

        return resultado;
    }

    private static void Embaralhar<T>(List<T> itens, Random rng)
    {
        var buffer = itens.ToArray();
        rng.Shuffle(buffer);
        itens.Clear();
        itens.AddRange(buffer);
    }
}
