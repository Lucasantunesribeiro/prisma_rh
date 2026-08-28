using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrismaRH.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class ImpostoDeRenda : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "QuantidadeDependentesIrrf",
                table: "folhas_funcionario",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "tabelas_irrf",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vigencia_inicio = table.Column<DateOnly>(type: "date", nullable: false),
                    fonte = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    deducao_por_dependente = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    desconto_simplificado = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    redutor_base = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    redutor_coeficiente = table.Column<decimal>(type: "numeric(7,6)", precision: 7, scale: 6, nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tabelas_irrf", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "faixas_irrf",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_tabela_irrf = table.Column<Guid>(type: "uuid", nullable: false),
                    ordem = table.Column<int>(type: "integer", nullable: false),
                    limite_superior = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    aliquota = table.Column<decimal>(type: "numeric(7,6)", precision: 7, scale: 6, nullable: false),
                    parcela_a_deduzir = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_faixas_irrf", x => x.id);
                    table.ForeignKey(
                        name: "FK_faixas_irrf_tabelas_irrf_id_tabela_irrf",
                        column: x => x.id_tabela_irrf,
                        principalTable: "tabelas_irrf",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_faixas_irrf_tabela_ordem",
                table: "faixas_irrf",
                columns: new[] { "id_tabela_irrf", "ordem" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_tabelas_irrf_vigencia_inicio",
                table: "tabelas_irrf",
                column: "vigencia_inicio",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "faixas_irrf");

            migrationBuilder.DropTable(
                name: "tabelas_irrf");

            migrationBuilder.DropColumn(
                name: "QuantidadeDependentesIrrf",
                table: "folhas_funcionario");
        }
    }
}
