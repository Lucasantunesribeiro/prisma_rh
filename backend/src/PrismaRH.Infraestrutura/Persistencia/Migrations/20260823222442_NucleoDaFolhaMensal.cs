using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrismaRH.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class NucleoDaFolhaMensal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "folhas_pagamento",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_organizacao = table.Column<Guid>(type: "uuid", nullable: false),
                    id_empresa = table.Column<Guid>(type: "uuid", nullable: false),
                    competencia = table.Column<int>(type: "integer", nullable: false),
                    situacao = table.Column<int>(type: "integer", nullable: false),
                    versao_calculo = table.Column<int>(type: "integer", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    calculada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fechada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    total_proventos = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    total_descontos = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    total_liquido = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_folhas_pagamento", x => x.id);
                    table.ForeignKey(
                        name: "FK_folhas_pagamento_empresas_id_empresa",
                        column: x => x.id_empresa,
                        principalTable: "empresas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rubricas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_organizacao = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    estrategia = table.Column<int>(type: "integer", nullable: false),
                    ativa = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rubricas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "folhas_funcionario",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_organizacao = table.Column<Guid>(type: "uuid", nullable: false),
                    id_folha = table.Column<Guid>(type: "uuid", nullable: false),
                    id_contrato = table.Column<Guid>(type: "uuid", nullable: false),
                    id_funcionario = table.Column<Guid>(type: "uuid", nullable: false),
                    avos = table.Column<int>(type: "integer", nullable: false),
                    divisor = table.Column<int>(type: "integer", nullable: false),
                    salario_referencia = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    id_vigencia_referencia = table.Column<Guid>(type: "uuid", nullable: true),
                    total_proventos = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    total_descontos = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    liquido = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_folhas_funcionario", x => x.id);
                    table.ForeignKey(
                        name: "FK_folhas_funcionario_contratos_trabalho_id_contrato",
                        column: x => x.id_contrato,
                        principalTable: "contratos_trabalho",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_folhas_funcionario_folhas_pagamento_id_folha",
                        column: x => x.id_folha,
                        principalTable: "folhas_pagamento",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_folhas_funcionario_funcionarios_id_funcionario",
                        column: x => x.id_funcionario,
                        principalTable: "funcionarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_folhas_funcionario_vigencias_contrato_id_vigencia_referencia",
                        column: x => x.id_vigencia_referencia,
                        principalTable: "vigencias_contrato",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lancamentos_folha",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_organizacao = table.Column<Guid>(type: "uuid", nullable: false),
                    id_folha_funcionario = table.Column<Guid>(type: "uuid", nullable: false),
                    id_rubrica = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo_rubrica = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nome_rubrica = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    origem = table.Column<int>(type: "integer", nullable: false),
                    valor = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    referencia = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ordem = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lancamentos_folha", x => x.id);
                    table.ForeignKey(
                        name: "FK_lancamentos_folha_folhas_funcionario_id_folha_funcionario",
                        column: x => x.id_folha_funcionario,
                        principalTable: "folhas_funcionario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_lancamentos_folha_rubricas_id_rubrica",
                        column: x => x.id_rubrica,
                        principalTable: "rubricas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "memorias_calculo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_organizacao = table.Column<Guid>(type: "uuid", nullable: false),
                    id_lancamento = table.Column<Guid>(type: "uuid", nullable: false),
                    ordem = table.Column<int>(type: "integer", nullable: false),
                    descricao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    expressao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    valor = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_memorias_calculo", x => x.id);
                    table.ForeignKey(
                        name: "FK_memorias_calculo_lancamentos_folha_id_lancamento",
                        column: x => x.id_lancamento,
                        principalTable: "lancamentos_folha",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_folhas_funcionario_id_contrato",
                table: "folhas_funcionario",
                column: "id_contrato");

            migrationBuilder.CreateIndex(
                name: "IX_folhas_funcionario_id_funcionario",
                table: "folhas_funcionario",
                column: "id_funcionario");

            migrationBuilder.CreateIndex(
                name: "IX_folhas_funcionario_id_vigencia_referencia",
                table: "folhas_funcionario",
                column: "id_vigencia_referencia");

            migrationBuilder.CreateIndex(
                name: "ux_folhas_funcionario_folha_contrato",
                table: "folhas_funcionario",
                columns: new[] { "id_folha", "id_contrato" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_folhas_empresa_competencia",
                table: "folhas_pagamento",
                columns: new[] { "id_empresa", "competencia" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lancamentos_folha_id_rubrica",
                table: "lancamentos_folha",
                column: "id_rubrica");

            migrationBuilder.CreateIndex(
                name: "ix_lancamentos_folha_ordem",
                table: "lancamentos_folha",
                columns: new[] { "id_folha_funcionario", "ordem" });

            migrationBuilder.CreateIndex(
                name: "ix_memorias_calculo_ordem",
                table: "memorias_calculo",
                columns: new[] { "id_lancamento", "ordem" });

            migrationBuilder.CreateIndex(
                name: "ux_rubricas_organizacao_codigo",
                table: "rubricas",
                columns: new[] { "id_organizacao", "codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_rubricas_salario_base_ativa",
                table: "rubricas",
                column: "id_organizacao",
                unique: true,
                filter: "estrategia = 1 AND ativa");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "memorias_calculo");

            migrationBuilder.DropTable(
                name: "lancamentos_folha");

            migrationBuilder.DropTable(
                name: "folhas_funcionario");

            migrationBuilder.DropTable(
                name: "rubricas");

            migrationBuilder.DropTable(
                name: "folhas_pagamento");
        }
    }
}
