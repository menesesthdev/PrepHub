using PrepHub.Domain.Autenticacao;
using PrepHub.Domain.Common;

namespace PrepHub.Domain.Entidades;

/// <summary>
/// Prova temporária de que o endereço informado no cadastro existe e é de quem cadastrou.
/// Criada junto com a conta local e consumida quando a pessoa abre o link do e-mail.
/// </summary>
/// <remarks>
/// <para>
/// Mesma disciplina do <see cref="TokenDeRedefinicaoDeSenha"/>: guarda o <see cref="TokenHash"/>,
/// nunca o token. Quem lê o banco (backup, dump, log de query) vê hashes e não consegue confirmar
/// conta de ninguém.
/// </para>
/// <para>
/// É uma entidade separada, e não o mesmo tipo com um campo "finalidade", de propósito. Os dois
/// tokens têm prazos diferentes (<see cref="PoliticaDeConfirmacaoDeEmail"/> contra
/// <see cref="PoliticaDeRedefinicaoDeSenha"/>) e poderes diferentes — um confirma um endereço, o
/// outro troca uma senha. Compartilhar a tabela criaria a chance de um link de confirmação ser
/// aceito onde se espera um de redefinição, que é exatamente a confusão que não pode existir; a
/// duplicação de umas poucas linhas é o preço de tornar isso impossível pelo tipo.
/// </para>
/// </remarks>
public class TokenDeConfirmacaoDeEmail : Entity
{
    // Construtor exigido pelo EF Core (materialização).
    private TokenDeConfirmacaoDeEmail()
    {
    }

    public TokenDeConfirmacaoDeEmail(Guid userId, string tokenHash, DateTime createdAt, Guid? id = null)
        : base(id ?? Guid.NewGuid())
    {
        UserId = Guard.NotEmpty(userId, nameof(userId));
        TokenHash = Guard.NotNullOrWhiteSpace(tokenHash, nameof(tokenHash));
        CreatedAt = createdAt;
        ExpiresAt = createdAt.Add(PoliticaDeConfirmacaoDeEmail.Validade);
    }

    public Guid UserId { get; private set; }

    /// <summary>Hash determinístico do token — é por ele que a busca acontece.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    public DateTime CreatedAt { get; private set; }

    public DateTime ExpiresAt { get; private set; }

    /// <summary>Quando o link foi aberto. <c>null</c> enquanto não foi.</summary>
    public DateTime? UsedAt { get; private set; }

    public bool EstaUtilizavel(DateTime agora) => UsedAt is null && agora < ExpiresAt;

    /// <summary>
    /// Marca o link como gasto. Recusa consumir duas vezes — quem chama já deve ter conferido
    /// <see cref="EstaUtilizavel"/>, e chegar aqui com um token usado é erro de fluxo, não
    /// entrada inválida do usuário.
    /// </summary>
    public void Consumir(DateTime agora)
    {
        if (UsedAt is not null)
        {
            throw new InvalidOperationException("Token de confirmação já foi usado.");
        }

        UsedAt = agora;
    }

    /// <summary>
    /// Invalida sem ter sido usado — é o que acontece com o link anterior quando a pessoa pede
    /// um reenvio. Dois links vivos ao mesmo tempo não servem para nada e só ampliam a janela.
    /// </summary>
    public void Invalidar(DateTime agora) => UsedAt ??= agora;
}
