using System.Diagnostics.Metrics;
using PrepHub.Application.Abstractions;
using PrepHub.Domain.Enums;

namespace PrepHub.Infrastructure.Observabilidade;

/// <summary>
/// Onde as métricas do PrepHub nascem. Usa o <see cref="Meter"/> da BCL
/// (<c>System.Diagnostics.Metrics</c>) e não a API de nenhum vendor: quem exporta para o
/// Prometheus é o OpenTelemetry, montado no Web, e trocar de destino não toca nesta classe.
/// </summary>
/// <remarks>
/// <para>
/// Os nomes usam ponto como separador porque é a convenção do OpenTelemetry; o exportador os
/// traduz para o formato do Prometheus na saída — <c>prephub.contas.criadas</c> vira
/// <c>prephub_contas_criadas_total</c>, <c>prephub.provas.duracao</c> (unidade <c>s</c>) vira
/// <c>prephub_provas_duracao_seconds</c>. As consultas dos dashboards usam a forma traduzida.
/// </para>
/// <para>
/// Singleton, e é obrigatório que seja: o <c>Meter</c> é o objeto que o OpenTelemetry assina no
/// startup. Um por requisição criaria instrumentos que ninguém está ouvindo, e a métrica
/// simplesmente não apareceria — sem erro nenhum para denunciar.
/// </para>
/// </remarks>
public sealed class MetricasDoPrepHub : IMetricasDeNegocio, IDisposable
{
    /// <summary>
    /// Nome do meter. É por ele que o Web assina a coleta (<c>AddMeter</c>) — mudar aqui sem
    /// mudar lá apaga todos os painéis de produto de uma vez.
    /// </summary>
    public const string NomeDoMeter = "PrepHub";

    private readonly Meter _meter;
    private readonly IClock _clock;

    private readonly Counter<long> _contasCriadas;
    private readonly Counter<long> _cadastrosRecusados;
    private readonly Counter<long> _logins;
    private readonly Counter<long> _redefinicoesDeSenha;
    private readonly Counter<long> _confirmacoesDeEmail;
    private readonly Counter<long> _provasIniciadas;
    private readonly Counter<long> _provasConcluidas;
    private readonly Histogram<int> _notas;
    private readonly Histogram<double> _duracoes;

    // Escrito pelo coletor em segundo plano e lido pelos medidores no momento do scrape — duas
    // threads diferentes, sempre. A referência é trocada inteira (o record é imutável), então
    // nunca se enxerga um retrato pela metade; volatile garante que a troca fique visível.
    private volatile RetratoDoBanco? _retrato;

