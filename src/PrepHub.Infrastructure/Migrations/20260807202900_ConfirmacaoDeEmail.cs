using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrepHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConfirmacaoDeEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EmailConfirmedAt",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmailConfirmationTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailConfirmationTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailConfirmationTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailConfirmationTokens_TokenHash",
                table: "EmailConfirmationTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailConfirmationTokens_UserId",
                table: "EmailConfirmationTokens",
                column: "UserId");

            // ⚠️ Backfill obrigatório, não conveniência. A coluna nasce nula e nulo significa
            // "não entra": sem esta linha, publicar a migration trancaria fora TODA conta que já
            // existe — inclusive as sociais, que nem têm por onde confirmar — e ninguém teria
            // como recuperar o acesso, porque não há link emitido para uma conta antiga.
            //
            // CreatedAt é a data certa: para a conta social, o provedor de fato verificou o
            // endereço naquele momento; para as locais anteriores a esta regra, é o instante em
            // que o produto passou a considerá-las boas. Inventar "agora" seria dizer que todas
            // foram verificadas no dia do deploy, o que não aconteceu.
            migrationBuilder.Sql("UPDATE Users SET EmailConfirmedAt = CreatedAt WHERE EmailConfirmedAt IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailConfirmationTokens");

            migrationBuilder.DropColumn(
                name: "EmailConfirmedAt",
                table: "Users");
        }
    }
}
