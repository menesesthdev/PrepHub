using PrepHub.Domain.Enums;

namespace PrepHub.Infrastructure.Observabilidade;

/// <summary>
/// Foto dos números que só o banco sabe responder — o "estoque" do produto, em oposição aos
/// eventos que os casos de uso contam conforme acontecem.
/// </summary>
/// <remarks>
/// A distinção importa na hora de consultar: evento vira contador (e se pergunta por taxa,
/// <c>rate()</c>), estoque vira medidor (e se pergunta pelo valor). Contador também zera quando o
/// processo reinicia — o total de contas cadastradas, que precisa sobreviver a todo deploy, só
/// pode vir daqui.
/// </remarks>
public sealed record RetratoDoBanco(
    DateTime ColetadoEm,
    IReadOnlyList<ContagemPorProvedor> UsuariosPorProvedor,
    IReadOnlyList<ContagemPorJanela> UsuariosAtivos,
    int ProvasEmAndamento,
    IReadOnlyList<ContagemDeProvasRealizadas> ProvasRealizadas);

/// <summary>Quantas contas existem em cada caminho de entrada.</summary>
public sealed record ContagemPorProvedor(ProvedorDeLogin Provedor, int Total);

/// <summary>
/// Quantas pessoas acessaram dentro de uma janela recente. É a "movimentação": cadastro é
/// número que só sobe, e sozinho não diz se alguém ainda usa o simulado.
/// </summary>
public sealed record ContagemPorJanela(string Janela, int Total);

/// <summary>Provas já encerradas, por exame e desfecho.</summary>
public sealed record ContagemDeProvasRealizadas(string CodigoDoExame, bool Aprovado, int Total);
