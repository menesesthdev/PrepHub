using PrepHub.Domain.Entidades;
using PrepHub.Domain.Enums;
using PrepHub.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace PrepHub.Infrastructure.Persistence;

/// <summary>
/// Popula o banco com os exames definidos em <see cref="Exames"/> e com as questões dos arquivos
/// de seed embutidos.
///
/// IMPORTANTE (regra não negociável do projeto): nenhuma questão do banco é ou pode ser copiada
/// de dumps/vazamentos de prova, nem adaptada dos practice assessments oficiais da Microsoft —
/// aqueles são públicos e não violam NDA, mas continuam sendo conteúdo proprietário. Todas as
/// questões são escritas a partir do Skills Measured outline público, com engenharia de distrator
/// e explicação por distrator — ver <c>docs/formato-questoes.md</c>.
///
/// O seed é <b>idempotente e incremental</b>: roda a cada startup, insere o que é novo e
/// atualiza o que mudou, casando pelo <c>ExternalId</c> do arquivo. Adicionar um lote de questões
/// é adicionar um JSON, e adicionar um exame é uma entrada em <see cref="Exames"/> — nada no
/// algoritmo abaixo muda em nenhum dos dois casos.
/// </summary>
public static class PrepHubDbSeeder
{
    /// <summary>
    /// Os exames que o simulado publica, com os pesos do Skills Measured outline oficial.
    /// </summary>
    /// <remarks>
    /// ⚠️ Um exame entra nesta lista quando o banco de questões dele existe, não quando se decide
    /// escrevê-lo. Exame publicado sem pool suficiente não falha: o sorteio faz
    /// <c>Math.Min(total, pool.Count)</c> e entrega uma prova mais curta, sempre parecida, sem
    /// aviso nenhum — e fidelidade à prova real é justamente o produto. Ver o aviso emitido por
    /// <see cref="ConferirTamanhoDoPool"/>.
    /// </remarks>
    public static IReadOnlyList<DefinicaoDeExame> Exames { get; } =
    [
        // 40 itens em 45 minutos espelha a entrega real do AZ-900. TotalQuestions não é "o tamanho
        // do banco" e sim o tamanho da PROVA: o sorteio escolhe 40 entre centenas a cada tentativa.
        new DefinicaoDeExame(
            Code: "AZ-900",
            Name: "Microsoft Azure Fundamentals",
            TimeLimitMinutes: 45,
            PassingScorePercent: 70,
            TotalQuestions: 40,
            Areas:
            [
                new AreaDeExame("conceitos-de-nuvem", "Descrever conceitos de nuvem", 27.5m),
                new AreaDeExame("arquitetura", "Descrever arquitetura e serviços do Azure", 37.5m),
                new AreaDeExame("governanca", "Descrever gestão e governança do Azure", 32.5m)
            ]),

        // Pesos conferidos no study guide oficial (skills measured de 17/04/2026): os slugs abaixo
        // são os cinco functional groups publicados, com o ponto médio de cada faixa.
        // Publicado em 27/08/2026, com os cinco domínios cobertos e pool acima da cota de cada um.
        new DefinicaoDeExame(
            Code: "AZ-104",
            Name: "Microsoft Azure Administrator",
            TimeLimitMinutes: 100,
            PassingScorePercent: 70,
            TotalQuestions: 50,
            Areas:
            [
                new AreaDeExame("identidade-governanca", "Gerenciar identidades e governança do Azure", 22.5m),
                new AreaDeExame("armazenamento", "Implementar e gerenciar armazenamento", 17.5m),
                new AreaDeExame("computacao", "Implantar e gerenciar recursos de computação do Azure", 22.5m),
                new AreaDeExame("rede-virtual", "Implementar e gerenciar rede virtual", 17.5m),
                new AreaDeExame("monitoramento", "Monitorar e manter recursos do Azure", 12.5m)
            ],
            Publicado: true),

        // Pesos conferidos no study guide oficial (skills measured de 17/04/2026). Somam 100 —
        // é o único dos quatro exames cujas faixas fecham exatamente no ponto médio.
        new DefinicaoDeExame(
            Code: "AZ-305",
            Name: "Designing Microsoft Azure Infrastructure Solutions",
            TimeLimitMinutes: 120,
            PassingScorePercent: 70,
            TotalQuestions: 50,
            Areas:
            [
                new AreaDeExame("identidade-governanca-monitoramento", "Projetar soluções de identidade, governança e monitoramento", 27.5m),
                new AreaDeExame("armazenamento-dados", "Projetar soluções de armazenamento de dados", 22.5m),
                new AreaDeExame("continuidade", "Projetar soluções de continuidade de negócios", 17.5m),
                new AreaDeExame("infraestrutura", "Projetar soluções de infraestrutura", 32.5m)
            ],
            Publicado: true),

        // Pesos conferidos no study guide oficial (skills measured de 27/07/2026).
        // ⚠️ 'pipelines' vale 50–55% sozinho: metade da prova sai de um domínio só, e é por ele que
        // o banco tem de começar. Um banco equilibrado entre os cinco daria uma prova enviesada.
        new DefinicaoDeExame(
            Code: "AZ-400",
            Name: "Designing and Implementing Microsoft DevOps Solutions",
            TimeLimitMinutes: 120,
            PassingScorePercent: 70,
            TotalQuestions: 50,
            Areas:
            [
                new AreaDeExame("processos-comunicacao", "Projetar e implementar processos e comunicação", 12.5m),
                new AreaDeExame("controle-codigo", "Projetar e implementar estratégia de controle de código-fonte", 12.5m),
                new AreaDeExame("pipelines", "Projetar e implementar pipelines de build e release", 52.5m),
                new AreaDeExame("seguranca-conformidade", "Desenvolver plano de segurança e conformidade", 12.5m),
                new AreaDeExame("instrumentacao", "Implementar estratégia de instrumentação", 7.5m)
            ],
            Publicado: true),

        // Primeiro exame AWS, publicado em 15/09/2026 com 450 questões (90/108/126/63/63).
        // Exam guide oficial v1.1 (30/04/2026): 65 questões em 90 minutos — 50
        // pontuadas e 15 não pontuadas na prova real; aqui as 65 contam (decisão de 15/09/2026:
        // descartar 15 ao acaso só deixaria a nota mais ruidosa, e a pressão de tempo é a mesma).
        // Os pesos são números exatos, não faixas. A AWS não publica percentual de corte, só "700
        // numa escala de 100–1000"; os 70% são a âncora da nossa aproximação, como nos exames Azure.
        new DefinicaoDeExame(
            Code: "AIF-C01",
            Name: "AWS Certified AI Practitioner",
            TimeLimitMinutes: 90,
            PassingScorePercent: 70,
            TotalQuestions: 65,
            Areas:
            [
                new AreaDeExame("fundamentos-ia-ml", "Fundamentos de IA e ML", 20m),
                new AreaDeExame("fundamentos-ia-generativa", "Fundamentos de IA generativa", 24m),
                new AreaDeExame("aplicacoes-modelos-fundacionais", "Aplicações de modelos de base", 28m),
                new AreaDeExame("ia-responsavel", "Diretrizes para IA responsável", 14m),
                new AreaDeExame("seguranca-conformidade-governanca", "Segurança, conformidade e governança para soluções de IA", 14m)
            ],
            Publicado: true,
            Fornecedor: FornecedorDoExame.Aws)
    ];

