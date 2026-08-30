using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrismaRH.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class MotorDeAnalises : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "execucoes_analise",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_organizacao = table.Column<Guid>(type: "uuid", nullable: false),
                    id_folha = table.Column<Guid>(type: "uuid", nullable: false),
                    competencia = table.Column<int>(type: "integer", nullable: false),
                    versao_calculo_folha = table.Column<int>(type: "integer", nullable: false),
                    id_usuario = table.Column<Guid>(type: "uuid", nullable: true),
                    executada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    regras_executadas = table.Column<int>(type: "integer", nullable: false),
                    total_resultados = table.Column<int>(type: "integer", nullable: false),
                    resultados_altos = table.Column<int>(type: "integer", nullable: false),
                    resultados_medios = table.Column<int>(type: "integer", nullable: false),
                    resultados_baixos = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_execucoes_analise", x => x.id);
                    table.CheckConstraint("ck_execucoes_analise_contadores", "regras_executadas >= 0 and total_resultados >= 0 and total_resultados = resultados_altos + resultados_medios + resultados_baixos");
                    table.ForeignKey(
                        name: "FK_execucoes_analise_folhas_pagamento_id_folha",
                        column: x => x.id_folha,
                        principalTable: "folhas_pagamento",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_execucoes_analise_usuarios_id_usuario",
                        column: x => x.id_usuario,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "regras_analise",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_organizacao = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<int>(type: "integer", nullable: false),
                    ativa = table.Column<bool>(type: "boolean", nullable: false),
                    severidade = table.Column<int>(type: "integer", nullable: false),
                    alterado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    alterado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_regras_analise", x => x.id);
                    table.ForeignKey(
                        name: "FK_regras_analise_usuarios_alterado_por",
                        column: x => x.alterado_por,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "resultados_analise",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_organizacao = table.Column<Guid>(type: "uuid", nullable: false),
                    id_execucao_analise = table.Column<Guid>(type: "uuid", nullable: false),
                    id_folha = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo_regra = table.Column<int>(type: "integer", nullable: false),
                    versao_regra = table.Column<int>(type: "integer", nullable: false),
                    categoria = table.Column<int>(type: "integer", nullable: false),
                    severidade = table.Column<int>(type: "integer", nullable: false),
                    id_folha_funcionario = table.Column<Guid>(type: "uuid", nullable: true),
                    id_funcionario = table.Column<Guid>(type: "uuid", nullable: true),
                    matricula = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    nome_funcionario = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    descricao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    valor_esperado = table.Column<decimal>(type: "numeric(14,2)", nullable: true),
                    valor_encontrado = table.Column<decimal>(type: "numeric(14,2)", nullable: true),
                    diferenca = table.Column<decimal>(type: "numeric(14,2)", nullable: true),
                    contexto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resultados_analise", x => x.id);
                    table.ForeignKey(
                        name: "FK_resultados_analise_execucoes_analise_id_execucao_analise",
                        column: x => x.id_execucao_analise,
                        principalTable: "execucoes_analise",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "parametros_regra_analise",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_organizacao = table.Column<Guid>(type: "uuid", nullable: false),
                    id_regra_analise = table.Column<Guid>(type: "uuid", nullable: false),
                    chave = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    valor = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parametros_regra_analise", x => x.id);
                    table.ForeignKey(
                        name: "FK_parametros_regra_analise_regras_analise_id_regra_analise",
                        column: x => x.id_regra_analise,
                        principalTable: "regras_analise",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_execucoes_analise_folha",
                table: "execucoes_analise",
                columns: new[] { "id_organizacao", "id_folha", "executada_em" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_execucoes_analise_id_folha",
                table: "execucoes_analise",
                column: "id_folha");

            migrationBuilder.CreateIndex(
                name: "IX_execucoes_analise_id_usuario",
                table: "execucoes_analise",
                column: "id_usuario");

            migrationBuilder.CreateIndex(
                name: "ux_parametros_regra_analise_chave",
                table: "parametros_regra_analise",
                columns: new[] { "id_regra_analise", "chave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_regras_analise_alterado_por",
                table: "regras_analise",
                column: "alterado_por");

            migrationBuilder.CreateIndex(
                name: "ux_regras_analise_organizacao_codigo",
                table: "regras_analise",
                columns: new[] { "id_organizacao", "codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_resultados_analise_execucao",
                table: "resultados_analise",
                columns: new[] { "id_organizacao", "id_execucao_analise", "severidade" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_resultados_analise_folha",
                table: "resultados_analise",
                columns: new[] { "id_organizacao", "id_folha" });

            migrationBuilder.CreateIndex(
                name: "IX_resultados_analise_id_execucao_analise",
                table: "resultados_analise",
                column: "id_execucao_analise");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "parametros_regra_analise");

            migrationBuilder.DropTable(
                name: "resultados_analise");

            migrationBuilder.DropTable(
                name: "regras_analise");

            migrationBuilder.DropTable(
                name: "execucoes_analise");
        }
    }
}
