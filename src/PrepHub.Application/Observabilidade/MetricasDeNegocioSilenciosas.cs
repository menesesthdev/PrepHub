using PrepHub.Application.Abstractions;
using PrepHub.Domain.Enums;

namespace PrepHub.Application.Observabilidade;

/// <summary>
/// Implementação que não mede nada. Existe para que instrumentação seja opcional sem virar
/// <c>if (_metricas is not null)</c> espalhado pelos casos de uso — e para que um teste que não
/// se importa com métrica não precise montar um coletor de verdade.
/// </summary>
public sealed class MetricasDeNegocioSilenciosas : IMetricasDeNegocio
{
    public static readonly MetricasDeNegocioSilenciosas Instancia = new();

    public void ContaCriada(ProvedorDeLogin provedor)
    {
    }

    public void CadastroRecusado(MotivoDeRecusaDeCadastro motivo)
    {
    }

    public void LoginRegistrado(ProvedorDeLogin provedor, ResultadoDeLogin resultado)
    {
    }

    public void RedefinicaoDeSenha(EtapaDeRedefinicao etapa)
    {
    }

    public void ConfirmacaoDeEmail(EtapaDeConfirmacaoDeEmail etapa)
    {
    }

    public void ProvaIniciada(string codigoDoExame)
    {
    }

    public void ProvaConcluida(
        string codigoDoExame,
        bool aprovado,
        int notaEscalada,
        TimeSpan duracao,
        MotivoDeEncerramento motivo)
    {
    }
}
