using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrepHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExameEmConstrucao : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// ⚠️ O <c>defaultValue: true</c> não é cosmético e não pode virar <c>false</c>: no SQLite
        /// o ADD COLUMN preenche as linhas existentes com o default, então é ele que mantém o
        /// AZ-900 publicado depois da migration. Com default <c>false</c>, publicar isto tiraria o
        /// único exame do ar — o catálogo ficaria vazio e "Iniciar simulado" sumiria da tela, sem
        /// erro nenhum para explicar por quê. É o mesmo cuidado do backfill de
        /// <c>EmailConfirmedAt</c> em <c>ConfirmacaoDeEmail</c>: coluna nova cujo valor padrão
        /// nega acesso é uma indisponibilidade silenciosa em produção.
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPublished",
                table: "Exams",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPublished",
                table: "Exams");
        }
    }
}
