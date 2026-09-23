namespace PrepHub.Domain.Autenticacao;

/// <summary>
/// Limite de tentativas de senha errada por conta, antes de a conta parar de aceitar login
/// por um tempo.
/// </summary>
/// <remarks>
/// É a segunda camada, não a primeira: quem segura ataque genérico é o limite por IP no Web
/// (um IP tentando muitas contas). Este aqui existe porque o limite por IP não segura ataque
/// distribuído contra UMA conta conhecida — cada máquina da botnet fica dentro da cota.
///
/// A janela é curta e o bloqueio expira sozinho, sem intervenção. Bloqueio longo (ou
/// permanente) transformaria a proteção em arma: qualquer pessoa trancaria a conta de outra
/// só errando a senha de propósito. Quinze minutos derrubam a taxa de tentativa em ordens de
/// grandeza e ainda deixam o dono entrar no mesmo dia.
/// </remarks>
public static class PoliticaDeTentativasDeLogin
{
    /// <summary>Falhas consecutivas toleradas antes do bloqueio.</summary>
    public const int MaximoDeFalhas = 5;

    public static readonly TimeSpan DuracaoDoBloqueio = TimeSpan.FromMinutes(15);
}
