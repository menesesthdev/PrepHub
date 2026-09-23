using PrepHub.Domain.Autenticacao;
using PrepHub.Domain.Common;

namespace PrepHub.Domain.Entidades;

/// <summary>
/// Permissão temporária para trocar a senha de uma conta local, criada quando a pessoa pede
/// "esqueci minha senha" e consumida quando a nova senha é definida.
/// </summary>
/// <remarks>
/// Guarda o <see cref="TokenHash"/>, nunca o token: o valor que viaja no link fica só no
/// e-mail da pessoa. Quem lê o banco (backup, dump, log de query) vê hashes e não consegue
/// redefinir senha de ninguém — é a mesma razão de não guardar senha em texto.
///
/// Uso único e com prazo. Marcar <see cref="UsedAt"/> em vez de apagar a linha deixa rastro
/// de quando o link foi usado, o que é o que se quer olhar se alguém reclamar de troca de
/// senha que não pediu.
/// </remarks>
public class TokenDeRedefinicaoDeSenha : Entity
{
    // Construtor exigido pelo EF Core (materialização).
    private TokenDeRedefinicaoDeSenha()
    {
    }

    public TokenDeRedefinicaoDeSenha(Guid userId, string tokenHash, DateTime createdAt, Guid? id = null)
        : base(id ?? Guid.NewGuid())
    {
        UserId = Guard.NotEmpty(userId, nameof(userId));
        TokenHash = Guard.NotNullOrWhiteSpace(tokenHash, nameof(tokenHash));
        CreatedAt = createdAt;
        ExpiresAt = createdAt.Add(PoliticaDeRedefinicaoDeSenha.Validade);
    }

    public Guid UserId { get; private set; }

    /// <summary>Hash determinístico do token — é por ele que a busca acontece.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    public DateTime CreatedAt { get; private set; }

    public DateTime ExpiresAt { get; private set; }

    /// <summary>Quando o token foi usado. <c>null</c> enquanto não foi.</summary>
    public DateTime? UsedAt { get; private set; }

    public bool EstaUtilizavel(DateTime agora) => UsedAt is null && agora < ExpiresAt;

    /// <summary>
    /// Marca o token como gasto. Recusa consumir duas vezes: sem isso, o mesmo link
    /// reenviado (ou vazado depois) trocaria a senha de novo.
    /// </summary>
    public void Consumir(DateTime agora)
    {
        if (UsedAt is not null)
        {
            throw new InvalidOperationException("Token de redefinição já foi usado.");
        }

        UsedAt = agora;
    }

    /// <summary>
    /// Invalida sem ter sido usado — é o que acontece com os tokens antigos quando um novo é
    /// pedido, ou com os irmãos do token que acabou de redefinir a senha.
    /// </summary>
    public void Invalidar(DateTime agora) => UsedAt ??= agora;
}
