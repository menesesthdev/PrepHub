using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PrepHub.Infrastructure.Persistence;

/// <summary>
/// Factory usada apenas em design-time pelo <c>dotnet ef</c> (migrations). Em runtime, o
/// DbContext é criado pela injeção de dependência configurada em <see cref="DependencyInjection"/>.
/// </summary>
public sealed class PrepHubDbContextFactory : IDesignTimeDbContextFactory<PrepHubDbContext>
{
    public PrepHubDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PrepHubDbContext>()
            .UseSqlite("Data Source=prephub.design.db")
            .Options;

        return new PrepHubDbContext(options);
    }
}
