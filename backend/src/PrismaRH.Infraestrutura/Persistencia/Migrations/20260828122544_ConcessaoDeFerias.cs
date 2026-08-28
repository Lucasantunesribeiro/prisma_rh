using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrismaRH.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class ConcessaoDeFerias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "concessoes_ferias",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_organizacao = table.Column<Guid>(type: "uuid", nullable: false),
                    id_contrato = table.Column<Guid>(type: "uuid", nullable: false),
                    inicio_periodo_aquisitivo = table.Column<DateOnly>(type: "date", nullable: false),
                    fim_periodo_aquisitivo = table.Column<DateOnly>(type: "date", nullable: false),
                    inicio = table.Column<DateOnly>(type: "date", nullable: false),
                    dias = table.Column<int>(type: "integer", nullable: false),
                    dias_abono_pecuniario = table.Column<int>(type: "integer", nullable: false),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_concessoes_ferias", x => x.id);
                    table.CheckConstraint("ck_concessoes_ferias_dias", "dias >= 0\nAND dias_abono_pecuniario >= 0\nAND (dias + dias_abono_pecuniario) > 0\nAND inicio > fim_periodo_aquisitivo\nAND fim_periodo_aquisitivo > inicio_periodo_aquisitivo");
                    table.ForeignKey(
                        name: "FK_concessoes_ferias_contratos_trabalho_id_contrato",
                        column: x => x.id_contrato,
                        principalTable: "contratos_trabalho",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_concessoes_ferias_id_contrato",
                table: "concessoes_ferias",
                column: "id_contrato");

            migrationBuilder.CreateIndex(
                name: "ix_concessoes_ferias_organizacao_contrato",
                table: "concessoes_ferias",
                columns: new[] { "id_organizacao", "id_contrato" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "concessoes_ferias");
        }
    }
}
