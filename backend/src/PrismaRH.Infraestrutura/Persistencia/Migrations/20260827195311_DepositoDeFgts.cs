using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrismaRH.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class DepositoDeFgts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_rubricas_inss_ativa",
                table: "rubricas");

            migrationBuilder.CreateTable(
                name: "tabelas_fgts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vigencia_inicio = table.Column<DateOnly>(type: "date", nullable: false),
                    aliquota = table.Column<decimal>(type: "numeric(7,6)", precision: 7, scale: 6, nullable: false),
                    fonte = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tabelas_fgts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_rubricas_fgts_ativa",
                table: "rubricas",
                column: "id_organizacao",
                unique: true,
                filter: "estrategia = 4 AND ativa");

            migrationBuilder.CreateIndex(
                name: "ux_tabelas_fgts_vigencia_inicio",
                table: "tabelas_fgts",
                column: "vigencia_inicio",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tabelas_fgts");

            migrationBuilder.DropIndex(
                name: "ux_rubricas_fgts_ativa",
                table: "rubricas");

            migrationBuilder.CreateIndex(
                name: "ux_rubricas_inss_ativa",
                table: "rubricas",
                column: "id_organizacao",
                unique: true,
                filter: "estrategia = 3 AND ativa");
        }
    }
}
