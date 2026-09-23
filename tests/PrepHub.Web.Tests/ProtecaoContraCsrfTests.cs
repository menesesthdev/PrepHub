using System.Reflection;
using PrepHub.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace PrepHub.Web.Tests;

/// <summary>
/// Toda ação que muda estado por POST tem de validar o token antiforgery.
/// </summary>
/// <remarks>
/// <para>
/// É teste de convenção, e não de um endpoint específico, de propósito: o furo que ele cobre
/// nasceu de um POST esquecido — <c>/exam/{id}/finish</c> ficou sem o atributo desde antes de o
/// projeto ter autenticação, e o comentário que explicava a ausência envelheceu junto. Cobrir
/// só aquele endpoint deixaria o próximo passar igual.
/// </para>
/// <para>
/// O caso do <c>finish</c> mostra por que Content-Type não serve de defesa: a ação não recebe
/// corpo, então um formulário em site de terceiro bastava para encerrar — irreversivelmente — a
/// prova de quem estivesse logado.
/// </para>
/// </remarks>
public class ProtecaoContraCsrfTests
{
    /// <summary>Todos os controllers MVC da aplicação, achados pelo assembly e não listados à mão.</summary>
    private static IEnumerable<Type> Controllers => typeof(ExameController).Assembly
        .GetTypes()
        .Where(t => typeof(Controller).IsAssignableFrom(t) && !t.IsAbstract);

    private static IEnumerable<MethodInfo> AcoesDePost => Controllers
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        .Where(m => m.GetCustomAttributes<HttpPostAttribute>().Any());

    [Fact]
    public void TodaAcaoDePost_ValidaOTokenAntiforgery()
    {
        var desprotegidas = AcoesDePost
            .Where(m => !m.GetCustomAttributes<ValidateAntiForgeryTokenAttribute>().Any()
                        && m.DeclaringType!.GetCustomAttributes<ValidateAntiForgeryTokenAttribute>().Count() == 0)
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .OrderBy(nome => nome)
            .ToList();

        Assert.True(
            desprotegidas.Count == 0,
            "POST sem [ValidateAntiForgeryToken]: " + string.Join(", ", desprotegidas)
            + ". Se a ação é chamada por fetch, mande o token pelo cabeçalho "
            + "RequestVerificationToken (ver AddAntiforgery em Program.cs) em vez de remover a validação.");
    }

    [Fact]
    public void NenhumaAcao_DesligaOAntiforgeryExplicitamente()
    {
        var isentas = Controllers
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttributes<IgnoreAntiforgeryTokenAttribute>().Any())
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();

        Assert.True(
            isentas.Count == 0,
            "Antiforgery desligado explicitamente em: " + string.Join(", ", isentas));
    }

    /// <summary>
    /// Guarda de sanidade do próprio teste: se a varredura por reflexão parar de encontrar as
    /// ações (renomeação de assembly, mudança de convenção), os dois testes acima passariam
    /// vazios e a proteção sumiria sem ninguém ver.
    /// </summary>
    [Fact]
    public void AVarredura_EncontraAsAcoesDePostConhecidas()
    {
        var acoes = AcoesDePost.Select(m => $"{m.DeclaringType!.Name}.{m.Name}").ToList();

        Assert.Contains("ExameController.Finalizar", acoes);
        Assert.Contains("ExameController.SalvarResposta", acoes);
        Assert.Contains("ExameController.Iniciar", acoes);
        Assert.Contains("ContaController.Sair", acoes);
    }
}
