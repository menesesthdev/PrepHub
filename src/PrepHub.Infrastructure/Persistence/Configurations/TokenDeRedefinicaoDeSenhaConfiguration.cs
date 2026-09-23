using PrepHub.Domain.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PrepHub.Infrastructure.Persistence.Configurations;

public sealed class TokenDeRedefinicaoDeSenhaConfiguration : IEntityTypeConfiguration<TokenDeRedefinicaoDeSenha>
{
    public void Configure(EntityTypeBuilder<TokenDeRedefinicaoDeSenha> builder)
    {
        builder.ToTable("PasswordResetTokens");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.UserId).IsRequired();
        // Base64 de SHA-256 dá 44 caracteres; o campo é fixo por natureza.
        builder.Property(t => t.TokenHash).IsRequired().HasMaxLength(64);
        builder.Property(t => t.CreatedAt).IsRequired();
        builder.Property(t => t.ExpiresAt).IsRequired();

        // Índice único no hash: é por ele que todo acesso ao token acontece, e dois tokens com
        // o mesmo hash seriam colisão de SHA-256 — se acontecer, é para falhar, não escolher um.
        builder.HasIndex(t => t.TokenHash).IsUnique();

        // Busca "tokens ativos deste usuário" a cada pedido de redefinição.
        builder.HasIndex(t => t.UserId);

        // Sem propriedade de navegação no Domain (o token não precisa carregar o Usuario), mas
        // a FK com cascade existe para que apagar um usuário não deixe token órfão para trás.
        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