    /// <summary>
    /// Os slugs de área que os arquivos de questões podem referenciar, por código de exame.
    /// </summary>
    /// <remarks>
    /// Público para que o teste de integridade valide o catálogo real contra as áreas reais. Sem
    /// isso, o teste teria de repetir os slugs, e a cópia divergiria da definição sem nada quebrar
    /// — que é exatamente o tipo de falha calada que este catálogo não pode ter.
    ///
    /// É um dicionário, e não uma lista plana, porque a área é escopada ao exame: <c>redes</c> do
    /// AZ-104 não existe no AZ-900. Com uma lista única, um lote do AZ-104 apontando para uma área
    /// do AZ-900 passaria na validação e as questões cairiam no domínio errado.
    /// </remarks>
    public static IReadOnlyDictionary<string, IReadOnlyCollection<string>> AreasPorExame { get; } =
        Exames.ToDictionary(
            e => e.Code,
            e => (IReadOnlyCollection<string>)e.Areas.Select(a => a.Key).ToList(),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// O fornecedor de cada exame, por código — é o que diz ao validador quais regras de formato
    /// valem para cada lote (a AWS não tem Sim/Não; múltipla resposta pede ao menos 5 alternativas).
    /// </summary>
    public static IReadOnlyDictionary<string, FornecedorDoExame> FornecedorPorExame { get; } =
        Exames.ToDictionary(e => e.Code, e => e.Fornecedor, StringComparer.OrdinalIgnoreCase);

    public static async Task SemearAsync(
        PrepHubDbContext db,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var catalogo = CatalogoDeQuestoesDeSeed.Carregar();

        // Valida o catálogo INTEIRO de uma vez, antes de tocar no banco. Validar por exame, dentro
        // do laço, deixaria passar justamente o erro que só existe entre exames: um lote cujo
        // 'exameCode' não casa com exame nenhum não pertence a nenhuma iteração e sumiria calado.
        var problemas = CatalogoDeQuestoesDeSeed.Validar(catalogo, AreasPorExame, FornecedorPorExame);
        if (problemas.Count > 0)
        {
            // Fail fast: subir com banco de questões inválido produziria prova com gabarito
            // errado, que é pior que não subir. O teste de integridade pega isso antes do deploy.
            throw new InvalidOperationException(
                "Banco de questões inválido:" + Environment.NewLine + string.Join(Environment.NewLine, problemas));
        }

        foreach (var definicao in Exames)
        {
            var exam = await SincronizarExameAsync(db, definicao, cancellationToken);
            await AplicarQuestoesAsync(db, exam, catalogo, cancellationToken);
            ConferirTamanhoDoPool(exam, definicao, logger);
        }
    }

    /// <summary>
    /// Cria o exame se ele não existe e, se existe, realinha parâmetros e áreas com a definição.
    /// </summary>
    private static async Task<Exame> SincronizarExameAsync(
        PrepHubDbContext db,
        DefinicaoDeExame definicao,
        CancellationToken cancellationToken)
    {
        var exam = await db.Exams
            .Include(e => e.SkillAreas)
            .FirstOrDefaultAsync(e => e.Code == definicao.Code, cancellationToken);

        if (exam is null)
        {
            exam = new Exame(
                code: definicao.Code,
                name: definicao.Name,
                timeLimitMinutes: definicao.TimeLimitMinutes,
                passingScorePercent: definicao.PassingScorePercent,
                totalQuestions: definicao.TotalQuestions,
                isPublished: definicao.Publicado,
                vendor: definicao.Fornecedor);

            db.Exams.Add(exam);
        }
        else
        {
            exam.AtualizarDefinicao(
                definicao.Name,
                definicao.TimeLimitMinutes,
                definicao.PassingScorePercent,
                definicao.TotalQuestions,
                definicao.Publicado,
                definicao.Fornecedor);
        }

        var areasPorKey = exam.SkillAreas.ToDictionary(a => a.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var area in definicao.Areas)
        {
            if (areasPorKey.TryGetValue(area.Key, out var existente))
            {
                existente.Atualizar(area.Name, area.WeightPercent);
            }
            else
            {
                exam.AdicionarAreaDeHabilidade(area.Key, area.Name, area.WeightPercent);
            }
        }

        // Área que saiu da definição NÃO é removida: as questões apontam para ela por FK
        // Restrict, e apagá-la falharia no SaveChanges. Ela simplesmente deixa de receber cota no
        // sorteio quando fica sem questão ativa — e, se ainda tiver, o SorteioDeQuestoes a trata
        // como área órfã e lhe dá peso proporcional em vez de descartá-la em silêncio.
        await db.SaveChangesAsync(cancellationToken);

        return exam;
    }

    /// <summary>
    /// Avisa quando o pool ativo não sustenta o tamanho declarado da prova.
    /// </summary>
    /// <remarks>
    /// Aviso, e não exceção: derrubar a aplicação porque um exame em construção ainda tem banco
    /// magro impediria justamente o trabalho de construí-lo. Mas o silêncio também não serve — o
    /// sorteio corta o total para o tamanho do pool sem reclamar, e o sintoma para o usuário é uma
    /// prova curta e sempre parecida, que ninguém associa a "faltam questões escritas".
    /// </remarks>
    private static void ConferirTamanhoDoPool(Exame exam, DefinicaoDeExame definicao, ILogger? logger)
    {
        var ativas = exam.Questions.Count(q => q.IsActive);

        // Domínio sem questão é pior que pool pequeno e não aparece na contagem total: o sorteio
        // ignora a área vazia e redistribui a cota dela entre as demais, produzindo prova do
        // tamanho certo em que uma fatia inteira do blueprint não aparece. Por isso a checagem por
        // domínio vem ANTES do retorno antecipado — senão o aviso sumiria justamente quando o
        // total já passou do exigido e o exame continua impublicável.
        var vazios = exam.SkillAreas
            .Where(area => !exam.Questions.Any(q => q.IsActive && q.SkillAreaId == area.Id))
            .Select(area => area.Key)
            .ToList();

        if (vazios.Count > 0)
        {
            logger?.Log(
                definicao.Publicado ? LogLevel.Warning : LogLevel.Information,
                "Exame {Codigo}: {Sujeito} {Dominios} sem nenhuma questão ativa. O sorteio " +
                "redistribui a cota {Pronome} entre os demais, então a prova sai completa com parte " +
                "do blueprint ausente.",
                definicao.Code,
                vazios.Count == 1 ? "domínio" : "domínios",
                string.Join(", ", vazios),
                vazios.Count == 1 ? "dele" : "deles");
        }

        // Domínio que não sustenta a PRÓPRIA cota é mais sutil que domínio vazio e igualmente
        // silencioso: o sorteio corta a cota dele para o que existe e redistribui a diferença entre
        // os demais (RedistribuirExcedente), então a prova sai completa, do tamanho certo, com o
        // blueprint distorcido. Aparece sobretudo em exame com um domínio muito pesado — no AZ-400,
        // 'pipelines' vale 52,5% e sozinho pede mais da metade dos itens.
        var pesoTotal = exam.SkillAreas.Sum(a => a.WeightPercent);
        if (pesoTotal > 0m)
        {
            foreach (var area in exam.SkillAreas)
            {
                var cota = (int)Math.Ceiling(area.WeightPercent / pesoTotal * definicao.TotalQuestions);
                var disponiveis = exam.Questions.Count(q => q.IsActive && q.SkillAreaId == area.Id);

                if (disponiveis > 0 && disponiveis < cota)
                {
                    logger?.Log(
                        definicao.Publicado ? LogLevel.Warning : LogLevel.Information,
                        "Exame {Codigo}: o domínio {Dominio} tem {Disponiveis} questões para uma cota " +
                        "de {Cota} itens ({Peso}% do blueprint). O sorteio redistribui a diferença " +
                        "entre os demais domínios, distorcendo a repartição da prova.",
                        definicao.Code,
                        area.Key,
                        disponiveis,
                        cota,
                        area.WeightPercent);
                }
            }
        }

        if (ativas >= definicao.TotalQuestions)
        {
            return;
        }

        if (!definicao.Publicado)
        {
            // Em construção o pool magro é o estado esperado — vira nota de progresso, não alarme.
            logger?.LogInformation(
                "Exame {Codigo} (em construção): {Ativas} de {Total} questões necessárias para publicar.",
                definicao.Code,
                ativas,
                definicao.TotalQuestions);

            return;
        }

        logger?.LogWarning(
            "Exame {Codigo} está PUBLICADO com apenas {Ativas} questões ativas para uma prova de " +
            "{Total} itens. O sorteio vai entregar uma prova mais curta e pouco variada — marque-o " +
            "como não publicado ou complete o banco.",
            definicao.Code,
            ativas,
            definicao.TotalQuestions);
    }

    /// <summary>
    /// Aplica todos os lotes embutidos. Questão nova entra; questão já conhecida tem enunciado,
    /// explicação e alternativas reescritos no lugar, preservando o Id — é isso que permite
    /// corrigir um erro de conteúdo sem invalidar as tentativas que já responderam aquele item.
    /// </summary>
    private static async Task AplicarQuestoesAsync(
        PrepHubDbContext db,
        Exame exam,
        IReadOnlyList<ArquivoDeQuestoes> catalogo,
        CancellationToken cancellationToken)
    {
        var arquivos = catalogo
            .Where(a => string.Equals(a.ExameCode, exam.Code, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (arquivos.Count == 0)
        {
            return;
        }

        var areasPorKey = exam.SkillAreas.ToDictionary(a => a.Key, StringComparer.OrdinalIgnoreCase);

        // O Guid vem do ExternalId, então buscar por Id já cobre a colisão de chave entre exames —
        // não é preciso uma segunda consulta por ExternalId. As questões deste exame entram na
        // busca mesmo sem estar nos arquivos: são elas que podem precisar ser aposentadas.
        var idsDosArquivos = arquivos
            .SelectMany(a => a.Questoes)
            .Select(q => GuidDeterministico.DeChave(q.Id))
            .ToHashSet();

        var existentes = await db.Questions
            .Include(q => q.Options)
            .Where(q => q.ExamId == exam.Id || idsDosArquivos.Contains(q.Id))
            .ToDictionaryAsync(q => q.Id, cancellationToken);

        foreach (var arquivo in arquivos)
        {
            var area = areasPorKey[arquivo.Area];

            foreach (var seed in arquivo.Questoes)
            {
                CatalogoDeQuestoesDeSeed.TentarConverterTipo(seed.Tipo, out var tipo);
                var id = GuidDeterministico.DeChave(seed.Id);

                if (existentes.TryGetValue(id, out var questao))
                {
                    if (questao.ExamId != exam.Id)
                    {
                        // A chave de seed é global (índice único em ExternalId) e o Guid deriva
                        // dela. Sem esta checagem, o segundo exame a usar a mesma chave quebraria
                        // no SaveChanges com uma violação de índice sem explicação nenhuma.
                        throw new InvalidOperationException(
                            $"A chave de questão '{seed.Id}' já pertence a outro exame. " +
                            "Chaves de seed são globais — prefixe-as com o código do exame.");
                    }

                    AtualizarQuestao(db, questao, area.Id, seed, tipo);
                }
                else
                {
                    var nova = exam.AdicionarQuestao(area.Id, seed.Id, seed.Enunciado, tipo, seed.Explicacao, seed.Topico, id);
                    for (var i = 0; i < seed.Opcoes.Count; i++)
                    {
                        nova.AdicionarOpcao(
                            seed.Opcoes[i].Texto,
                            seed.Opcoes[i].Correta,
                            i,
                            GuidDeterministico.DeOpcao(seed.Id, i),
                            seed.Opcoes[i].Alvo);
                    }

                    // Add explícito: a chave já vem preenchida do domínio, então o EF não tem como
                    // inferir sozinho que é inserção — mesmo motivo do Add de respostas no repositório.
                    db.Questions.Add(nova);
                }
            }
        }

        SincronizarAtivas(existentes.Values, exam.Id, idsDosArquivos);

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Alinha o pool ao conteúdo dos arquivos: o que saiu deles é aposentado, o que voltou é
    /// reativado. É o que faltava para os arquivos serem de fato a fonte única da verdade — antes
    /// disso, apagar uma questão do JSON não a tirava do sorteio, e não havia como retirar de
    /// circulação uma questão com gabarito errado.
    /// </summary>
    /// <remarks>
    /// Aposentar, nunca apagar: as tentativas já feitas apontam para a questão, e o FK
    /// <c>Restrict</c> de <c>ExamAttemptQuestions</c> existe justamente para impedir o DELETE.
    /// ⚠️ Isso alcança também as questões do seed hardcoded antigo (marcadas como
    /// <c>az900-legado-*</c> pela migration <c>SorteioDeQuestoes</c>): como não têm chave em
    /// arquivo nenhum, saem do sorteio na primeira execução. É o resultado pretendido — questão
    /// que não está num arquivo não tem como ser corrigida nem revisada. Para manter alguma
    /// delas, basta reescrevê-la num JSON.
    /// </remarks>
    private static void SincronizarAtivas(
        IEnumerable<Questao> conhecidas,
        Guid examId,
        IReadOnlySet<Guid> idsDosArquivos)
    {
        foreach (var questao in conhecidas.Where(q => q.ExamId == examId))
        {
            var estaNosArquivos = idsDosArquivos.Contains(questao.Id);

            if (!estaNosArquivos && questao.IsActive)
            {
                questao.Aposentar();
            }
            else if (estaNosArquivos && !questao.IsActive)
            {
                questao.Reativar();
            }
        }
    }

    private static void AtualizarQuestao(
        PrepHubDbContext db,
        Questao questao,
        Guid skillAreaId,
        QuestaoDeSeed seed,
        Domain.Enums.TipoDeQuestao tipo)
    {
        questao.Atualizar(skillAreaId, seed.Enunciado, tipo, seed.Explicacao, seed.Topico);

        for (var i = 0; i < seed.Opcoes.Count; i++)
        {
            var opcao = questao.OpcaoNaPosicao(i);
            if (opcao is null)
            {
                db.AnswerOptions.Add(new OpcaoDeResposta(
                    questao.Id,
                    seed.Opcoes[i].Texto,
                    seed.Opcoes[i].Correta,
                    i,
                    GuidDeterministico.DeOpcao(seed.Id, i),
                    seed.Opcoes[i].Alvo));
            }
            else
            {
                opcao.Atualizar(seed.Opcoes[i].Texto, seed.Opcoes[i].Correta, seed.Opcoes[i].Alvo);
            }
        }

        // Alternativas que sobraram de uma versão anterior do arquivo saem — sem isso, reduzir
        // uma questão de 5 para 4 alternativas deixaria a quinta viva e selecionável.
        var sobrando = questao.Options.Where(o => o.OrderIndex >= seed.Opcoes.Count).ToList();
        if (sobrando.Count > 0)
        {
            db.AnswerOptions.RemoveRange(sobrando);
        }
    }
}
