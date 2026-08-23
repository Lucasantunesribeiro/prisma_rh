using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrismaRH.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class CadastroFuncionalDeRh : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cargos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_organizacao = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    nome = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cargos", x => x.id);
                    table.ForeignKey(
                        name: "FK_cargos_organizacoes_id_organizacao",
                        column: x => x.id_organizacao,
                        principalTable: "organizacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "funcionarios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_organizacao = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cpf = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    data_nascimento = table.Column<DateOnly>(type: "date", nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_funcionarios", x => x.id);
                    table.ForeignKey(
                        name: "FK_funcionarios_organizacoes_id_organizacao",
                        column: x => x.id_organizacao,
                        principalTable: "organizacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contratos_trabalho",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_organizacao = table.Column<Guid>(type: "uuid", nullable: false),
                    id_funcionario = table.Column<Guid>(type: "uuid", nullable: false),
                    id_empresa = table.Column<Guid>(type: "uuid", nullable: false),
                    matricula = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    data_admissao = table.Column<DateOnly>(type: "date", nullable: false),
                    data_desligamento = table.Column<DateOnly>(type: "date", nullable: true),
                    situacao = table.Column<int>(type: "integer", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contratos_trabalho", x => x.id);
                    table.ForeignKey(
                        name: "FK_contratos_trabalho_empresas_id_empresa",
                        column: x => x.id_empresa,
                        principalTable: "empresas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_contratos_trabalho_funcionarios_id_funcionario",
                        column: x => x.id_funcionario,
                        principalTable: "funcionarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vigencias_contrato",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_organizacao = table.Column<Guid>(type: "uuid", nullable: false),
                    id_contrato = table.Column<Guid>(type: "uuid", nullable: false),
                    valido_de = table.Column<DateOnly>(type: "date", nullable: false),
                    valido_ate = table.Column<DateOnly>(type: "date", nullable: true),
                    salario = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    id_cargo = table.Column<Guid>(type: "uuid", nullable: false),
                    id_estabelecimento = table.Column<Guid>(type: "uuid", nullable: false),
                    jornada_mensal_horas = table.Column<int>(type: "integer", nullable: false),
                    motivo = table.Column<int>(type: "integer", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vigencias_contrato", x => x.id);
                    table.ForeignKey(
                        name: "FK_vigencias_contrato_cargos_id_cargo",
                        column: x => x.id_cargo,
                        principalTable: "cargos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_vigencias_contrato_contratos_trabalho_id_contrato",
                        column: x => x.id_contrato,
                        principalTable: "contratos_trabalho",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_vigencias_contrato_estabelecimentos_id_estabelecimento",
                        column: x => x.id_estabelecimento,
                        principalTable: "estabelecimentos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cargos_organizacao_codigo",
                table: "cargos",
                columns: new[] { "id_organizacao", "codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contratos_empresa_matricula",
                table: "contratos_trabalho",
                columns: new[] { "id_empresa", "matricula" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contratos_funcionario",
                table: "contratos_trabalho",
                column: "id_funcionario");

            migrationBuilder.CreateIndex(
                name: "ix_funcionarios_nome",
                table: "funcionarios",
                column: "nome");

            migrationBuilder.CreateIndex(
                name: "ix_funcionarios_organizacao_cpf",
                table: "funcionarios",
                columns: new[] { "id_organizacao", "cpf" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_vigencias_contrato_id_cargo",
                table: "vigencias_contrato",
                column: "id_cargo");

            migrationBuilder.CreateIndex(
                name: "IX_vigencias_contrato_id_estabelecimento",
                table: "vigencias_contrato",
                column: "id_estabelecimento");

            migrationBuilder.CreateIndex(
                name: "ix_vigencias_contrato_inicio",
                table: "vigencias_contrato",
                columns: new[] { "id_contrato", "valido_de" });

            migrationBuilder.CreateIndex(
                name: "ix_vigencias_uma_aberta_por_contrato",
                table: "vigencias_contrato",
                column: "id_contrato",
                unique: true,
                filter: "valido_ate IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vigencias_contrato");

            migrationBuilder.DropTable(
                name: "cargos");

            migrationBuilder.DropTable(
                name: "contratos_trabalho");

            migrationBuilder.DropTable(
                name: "funcionarios");
        }
    }
}
