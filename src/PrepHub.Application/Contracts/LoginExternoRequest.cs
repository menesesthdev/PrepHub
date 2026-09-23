using PrepHub.Domain.Enums;

namespace PrepHub.Application.Contracts;

/// <summary>
/// Perfil recebido do provedor externo após o callback do OAuth. É o formato comum para
/// onde Google, LinkedIn e GitHub convergem: todos entregam identificador, nome e foto,
/// e o e-mail pode faltar (o GitHub permite mantê-lo privado).
/// </summary>
public sealed record LoginExternoRequest(
    ProvedorDeLogin Provider,
    string ProviderKey,
    string Name,
    string? Email,
    string? AvatarUrl);

/// <summary>Usuário autenticado, como a UI precisa exibi-lo.</summary>
public sealed record UsuarioDto(
    Guid Id,
    string Name,
    string? Email,
    string? AvatarUrl,
    ProvedorDeLogin Provider);
