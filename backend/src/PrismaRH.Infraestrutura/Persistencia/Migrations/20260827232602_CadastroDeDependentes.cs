using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrismaRH.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class CadastroDeDependentes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dependentes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_organizacao = table.Column<Guid>(type: "uuid", nullable: false),
                    id_funcionario = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    data_nascimento = table.Column<DateOnly>(type: "date", nullable: false),
                    relacao = table.Column<int>(type: "integer", nullable: false),
                    inicio_deducao_irrf = table.Column<DateOnly>(type: "date", nullable: true),
                    fim_deducao_irrf = table.Column<DateOnly>(type: "date", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dependentes", x => x.id);
                    table.CheckConstraint("ck_dependentes_periodo_deducao", "(inicio_deducao_irrf IS NOT NULL OR fim_deducao_irrf IS NULL)\nAND (fim_deducao_irrf IS NULL OR fim_deducao_irrf >= inicio_deducao_irrf)");
                    table.ForeignKey(
                        name: "FK_dependentes_funcionarios_id_funcionario",
                        column: x => x.id_funcionario,
                        principalTable: "funcionarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_dependentes_id_funcionario",
                table: "dependentes",
                column: "id_funcionario");

            migrationBuilder.CreateIndex(
                name: "ix_dependentes_organizacao_funcionario",
                table: "dependentes",
                columns: new[] { "id_organizacao", "id_funcionario" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dependentes");
        }
    }
}
