using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrepHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SorteioDeQuestoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SkillAreas_ExamId",
                table: "SkillAreas");

            migrationBuilder.DropIndex(
                name: "IX_Questions_ExamId",
                table: "Questions");

            migrationBuilder.AddColumn<string>(
                name: "Key",
                table: "SkillAreas",
                type: "TEXT",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "Questions",
                type: "TEXT",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Topic",
                table: "Questions",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExamAttemptQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExamAttemptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuestionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrderIndex = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamAttemptQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamAttemptQuestions_ExamAttempts_ExamAttemptId",
                        column: x => x.ExamAttemptId,
                        principalTable: "ExamAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExamAttemptQuestions_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            PreencherDadosExistentes(migrationBuilder);

            migrationBuilder.CreateIndex(
                name: "IX_SkillAreas_ExamId_Key",
                table: "SkillAreas",
                columns: new[] { "ExamId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Questions_ExamId_SkillAreaId",
                table: "Questions",
                columns: new[] { "ExamId", "SkillAreaId" });

            migrationBuilder.CreateIndex(
                name: "IX_Questions_ExternalId",
                table: "Questions",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamAttemptQuestions_ExamAttemptId_OrderIndex",
                table: "ExamAttemptQuestions",
                columns: new[] { "ExamAttemptId", "OrderIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamAttemptQuestions_ExamAttemptId_QuestionId",
                table: "ExamAttemptQuestions",
                columns: new[] { "ExamAttemptId", "QuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamAttemptQuestions_QuestionId",
                table: "ExamAttemptQuestions",
                column: "QuestionId");
        }

        /// <summary>
        /// Acerta os dados que já existem antes que os índices únicos passem a valer.
        ///
        /// Roda depois das colunas novas e ANTES dos CreateIndex de propósito: <c>ExternalId</c>
        /// entra com string vazia como default, e um índice único sobre várias linhas vazias
        /// falharia. Vale para banco novo também — ali as instruções simplesmente não afetam linha
        /// nenhuma.
        /// </summary>
        private static void PreencherDadosExistentes(MigrationBuilder migrationBuilder)
        {
            // Slug das áreas já semeadas. O nome exibido é o que existia antes desta migration.
            migrationBuilder.Sql(
                "UPDATE SkillAreas SET Key = 'conceitos-de-nuvem' WHERE Key = '' AND Name LIKE '%conceitos de nuvem%';");
            migrationBuilder.Sql(
                "UPDATE SkillAreas SET Key = 'arquitetura' WHERE Key = '' AND Name LIKE '%arquitetura%';");
            migrationBuilder.Sql(
                "UPDATE SkillAreas SET Key = 'governanca' WHERE Key = '' AND Name LIKE '%governan%';");

            // Rede de segurança: área que não casou com nenhum slug conhecido ainda precisa de um
            // Key único para o índice aceitar. Fica visivelmente marcada como área não mapeada.
            migrationBuilder.Sql(
                "UPDATE SkillAreas SET Key = 'area-' || lower(Id) WHERE Key = '';");

            // As questões que vieram do seed hardcoded não têm chave de arquivo. Marcá-las como
            // legado as mantém no pool (são questões boas, e há tentativas apontando para elas)
            // sem colidir com os ExternalIds dos lotes JSON.
            migrationBuilder.Sql(
                "UPDATE Questions SET ExternalId = 'az900-legado-' || lower(Id) WHERE ExternalId = '';");

            // Reconstrói a composição das tentativas anteriores ao sorteio. Neste ponto da
            // migration o banco ainda tem exatamente as questões daquela época, e a regra de então
            // era "todas as questões do exame, ordenadas por Id" — então a reconstrução é exata.
            // Sem isso, uma prova antiga de 8 itens passaria a ser corrigida contra o banco atual.
            migrationBuilder.Sql(
                """
                INSERT INTO ExamAttemptQuestions (Id, ExamAttemptId, QuestionId, OrderIndex)
                SELECT
                    lower(
                        substr(hex(randomblob(4)), 1, 8) || '-' ||
                        substr(hex(randomblob(2)), 1, 4) || '-' ||
                        substr(hex(randomblob(2)), 1, 4) || '-' ||
                        substr(hex(randomblob(2)), 1, 4) || '-' ||
                        substr(hex(randomblob(6)), 1, 12)),
                    a.Id,
                    q.Id,
                    ROW_NUMBER() OVER (PARTITION BY a.Id ORDER BY q.Id) - 1
                FROM ExamAttempts a
                JOIN Questions q ON q.ExamId = a.ExamId;
                """);

            // TotalQuestions deixa de ser o tamanho do banco e passa a ser o tamanho da prova.
            migrationBuilder.Sql("UPDATE Exams SET TotalQuestions = 40 WHERE Code = 'AZ-900';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExamAttemptQuestions");

            migrationBuilder.DropIndex(
                name: "IX_SkillAreas_ExamId_Key",
                table: "SkillAreas");

            migrationBuilder.DropIndex(
                name: "IX_Questions_ExamId_SkillAreaId",
                table: "Questions");

            migrationBuilder.DropIndex(
                name: "IX_Questions_ExternalId",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "Key",
                table: "SkillAreas");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "Topic",
                table: "Questions");

            migrationBuilder.CreateIndex(
                name: "IX_SkillAreas_ExamId",
                table: "SkillAreas",
                column: "ExamId");

            migrationBuilder.CreateIndex(
                name: "IX_Questions_ExamId",
                table: "Questions",
                column: "ExamId");
        }
    }
}
