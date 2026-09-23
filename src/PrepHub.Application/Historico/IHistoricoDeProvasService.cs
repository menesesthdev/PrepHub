using PrepHub.Application.Contracts;

namespace PrepHub.Application.Historico;

public interface IHistoricoDeProvasService
{
    /// <summary>
    /// Histórico de quem está logado. O usuário vem de <c>IUsuarioAtual</c> e não por parâmetro:
    /// é a mesma disciplina do <c>SessaoDeProvaService</c> — sem id de usuário na assinatura,
    /// nenhum controller consegue pedir o histórico de outra pessoa.
    /// </summary>
    Task<HistoricoDoUsuarioDto> ObterHistoricoAsync(CancellationToken cancellationToken = default);
}
