using PrepHub.Domain.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PrepHub.Infrastructure.Persistence.Configurations;

public sealed class UsuarioConfiguration : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);

        // Provider como int: o enum é a fonte da verdade e o schema fica compacto.
        builder.Property(u => u.Provider).IsRequired().HasConversion<int>();
        builder.Property(u => u.ProviderKey).IsRequired().HasMaxLength(200);
        builder.Property(u => u.Name).IsRequired().HasMaxLength(200);
        builder.Property(u => u.Email).HasMaxLength(320);
        builder.Property(u => u.AvatarUrl).HasMaxLength(500);

        // Nulo em conta social — só a conta local tem senha nossa. O tamanho cobre com folga
        // o formato "pbkdf2-sha256$iteracoes$salt$hash" e deixa espaço para um algoritmo futuro.
        builder.Property(u => u.PasswordHash).HasMaxLength(400);
        builder.Property(u => u.CreatedAt).IsRequired();
        builder.Property(u => u.LastLoginAt).IsRequired();

        // Chave natural da identidade — impede duas linhas para a mesma conta externa
        // mesmo em caso de callback concorrente (duplo clique no botão de login).
        builder.HasIndex(u => new { u.Provider, u.ProviderKey }).IsUnique();
    }
}
