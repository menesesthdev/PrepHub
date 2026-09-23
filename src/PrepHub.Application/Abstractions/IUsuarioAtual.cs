namespace PrepHub.Application.Abstractions;

/// <summary>
/// Quem está autenticado na requisição atual. Existe para que o serviço de sessão consiga
/// impor a posse da tentativa sem depender do chamador passar o id certo — o controller não
/// tem como "esquecer" de checar, porque não é ele quem checa.
/// </summary>
public interface IUsuarioAtual
{
    /// <summary>Id do usuário logado, ou <c>null</c> se a requisição é anônima.</summary>
    Guid? Id { get; }
}
