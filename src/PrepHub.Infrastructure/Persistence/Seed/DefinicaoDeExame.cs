namespace PrepHub.Infrastructure.Persistence.Seed;

/// <summary>
/// A definição de um exame como <b>dado</b>: parâmetros de entrega e os domínios do Skills
/// Measured, com os pesos oficiais. É a fonte da verdade do que o seeder escreve na tabela
/// <c>Exams</c> — e continua sendo depois da primeira execução, porque o seeder reaplica.
/// </summary>
/// <remarks>
/// Separar isto do <c>PrepHubDbSeeder</c> é o que torna "adicionar um exame" uma entrada numa
/// lista em vez de uma edição de algoritmo. Enquanto o seeder tinha uma constante
/// <c>CodigoDoExame</c> e um array de áreas soltos, cada exame novo pedia uma segunda cópia do
/// mesmo código — e a segunda cópia é onde os dois caminhos começam a divergir sem ninguém notar.
/// </remarks>
/// <param name="Publicado">
/// <c>false</c> enquanto o banco de questões está sendo escrito. O exame é semeado e recebe
/// questões normalmente — o que permite exercitá-lo pelo seed e pelos testes desde a primeira
/// questão —, mas fica fora do catálogo e recusa novas tentativas até virar <c>true</c>.
/// </param>
/// <param name="Fornecedor">
/// Quem emite a certificação. Decide a escala da nota (1–1000 na Microsoft, 100–1000 na AWS), as
/// regras de formato que o validador aplica aos lotes e a tela em que a prova é entregue.
/// </param>
public sealed record DefinicaoDeExame(
    string Code,
    string Name,
    int TimeLimitMinutes,
    int PassingScorePercent,
    int TotalQuestions,
    IReadOnlyList<AreaDeExame> Areas,
    bool Publicado = true,
    Domain.Enums.FornecedorDoExame Fornecedor = Domain.Enums.FornecedorDoExame.Microsoft);

/// <summary>Um domínio do Skills Measured: slug estável, nome de UI e peso em pontos percentuais.</summary>
/// <param name="Key">
/// Slug que os arquivos de questões referenciam (ex.: <c>conceitos-de-nuvem</c>). É contrato com
/// os JSONs — renomear aqui órfãria o lote que o usa.
/// </param>
/// <param name="WeightPercent">
/// Peso do domínio na prova. O outline oficial publica faixas ("25–30%"); usamos o ponto médio,
/// que é o que mantém a soma perto de 100 sem inventar precisão que a Microsoft não dá.
/// </param>
public sealed record AreaDeExame(string Key, string Name, decimal WeightPercent);
