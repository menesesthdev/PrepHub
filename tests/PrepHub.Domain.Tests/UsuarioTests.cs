using PrepHub.Domain.Autenticacao;
using PrepHub.Domain.Entidades;
using PrepHub.Domain.Enums;

namespace PrepHub.Domain.Tests;

public class UsuarioTests
{
    private static readonly DateTime Agora = new(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ContaLocal_UsaEmailNormalizadoComoChaveNatural()
    {
        var usuario = Usuario.CriarComSenha("Ana", " Ana@Example.COM ", "hash", Agora);

        Assert.Equal(ProvedorDeLogin.Local, usuario.Provider);
        Assert.Equal("ana@example.com", usuario.ProviderKey);
        Assert.Equal("ana@example.com", usuario.Email);
        Assert.True(usuario.EhContaLocal);
    }

    [Fact]
    public void ContaLocal_ExigeHashDeSenha()
    {
        Assert.Throws<ArgumentException>(
            () => Usuario.CriarComSenha("Ana", "ana@example.com", "   ", Agora));
    }

    // O construtor externo não pode ser um atalho para criar conta Local sem senha — ela
    // ficaria com PasswordHash nulo e o login por senha nunca funcionaria para ela.
    [Fact]
    public void ConstrutorExterno_RecusaProvedorLocal()
    {
        Assert.Throws<ArgumentException>(
            () => new Usuario(ProvedorDeLogin.Local, "ana@example.com", "Ana", null, null, Agora));
    }

    [Fact]
    public void ContaSocial_NaoTemHashDeSenha()
    {
        var usuario = new Usuario(ProvedorDeLogin.Google, "g-1", "Ana", "ana@example.com", null, Agora);

        Assert.Null(usuario.PasswordHash);
        Assert.False(usuario.EhContaLocal);
    }

    [Fact]
    public void RegistrarLoginLocal_CarimbaOAcessoSemMexerNoPerfil()
    {
        var usuario = Usuario.CriarComSenha("Ana", "ana@example.com", "hash", Agora);

        usuario.RegistrarLoginLocal(Agora.AddDays(5));

        Assert.Equal(Agora.AddDays(5), usuario.LastLoginAt);
        Assert.Equal(Agora, usuario.CreatedAt);
        Assert.Equal("Ana", usuario.Name);
    }

    // ---- Bloqueio por tentativas -----------------------------------------

    private static Usuario ContaLocal() => Usuario.CriarComSenha("Ana", "ana@example.com", "hash", Agora);

    [Fact]
    public void RegistrarFalhaDeLogin_SoBloqueiaNoLimite()
    {
        var usuario = ContaLocal();

        for (var i = 0; i < PoliticaDeTentativasDeLogin.MaximoDeFalhas - 1; i++)
        {
            usuario.RegistrarFalhaDeLogin(Agora);
        }

        Assert.False(usuario.EstaBloqueada(Agora));

        usuario.RegistrarFalhaDeLogin(Agora);

        Assert.True(usuario.EstaBloqueada(Agora));
        Assert.Equal(Agora.Add(PoliticaDeTentativasDeLogin.DuracaoDoBloqueio), usuario.LockoutEndsAt);
    }

    [Fact]
    public void Bloqueio_ExpiraSozinhoNoFimDaJanela()
    {
        var usuario = ContaLocal();
        for (var i = 0; i < PoliticaDeTentativasDeLogin.MaximoDeFalhas; i++)
        {
            usuario.RegistrarFalhaDeLogin(Agora);
        }

        Assert.True(usuario.EstaBloqueada(Agora));
        Assert.False(usuario.EstaBloqueada(Agora.Add(PoliticaDeTentativasDeLogin.DuracaoDoBloqueio)));
    }

    // Depois do bloqueio o contador volta a zero; senão a primeira falha após a janela
    // bloquearia de novo na hora.
    [Fact]
    public void Bloqueio_ZeraOContadorParaNaoRebloquearNaFalhaSeguinte()
    {
        var usuario = ContaLocal();
        for (var i = 0; i < PoliticaDeTentativasDeLogin.MaximoDeFalhas; i++)
        {
            usuario.RegistrarFalhaDeLogin(Agora);
        }

        var depois = Agora.Add(PoliticaDeTentativasDeLogin.DuracaoDoBloqueio).AddMinutes(1);
        usuario.RegistrarFalhaDeLogin(depois);

        Assert.False(usuario.EstaBloqueada(depois));
        Assert.Equal(1, usuario.FailedLoginAttempts);
    }

    [Fact]
    public void LoginBemSucedido_LimpaFalhasEBloqueio()
    {
        var usuario = ContaLocal();
        for (var i = 0; i < PoliticaDeTentativasDeLogin.MaximoDeFalhas; i++)
        {
            usuario.RegistrarFalhaDeLogin(Agora);
        }

        usuario.RegistrarLoginLocal(Agora.AddHours(1));

        Assert.Equal(0, usuario.FailedLoginAttempts);
        Assert.Null(usuario.LockoutEndsAt);
    }

    // ---- Redefinição de senha --------------------------------------------

    [Fact]
    public void DefinirNovaSenha_TrocaOHashELiberaBloqueio()
    {
        var usuario = ContaLocal();
        for (var i = 0; i < PoliticaDeTentativasDeLogin.MaximoDeFalhas; i++)
        {
            usuario.RegistrarFalhaDeLogin(Agora);
        }

        usuario.DefinirNovaSenha("hash-novo", Agora.AddHours(2));

        Assert.Equal("hash-novo", usuario.PasswordHash);
        Assert.False(usuario.EstaBloqueada(Agora.AddHours(2)));
        Assert.Equal(0, usuario.FailedLoginAttempts);
    }

    // Definir senha numa conta social a converteria em local pela porta de trás.
    [Fact]
    public void DefinirNovaSenha_EmContaSocial_Recusa()
    {
        var social = new Usuario(ProvedorDeLogin.Google, "g-1", "Ana", "ana@example.com", null, Agora);

        Assert.Throws<InvalidOperationException>(() => social.DefinirNovaSenha("hash", Agora));
    }
}
