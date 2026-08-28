using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrismaRH.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class TipoDeFolha : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_folhas_empresa_competencia",
                table: "folhas_pagamento");

            migrationBuilder.AddColumn<int>(
                name: "tipo",
                table: "folhas_pagamento",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "ux_folhas_empresa_competencia_tipo",
                table: "folhas_pagamento",
                columns: new[] { "id_empresa", "competencia", "tipo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_folhas_empresa_competencia_tipo",
                table: "folhas_pagamento");

            migrationBuilder.DropColumn(
                name: "tipo",
                table: "folhas_pagamento");

            migrationBuilder.CreateIndex(
                name: "ux_folhas_empresa_competencia",
                table: "folhas_pagamento",
                columns: new[] { "id_empresa", "competencia" },
                unique: true);
        }
    }
}
