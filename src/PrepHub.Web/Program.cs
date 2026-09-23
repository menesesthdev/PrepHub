using PrepHub.Application;
using PrepHub.Infrastructure;
using PrepHub.Infrastructure.Persistence;
using PrepHub.Web.Autenticacao;
using PrepHub.Web.Observabilidade;
using PrepHub.Web.Rede;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Único diretório com estado que sobrevive ao processo: banco SQLite e chaves de criptografia.
// Em container é ele que recebe o volume — o resto do sistema de arquivos é descartável.
var diretorioDeDados = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
var diretorioDeChaves = Path.Combine(diretorioDeDados, "dp-keys");
Directory.CreateDirectory(diretorioDeChaves);

// Camadas da aplicação (Clean Architecture).
builder.Services.AddControllersWithViews();

// A tela de prova grava resposta e encerra por fetch, sem formulário — então o token antiforgery
// precisa poder chegar por cabeçalho. O nome é o convencionado pelo ASP.NET Core; o validador
// continua aceitando o campo de formulário, que é o que as telas de conta usam.
builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAutenticacaoSocial(builder.Configuration);
builder.Services.AddLimiteDeTentativas();

// Coleta e exportação de métricas. O medidor de negócio e o coletor que reconta o banco já vieram
// de AddInfrastructure — aqui entra só o que precisa do pipeline HTTP.
builder.Services.AddObservabilidade();

// As chaves do Data Protection cifram o cookie de autenticação e os tokens antiforgery. O padrão
// as guarda numa pasta do perfil do usuário — que num container é disco efêmero: a cada restart
// nasceriam chaves novas, derrubando todas as sessões e fazendo os formulários falharem com
// "the antiforgery token could not be decrypted". Persistir junto do banco resolve os dois casos
// (container e troca de máquina) e mantém um só diretório a preservar/backupear.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(diretorioDeChaves))
    .SetApplicationName("PrepHub");

var app = builder.Build();

// Aplica migrations e semeia o banco no startup — é o que permite ao container subir com o
// volume vazio e se auto-inicializar.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PrepHubDbContext>();
    await db.Database.MigrateAsync();

    // WAL: leitores deixam de bloquear o escritor. É gravado no próprio arquivo do banco,
    // então basta aplicar uma vez — repetir é barato e idempotente.
    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");

    // O logger vai junto porque o seed tem um aviso que não pode ser exceção: exame cujo pool
    // ainda não sustenta o tamanho declarado da prova (ver ConferirTamanhoDoPool).
    var loggerDoSeed = scope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger(nameof(PrepHubDbSeeder));

    await PrepHubDbSeeder.SemearAsync(db, loggerDoSeed);
}

// PRIMEIRO middleware do pipeline, e a ordem é o que o faz funcionar: corrige esquema e IP a
// partir dos cabeçalhos do proxy reverso, antes que HSTS, redirecionamento para HTTPS,
// autenticação e limitador por IP leiam esses valores. Não faz nada quando não há proxy
// declarado (ProxyReverso:Habilitado), que é o caso em desenvolvimento.
app.UseProxyReverso();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Depois de Authentication/Authorization: assim o 429 não gasta cota de quem já está logado
// navegando normalmente, e o limitador só vê o que chega aos endpoints marcados.
app.UseRateLimiter();

// MapStaticAssets mapeia wwwroot como ENDPOINTS, e a política de fallback (exige autenticação)
// vale para todo endpoint sem metadata de autorização — inclusive esses. Sem AllowAnonymous o
// CSS/JS responde 302 para a tela de login e a página carrega sem estilo nenhum.
app.MapStaticAssets().AllowAnonymous();

// Sinal de vida para orquestrador/proxy: responde sem tocar no banco e sem exigir login (a
// política padrão exige autenticação, então precisa de AllowAnonymous explícito).
app.MapGet("/health", () => Results.Ok("ok")).AllowAnonymous();

// /metrics para o Prometheus — por padrão numa porta que o compose não publica no host.
app.MapMetricas();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
