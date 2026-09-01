using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrismaRH.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDefaultObsoletoDoTipoDeFolha : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "tipo",
                table: "folhas_pagamento",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "tipo",
                table: "folhas_pagamento",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "integer");
        }
    }
}