    public MetricasDoPrepHub(IClock clock)
    {
        _clock = clock;

        // O `scope` é esta instância. Em produção não muda nada — quem assina a coleta
        // (AddMeter no Web) e o Prometheus enxergam só o NOME. Ele existe para o teste:
        // MetricCollector filtra por escopo, e sem isso duas instâncias desta classe rodando em
        // paralelo (xUnit executa classes de teste concorrentemente) alimentariam o mesmo
        // coletor — um teste vendo a medição do outro, com falha intermitente e sem explicação.
        _meter = new Meter(NomeDoMeter, version: null, tags: null, scope: this);

        _contasCriadas = _meter.CreateCounter<long>(
            "prephub.contas.criadas",
            unit: "{conta}",
            description: "Contas criadas, por caminho de entrada.");

        _cadastrosRecusados = _meter.CreateCounter<long>(
            "prephub.cadastros.recusados",
            unit: "{cadastro}",
            description: "Cadastros que não viraram conta, por motivo.");

        _logins = _meter.CreateCounter<long>(
            "prephub.logins",
            unit: "{tentativa}",
            description: "Tentativas de login, por provedor e desfecho.");

        _redefinicoesDeSenha = _meter.CreateCounter<long>(
            "prephub.redefinicoes.senha",
            unit: "{evento}",
            description: "Passos do fluxo de redefinição de senha.");

        _confirmacoesDeEmail = _meter.CreateCounter<long>(
            "prephub.confirmacoes.email",
            unit: "{evento}",
            description: "Passos da confirmação de e-mail. A diferença entre link_emitido e concluida é quanta gente cadastra e nunca recebe (ou nunca abre) a mensagem.");

        _provasIniciadas = _meter.CreateCounter<long>(
            "prephub.provas.iniciadas",
            unit: "{prova}",
            description: "Simulados iniciados, por exame.");

        _provasConcluidas = _meter.CreateCounter<long>(
            "prephub.provas.concluidas",
            unit: "{prova}",
            description: "Simulados encerrados, por exame, desfecho e motivo do encerramento.");

        _notas = _meter.CreateHistogram<int>(
            "prephub.provas.nota",
            unit: "{ponto}",
            description: "Nota final na escala 1–1000 (corte em 700).");

        _duracoes = _meter.CreateHistogram<double>(
            "prephub.provas.duracao",
            unit: "s",
            description: "Tempo entre o início e o encerramento da prova.");

        // --- Medidores alimentados pelo retrato do banco -----------------------------------
        // São observáveis (o callback roda no scrape) porque representam estado, não evento:
        // não há "momento" em que o total de contas muda do ponto de vista de quem coleta.

        _meter.CreateObservableGauge(
            "prephub.usuarios.cadastrados",
            ObservarUsuariosCadastrados,
            unit: "{usuario}",
            description: "Contas existentes, por provedor.");

        _meter.CreateObservableGauge(
            "prephub.usuarios.ativos",
            ObservarUsuariosAtivos,
            unit: "{usuario}",
            description: "Contas com acesso dentro de cada janela recente.");

        _meter.CreateObservableGauge(
            "prephub.provas.em_andamento",
            ObservarProvasEmAndamento,
            unit: "{prova}",
            description: "Tentativas abertas, ainda sem encerramento gravado.");

        _meter.CreateObservableGauge(
            "prephub.provas.realizadas",
            ObservarProvasRealizadas,
            unit: "{prova}",
            description: "Total histórico de provas encerradas, por exame e desfecho. Sobrevive a reinício, ao contrário do contador.");

        _meter.CreateObservableGauge(
            "prephub.coleta.idade",
            ObservarIdadeDoRetrato,
            unit: "s",
            description: "Há quanto tempo o retrato do banco foi tirado. Crescer sem parar significa coletor parado — e medidores congelados mentindo com cara de verdade.");
    }

    /// <summary>Publica um retrato novo. Chamado pelo coletor em segundo plano.</summary>
    public void AtualizarRetrato(RetratoDoBanco retrato) => _retrato = retrato;

    public void ContaCriada(ProvedorDeLogin provedor)
        => _contasCriadas.Add(1, new KeyValuePair<string, object?>("provedor", Rotulo(provedor)));

    public void CadastroRecusado(MotivoDeRecusaDeCadastro motivo)
        => _cadastrosRecusados.Add(1, new KeyValuePair<string, object?>("motivo", Rotulo(motivo)));

    public void LoginRegistrado(ProvedorDeLogin provedor, ResultadoDeLogin resultado)
        => _logins.Add(
            1,
            new KeyValuePair<string, object?>("provedor", Rotulo(provedor)),
            new KeyValuePair<string, object?>("resultado", Rotulo(resultado)));

    public void RedefinicaoDeSenha(EtapaDeRedefinicao etapa)
        => _redefinicoesDeSenha.Add(1, new KeyValuePair<string, object?>("etapa", Rotulo(etapa)));

    public void ConfirmacaoDeEmail(EtapaDeConfirmacaoDeEmail etapa)
        => _confirmacoesDeEmail.Add(1, new KeyValuePair<string, object?>("etapa", Rotulo(etapa)));

    public void ProvaIniciada(string codigoDoExame)
        => _provasIniciadas.Add(1, new KeyValuePair<string, object?>("exame", codigoDoExame));

    public void ProvaConcluida(
        string codigoDoExame,
        bool aprovado,
        int notaEscalada,
        TimeSpan duracao,
        MotivoDeEncerramento motivo)
    {
        var exame = new KeyValuePair<string, object?>("exame", codigoDoExame);
        var resultado = new KeyValuePair<string, object?>("resultado", aprovado ? "aprovado" : "reprovado");

        _provasConcluidas.Add(1, exame, resultado, new KeyValuePair<string, object?>("motivo", Rotulo(motivo)));
        _notas.Record(notaEscalada, exame, resultado);

        // Duração negativa não existe, mas relógio ajustado para trás produz uma — e um valor
        // negativo num histograma contamina a soma para sempre, sem jeito de tirar depois.
        _duracoes.Record(Math.Max(0d, duracao.TotalSeconds), exame);
    }

