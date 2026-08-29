using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrismaRH.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class ValorBaseFgtsRescisorio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "valores_base_fgts_rescisorio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_organizacao = table.Column<Guid>(type: "uuid", nullable: false),
                    id_contrato = table.Column<Guid>(type: "uuid", nullable: false),
                    valor = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    observacao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    informado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_valores_base_fgts_rescisorio", x => x.id);
                    table.CheckConstraint("ck_valores_base_fgts_nao_negativo", "valor >= 0");
                    table.ForeignKey(
                        name: "FK_valores_base_fgts_rescisorio_contratos_trabalho_id_contrato",
                        column: x => x.id_contrato,
                        principalTable: "contratos_trabalho",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_valores_base_fgts_contrato",
                table: "valores_base_fgts_rescisorio",
                column: "id_contrato",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "valores_base_fgts_rescisorio");
        }
    }
}
