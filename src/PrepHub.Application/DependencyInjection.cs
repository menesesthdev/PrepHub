using PrepHub.Application.Autenticacao;
using PrepHub.Application.Exames;
using PrepHub.Application.Historico;
using PrepHub.Application.Sessoes;
using PrepHub.Application.Sorteios;
using PrepHub.Domain.Sorteio;
using Microsoft.Extensions.DependencyInjection;

namespace PrepHub.Application;

/// <summary>Registra os serviços de caso de uso da camada Application.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ICatalogoDeExamesService, CatalogoDeExamesService>();
        services.AddScoped<ISessaoDeProvaService, SessaoDeProvaService>();

        // A política do sorteio é registrada como serviço para poder ser ajustada em um só lugar
        // (e sobrescrita em teste) sem virar string de configuração.
        services.AddSingleton(PoliticaDeSorteio.Padrao);
        services.AddScoped<ISorteadorDeQuestoes, SorteadorDeQuestoesService>();
        services.AddScoped<IHistoricoDeProvasService, HistoricoDeProvasService>();
        services.AddScoped<IAutenticacaoService, AutenticacaoService>();

        return services;
    }
}
