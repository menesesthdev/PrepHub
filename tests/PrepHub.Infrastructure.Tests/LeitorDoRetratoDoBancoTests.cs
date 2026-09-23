using PrepHub.Application.Abstractions;
using PrepHub.Domain.Entidades;
using PrepHub.Domain.Enums;
using PrepHub.Infrastructure.Observabilidade;
using PrepHub.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace PrepHub.Infrastructure.Tests;

/// <summary>
/// O leitor que alimenta os medidores, contra SQLite de verdade.
/// </summary>
/// <remarks>
/// SQLite real, e não um dublê, porque o risco que este teste cobre é de TRADUÇÃO: um
/// <c>GroupBy</c> ou um <c>Join</c> que o provider não sabe converter compila normalmente e só
/// estoura em tempo de execução. E estoura dentro de um serviço em segundo plano, cuja exceção é
/// engolida de propósito para não derrubar a aplicação — ou seja, a falha apareceria como painel
/// vazio e uma linha de log, não como teste vermelho.
/// </remarks>
public sealed class LeitorDoRetratoDoBancoTests : IDisposable
{
    private static readonly DateTime Agora = new(2026, 5, 10, 12, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _conexao;
    private readonly RelogioFixo _relogio = new(Agora);

    public LeitorDoRetratoDoBancoTests()
    {
        _conexao = new SqliteConnection("DataSource=:memory:");
        _conexao.Open();

        using var ctx = NovoContexto();
        ctx.Database.EnsureCreated();
    }

    private PrepHubDbContext NovoContexto()
        => new(new DbContextOptionsBuilder<PrepHubDbContext>().UseSqlite(_conexao).Options);

    public void Dispose() => _conexao.Dispose();

    private Task<RetratoDoBanco> LerAsync()
    {
        var ctx = NovoContexto();
        return new LeitorDoRetratoDoBanco(ctx, _relogio).LerAsync();
    }

    private Guid AdicionarUsuario(ProvedorDeLogin provedor, DateTime ultimoAcesso)
    {
        using var ctx = NovoContexto();

        var usuario = provedor == ProvedorDeLogin.Local
            ? Usuario.CriarComSenha($"Fulano {Guid.NewGuid():N}", $"{Guid.NewGuid():N}@example.com", "hash", ultimoAcesso)
            : new Usuario(provedor, Guid.NewGuid().ToString(), "Fulano", null, null, ultimoAcesso);

        ctx.Users.Add(usuario);
        ctx.SaveChanges();
        return usuario.Id;
    }

    private Guid AdicionarExame(string codigo)
    {
        using var ctx = NovoContexto();
        var exame = new Exame(codigo, "Exame de teste", 45, 70, 2);
        ctx.Exams.Add(exame);
        ctx.SaveChanges();
        return exame.Id;
    }

    private void AdicionarTentativa(Guid exameId, Guid usuarioId, bool? aprovada)
    {
        using var ctx = NovoContexto();
        var tentativa = new TentativaDeProva(exameId, usuarioId, Agora.AddHours(-1));

        if (aprovada is { } passou)
        {
            tentativa.Concluir(passou ? 80m : 40m, passou, Agora.AddMinutes(-20));
        }

        ctx.ExamAttempts.Add(tentativa);
        ctx.SaveChanges();
    }

    [Fact]
    public async Task BancoVazio_DevolveRetratoZerado()
    {
        var retrato = await LerAsync();

        Assert.Empty(retrato.UsuariosPorProvedor);
        Assert.Empty(retrato.ProvasRealizadas);
        Assert.Equal(0, retrato.ProvasEmAndamento);

        // As janelas SEMPRE aparecem, mesmo zeradas: elas são a régua do painel, não um dado que
        // existe só quando alguém acessou.
        Assert.Equal(3, retrato.UsuariosAtivos.Count);
        Assert.All(retrato.UsuariosAtivos, c => Assert.Equal(0, c.Total));
    }

    [Fact]
    public async Task ContaUsuariosPorProvedor()
    {
        AdicionarUsuario(ProvedorDeLogin.Local, Agora);
        AdicionarUsuario(ProvedorDeLogin.Local, Agora);
        AdicionarUsuario(ProvedorDeLogin.Google, Agora);

        var retrato = await LerAsync();

        var porProvedor = retrato.UsuariosPorProvedor.ToDictionary(c => c.Provedor, c => c.Total);
        Assert.Equal(2, porProvedor[ProvedorDeLogin.Local]);
        Assert.Equal(1, porProvedor[ProvedorDeLogin.Google]);
        Assert.False(porProvedor.ContainsKey(ProvedorDeLogin.GitHub));
    }

    /// <summary>
    /// As janelas são encaixadas: quem acessou hoje conta nas três. É o que permite ler retenção
    /// comparando 24h contra 30d — se cada janela excluísse a anterior, as três somariam o total
    /// e nenhuma responderia "quanta gente ainda volta".
    /// </summary>
    [Fact]
    public async Task JanelasDeAtividadeSaoEncaixadas()
    {
        AdicionarUsuario(ProvedorDeLogin.Local, Agora.AddHours(-2));
        AdicionarUsuario(ProvedorDeLogin.Google, Agora.AddDays(-3));
        AdicionarUsuario(ProvedorDeLogin.GitHub, Agora.AddDays(-20));
        AdicionarUsuario(ProvedorDeLogin.LinkedIn, Agora.AddDays(-200));

        var retrato = await LerAsync();

        var porJanela = retrato.UsuariosAtivos.ToDictionary(c => c.Janela, c => c.Total);
        Assert.Equal(1, porJanela["24h"]);
        Assert.Equal(2, porJanela["7d"]);
        Assert.Equal(3, porJanela["30d"]);
    }

    [Fact]
    public async Task SeparaProvasEmAndamentoDasConcluidas()
    {
        var exame = AdicionarExame("AZ-900");
        var usuario = AdicionarUsuario(ProvedorDeLogin.Local, Agora);

        AdicionarTentativa(exame, usuario, aprovada: null);
        AdicionarTentativa(exame, usuario, aprovada: null);
        AdicionarTentativa(exame, usuario, aprovada: true);
        AdicionarTentativa(exame, usuario, aprovada: false);
        AdicionarTentativa(exame, usuario, aprovada: false);

        var retrato = await LerAsync();

        Assert.Equal(2, retrato.ProvasEmAndamento);

        var porResultado = retrato.ProvasRealizadas.ToDictionary(c => c.Aprovado, c => c.Total);
        Assert.Equal(1, porResultado[true]);
        Assert.Equal(2, porResultado[false]);
        Assert.All(retrato.ProvasRealizadas, c => Assert.Equal("AZ-900", c.CodigoDoExame));
    }

    /// <summary>
    /// O código do exame vem do JOIN com a tabela de exames, e não do id da tentativa: é ele que
    /// vira rótulo no Prometheus. Com mais de um exame no banco (AZ-104, AI-900 no roadmap), um
    /// JOIN errado misturaria as provas de todos num número só.
    /// </summary>
    [Fact]
    public async Task ProvasSaoAgrupadasPorCodigoDoExame()
    {
        var az900 = AdicionarExame("AZ-900");
        var az104 = AdicionarExame("AZ-104");
        var usuario = AdicionarUsuario(ProvedorDeLogin.Local, Agora);

        AdicionarTentativa(az900, usuario, aprovada: true);
        AdicionarTentativa(az104, usuario, aprovada: true);
        AdicionarTentativa(az104, usuario, aprovada: true);

        var retrato = await LerAsync();

        var porExame = retrato.ProvasRealizadas.ToDictionary(c => c.CodigoDoExame, c => c.Total);
        Assert.Equal(1, porExame["AZ-900"]);
        Assert.Equal(2, porExame["AZ-104"]);
    }

    [Fact]
    public async Task CarimbaOInstanteDaColeta()
    {
        var retrato = await LerAsync();

        Assert.Equal(Agora, retrato.ColetadoEm);
    }

    private sealed class RelogioFixo(DateTime agora) : IClock
    {
        public DateTime UtcNow { get; } = agora;
    }
}
