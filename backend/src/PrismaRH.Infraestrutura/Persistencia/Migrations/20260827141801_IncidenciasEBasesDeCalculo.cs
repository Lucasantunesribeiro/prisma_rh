using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrismaRH.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class IncidenciasEBasesDeCalculo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "bases_incidentes",
                table: "rubricas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "bases_incidentes",
                table: "lancamentos_folha",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "bases_apuradas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_organizacao = table.Column<Guid>(type: "uuid", nullable: false),
                    id_folha_funcionario = table.Column<Guid>(type: "uuid", nullable: false),
                    @base = table.Column<int>(name: "base", type: "integer", nullable: false),
                    valor = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bases_apuradas", x => x.id);
                    table.ForeignKey(
                        name: "FK_bases_apuradas_folhas_funcionario_id_folha_funcionario",
                        column: x => x.id_folha_funcionario,
                        principalTable: "folhas_funcionario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_bases_apuradas_holerite_base",
                table: "bases_apuradas",
                columns: new[] { "id_folha_funcionario", "base" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bases_apuradas");

            migrationBuilder.DropColumn(
                name: "bases_incidentes",
                table: "rubricas");

            migrationBuilder.DropColumn(
                name: "bases_incidentes",
                table: "lancamentos_folha");
        }
    }
}
