using PrepHub.Domain.Entidades;
using PrepHub.Domain.Enums;

namespace PrepHub.Application.Abstractions;

public interface IUsuarioRepository
{
    /// <summary>Busca pelo par (provedor, chave externa) — a chave natural da identidade.</summary>
    Task<Usuario?> ObterPorProvedorAsync(
        ProvedorDeLogin provider,
        string providerKey,
        CancellationToken cancellationToken = default);

    /// <summary>Leitura only-read (sem rastreamento) — para exibir, não para alterar.</summary>
    Task<Usuario?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mesma busca, mas rastreada: quem vai ALTERAR o usuário precisa desta, senão a mudança
    /// se perde no SaveChanges sem erro nenhum.
    /// </summary>
    Task<Usuario?> ObterPorIdParaAtualizacaoAsync(Guid id, CancellationToken cancellationToken = default);

    Task AdicionarAsync(Usuario usuario, CancellationToken cancellationToken = default);

    // ---- Tokens de redefinição de senha ----------------------------------
    // Ficam neste repositório, e não em outro, porque o token não tem vida própria: é estado
    // do agregado Usuario, sempre criado e consumido junto com ele.

    Task AdicionarTokenDeRedefinicaoAsync(
        TokenDeRedefinicaoDeSenha token,
        CancellationToken cancellationToken = default);

    /// <summary>Busca pelo hash do token — o valor original nunca chega ao banco.</summary>
    Task<TokenDeRedefinicaoDeSenha?> ObterTokenDeRedefinicaoAsync(
        string tokenHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tokens ainda não usados de um usuário. Serve para invalidar os antigos ao emitir um
    /// novo e ao concluir a redefinição.
    /// </summary>
    Task<IReadOnlyList<TokenDeRedefinicaoDeSenha>> ObterTokensAtivosDoUsuarioAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    // ---- Tokens de confirmação de e-mail ---------------------------------
    // Mesma justificativa dos de redefinição: são estado do agregado Usuario, criados e
    // consumidos junto com ele, então não ganham repositório próprio.

    Task AdicionarTokenDeConfirmacaoAsync(
        TokenDeConfirmacaoDeEmail token,
        CancellationToken cancellationToken = default);

    /// <summary>Busca pelo hash do token — o valor original nunca chega ao banco.</summary>
    Task<TokenDeConfirmacaoDeEmail?> ObterTokenDeConfirmacaoAsync(
        string tokenHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Links de confirmação ainda não usados de um usuário, para invalidá-los quando um
    /// reenvio emitir outro.
    /// </summary>
    Task<IReadOnlyList<TokenDeConfirmacaoDeEmail>> ObterTokensDeConfirmacaoAtivosDoUsuarioAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
