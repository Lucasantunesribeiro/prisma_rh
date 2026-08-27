using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrismaRH.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class DescontoDeInssNaFolha : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_rubricas_salario_base_ativa",
                table: "rubricas");

            migrationBuilder.AddColumn<int>(
                name: "estrategia",
                table: "lancamentos_folha",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ux_rubricas_inss_ativa",
                table: "rubricas",
                column: "id_organizacao",
                unique: true,
                filter: "estrategia = 3 AND ativa");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_rubricas_inss_ativa",
                table: "rubricas");

            migrationBuilder.DropColumn(
                name: "estrategia",
                table: "lancamentos_folha");

            migrationBuilder.CreateIndex(
                name: "ux_rubricas_salario_base_ativa",
                table: "rubricas",
                column: "id_organizacao",
                unique: true,
                filter: "estrategia = 1 AND ativa");
        }
    }
}
