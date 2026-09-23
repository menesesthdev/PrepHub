namespace PrepHub.Infrastructure.Persistence.Seed;

/// <summary>
/// Um lote de questões vindo de um arquivo JSON embutido. O formato é o descrito em
/// <c>docs/formato-questoes.md</c> — este tipo é só a forma desserializada dele.
/// </summary>
public sealed record ArquivoDeQuestoes
{
    /// <summary>Nome do recurso de origem, usado nas mensagens de validação.</summary>
    public string Origem { get; init; } = string.Empty;

    public string ExameCode { get; init; } = string.Empty;

    /// <summary>Slug da área de habilidade (ex.: "conceitos-de-nuvem").</summary>
    public string Area { get; init; } = string.Empty;

    public IReadOnlyList<QuestaoDeSeed> Questoes { get; init; } = Array.Empty<QuestaoDeSeed>();
}

public sealed record QuestaoDeSeed
{
    /// <summary>Chave estável da questão — vira <c>ExternalId</c> e origem do Guid.</summary>
    public string Id { get; init; } = string.Empty;

    public string? Topico { get; init; }

    /// <summary>"EscolhaUnica", "EscolhaMultipla", "SimNao", "Associacao" ou "Ordenacao".</summary>
    public string Tipo { get; init; } = string.Empty;

    public string Enunciado { get; init; } = string.Empty;

    public string Explicacao { get; init; } = string.Empty;

    public IReadOnlyList<OpcaoDeSeed> Opcoes { get; init; } = Array.Empty<OpcaoDeSeed>();

    /// <summary>
    /// Gabarito de uma questão <c>Associacao</c>: cada entrada é um alvo e o item que lhe
    /// corresponde. Só o tipo Associacao usa este campo.
    /// </summary>
    /// <remarks>
    /// É a forma <b>escrita à mão</b>; o que vai para o banco é a expansão dela em pares
    /// candidatos, feita por <see cref="CatalogoDeQuestoesDeSeed"/>. Escrever os pares expandidos
    /// no JSON seria escrever 16 linhas para uma questão de 4 alvos, com o gabarito espalhado
    /// entre elas — ilegível de revisar, que é justamente o que os arquivos existem para permitir.
    ///
    /// ⚠️ A ordem das entradas é parte da chave dos Ids das alternativas (ver
    /// <see cref="GuidDeterministico.DeOpcao"/>): vale para ela a mesma regra das
    /// <see cref="Opcoes"/> — nunca reordenar numa questão já publicada.
    /// </remarks>
    public IReadOnlyList<AssociacaoDeSeed> Associacoes { get; init; } = Array.Empty<AssociacaoDeSeed>();

    /// <summary>
    /// Itens arrastáveis que não correspondem a alvo nenhum — os distratores do arrastar e soltar.
    /// Opcional.
    /// </summary>
    /// <remarks>
    /// Sem eles, uma questão com N alvos e N itens se resolve por eliminação: quem sabe três dos
    /// quatro acerta o quarto de graça. É o mesmo raciocínio da engenharia de distrator do resto do
    /// banco, aplicado ao painel de itens.
    /// </remarks>
    public IReadOnlyList<string> ItensExtras { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gabarito de uma questão <c>Ordenacao</c>: os passos certos, na ordem certa. Os passos que não
    /// entram em etapa nenhuma vão em <see cref="ItensExtras"/>.
    /// </summary>
    /// <remarks>
    /// O carregador transforma cada posição numa etapa ("Etapa 1", "Etapa 2"...) e expande em pares
    /// etapa × passo, exatamente como faz com <see cref="Associacoes"/>. ⚠️ Mesma regra de Id: a
    /// ordem da lista e dos extras é parte da chave das alternativas — nunca reordenar numa questão
    /// já publicada (corrigir a sequência de uma questão publicada é aposentá-la e escrever outra).
    /// </remarks>
    public IReadOnlyList<string> Sequencia { get; init; } = Array.Empty<string>();
}

/// <summary>Um alvo e o item que lhe corresponde, numa questão de arrastar e soltar.</summary>
public sealed record AssociacaoDeSeed
{
    /// <summary>O alvo — a coluna da direita, onde o item é solto.</summary>
    public string Alvo { get; init; } = string.Empty;

    /// <summary>O item arrastável que responde a este alvo.</summary>
    public string Item { get; init; } = string.Empty;
}

public sealed record OpcaoDeSeed
{
    public string Texto { get; init; } = string.Empty;

    public bool Correta { get; init; }

    /// <summary>
    /// Alvo do par candidato nas questões de arrastar e soltar; nulo nos demais tipos. Não é
    /// escrito à mão — vem da expansão de <see cref="QuestaoDeSeed.Associacoes"/>.
    /// </summary>
    public string? Alvo { get; init; }
}
