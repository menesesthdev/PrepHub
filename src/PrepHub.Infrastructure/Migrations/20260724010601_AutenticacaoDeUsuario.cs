using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrepHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AutenticacaoDeUsuario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Toda tentativa passa a exigir um dono. As que existiam são anteriores ao login
            // e não têm como ser atribuídas a ninguém — sem isso o rebuild da tabela falharia
            // ao aplicar a FK, porque elas ficariam apontando para um usuário inexistente.
            migrationBuilder.Sql("DELETE FROM ExamAttemptAnswers;");
            migrationBuilder.Sql("DELETE FROM ExamAttempts;");

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "ExamAttempts",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Provider = table.Column<int>(type: "INTEGER", nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    AvatarUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExamAttempts_UserId_StartedAt",
                table: "ExamAttempts",
                columns: new[] { "UserId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_Provider_ProviderKey",
                table: "Users",
                columns: new[] { "Provider", "ProviderKey" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ExamAttempts_Users_UserId",
                table: "ExamAttempts",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExamAttempts_Users_UserId",
                table: "ExamAttempts");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropIndex(
                name: "IX_ExamAttempts_UserId_StartedAt",
                table: "ExamAttempts");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "ExamAttempts");
        }
    }
}
