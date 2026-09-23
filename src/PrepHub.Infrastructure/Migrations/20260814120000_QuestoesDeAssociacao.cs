using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrepHub.Infrastructure.Migrations
{
    /// <summary>
    /// Abre espaço para o tipo <c>Associacao</c> (arrastar e soltar).
    /// </summary>
    /// <remarks>
    /// Uma coluna só, e nulável — e é essa a razão de a mudança caber numa migration trivial: numa
    /// questão de arrastar, cada alternativa passa a ser um <b>par candidato</b> (alvo × item), com
    /// <c>TargetText</c> guardando o alvo e <c>Text</c> o item. Responder continua sendo selecionar
    /// um conjunto de Ids de alternativa, então correção, gravação de resposta e embaralhamento
    /// seguem intactos. A alternativa seria uma tabela de pares com formato próprio de resposta,
    /// que obrigaria todo caminho do sistema — do <c>CorretorDeProva</c> ao histórico — a aprender
    /// um segundo jeito de estar certo.
    ///
    /// Nada a preencher nas linhas existentes: nulo é exatamente o que os outros três tipos querem
    /// dizer ("esta alternativa não pertence a alvo nenhum").
    /// </remarks>
    public partial class QuestoesDeAssociacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TargetText",
                table: "AnswerOptions",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TargetText",
                table: "AnswerOptions");
        }
    }
}
