using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrismaRH.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class TabelaProgressivaDoInss : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tabelas_inss",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vigencia_inicio = table.Column<DateOnly>(type: "date", nullable: false),
                    fonte = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tabelas_inss", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "faixas_inss",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_tabela_inss = table.Column<Guid>(type: "uuid", nullable: false),
                    ordem = table.Column<int>(type: "integer", nullable: false),
                    limite_superior = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    aliquota = table.Column<decimal>(type: "numeric(7,6)", precision: 7, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_faixas_inss", x => x.id);
                    table.ForeignKey(
                        name: "FK_faixas_inss_tabelas_inss_id_tabela_inss",
                        column: x => x.id_tabela_inss,
                        principalTable: "tabelas_inss",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_faixas_inss_tabela_ordem",
                table: "faixas_inss",
                columns: new[] { "id_tabela_inss", "ordem" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_tabelas_inss_vigencia_inicio",
                table: "tabelas_inss",
                column: "vigencia_inicio",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "faixas_inss");

            migrationBuilder.DropTable(
                name: "tabelas_inss");
        }
    }
}
