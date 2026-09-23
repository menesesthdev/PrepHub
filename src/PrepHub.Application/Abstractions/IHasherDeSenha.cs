namespace PrepHub.Application.Abstractions;

/// <summary>
/// Deriva e verifica hash de senha. É abstração porque algoritmo, custo e formato do hash são
/// decisão de infraestrutura que muda com o tempo (fator de trabalho sobe junto com o hardware),
/// e nem Application nem Domain devem depender de biblioteca de criptografia.
/// </summary>
public interface IHasherDeSenha
{
    /// <summary>Gera o hash a ser persistido, incluindo salt e parâmetros embutidos.</summary>
    string Hash(string senha);

    /// <summary>
    /// Confere a senha contra o hash guardado. Devolve <c>false</c> em vez de lançar quando o
    /// hash é ilegível — hash corrompido não deve virar erro 500 na tela de login.
    /// </summary>
    bool Verificar(string senha, string hash);
}