    // ---- Leitura dos medidores -------------------------------------------------------------
    // Todos devolvem sequência VAZIA enquanto não houve coleta. É de propósito: uma métrica
    // ausente aparece como lacuna no gráfico, enquanto um zero publicado seria lido como "não há
    // nenhum usuário cadastrado" — resposta errada dita com confiança.

    private IEnumerable<Measurement<long>> ObservarUsuariosCadastrados()
        => _retrato is not { } retrato
            ? Array.Empty<Measurement<long>>()
            : retrato.UsuariosPorProvedor.Select(c => new Measurement<long>(
                c.Total,
                new KeyValuePair<string, object?>("provedor", Rotulo(c.Provedor))));

    private IEnumerable<Measurement<long>> ObservarUsuariosAtivos()
        => _retrato is not { } retrato
            ? Array.Empty<Measurement<long>>()
            : retrato.UsuariosAtivos.Select(c => new Measurement<long>(
                c.Total,
                new KeyValuePair<string, object?>("janela", c.Janela)));

    private IEnumerable<Measurement<long>> ObservarProvasEmAndamento()
        => _retrato is not { } retrato
            ? Array.Empty<Measurement<long>>()
            : new[] { new Measurement<long>(retrato.ProvasEmAndamento) };

    private IEnumerable<Measurement<long>> ObservarProvasRealizadas()
        => _retrato is not { } retrato
            ? Array.Empty<Measurement<long>>()
            : retrato.ProvasRealizadas.Select(c => new Measurement<long>(
                c.Total,
                new KeyValuePair<string, object?>("exame", c.CodigoDoExame),
                new KeyValuePair<string, object?>("resultado", c.Aprovado ? "aprovado" : "reprovado")));

    private IEnumerable<Measurement<double>> ObservarIdadeDoRetrato()
        => _retrato is not { } retrato
            ? Array.Empty<Measurement<double>>()
            : new[] { new Measurement<double>(Math.Max(0d, (_clock.UtcNow - retrato.ColetadoEm).TotalSeconds)) };

    /// <summary>
    /// O provedor vira rótulo em minúsculas, SEM separar as palavras: <c>LinkedIn</c> é
    /// <c>linkedin</c> e <c>GitHub</c> é <c>github</c>.
    /// </summary>
    /// <remarks>
    /// Sobrecarga própria porque a regra geral (<see cref="Rotulo{T}"/>) produziria
    /// <c>linked_in</c> e <c>git_hub</c>: ela lê a maiúscula do meio como início de outra palavra,
    /// o que vale para <c>SenhaIncorreta</c> e não vale para nome de marca, que é uma palavra só.
    /// </remarks>
    private static string Rotulo(ProvedorDeLogin provedor) => provedor.ToString().ToLowerInvariant();

    /// <summary>
    /// Converte o enum em rótulo no estilo do Prometheus: <c>SenhaIncorreta</c> vira
    /// <c>senha_incorreta</c>.
    /// </summary>
    /// <remarks>
    /// Passar o enum direto para o instrumento funcionaria, mas o valor sairia com a grafia exata
    /// do símbolo C#. Além de destoar de tudo o mais que aparece no <c>/metrics</c>, prenderia as
    /// consultas dos dashboards ao nome do membro — e um <c>ToLowerInvariant</c> sozinho produzia
    /// <c>containexistente</c>, que ninguém lê à primeira vista numa legenda de gráfico.
    /// </remarks>
    private static string Rotulo<T>(T valor) where T : struct, Enum
    {
        var nome = valor.ToString();
        var texto = new System.Text.StringBuilder(nome.Length + 4);

        for (var i = 0; i < nome.Length; i++)
        {
            if (i > 0 && char.IsUpper(nome[i]))
            {
                texto.Append('_');
            }

            texto.Append(char.ToLowerInvariant(nome[i]));
        }

        return texto.ToString();
    }

    public void Dispose() => _meter.Dispose();
}
