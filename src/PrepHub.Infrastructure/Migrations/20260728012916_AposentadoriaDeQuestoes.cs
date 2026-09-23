using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrepHub.Infrastructure.Migrations
{
    /// <summary>
    /// Dá ao banco de questões um jeito de tirar uma questão de circulação.
    ///
    /// <c>IsActive</c> entra com <c>true</c> para todas as linhas existentes de propósito: quem
    /// decide o que sai é o seed, comparando o banco com os arquivos JSON, e não esta migration —
    /// que não tem como saber quais chaves existem no assembly. Na primeira execução depois daqui,
    /// as questões sem arquivo (inclusive as <c>az900-legado-*</c> do seed hardcoded antigo) são
    /// aposentadas.
    ///
    /// O índice do pool ganha <c>IsActive</c> no meio porque a consulta do sorteio passou a ser
    /// <c>WHERE ExamId = ? AND IsActive = 1</c> projetando <c>SkillAreaId</c> — nessa ordem, o
    /// índice cobre a leitura inteira.
    /// </summary>
    public partial class AposentadoriaDeQuestoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Questions_ExamId_SkillAreaId",
                table: "Questions");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Questions",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_Questions_ExamId_IsActive_SkillAreaId",
                table: "Questions",
                columns: new[] { "ExamId", "IsActive", "SkillAreaId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Questions_ExamId_IsActive_SkillAreaId",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Questions");

            migrationBuilder.CreateIndex(
                name: "IX_Questions_ExamId_SkillAreaId",
                table: "Questions",
                columns: new[] { "ExamId", "SkillAreaId" });
        }
    }
}
