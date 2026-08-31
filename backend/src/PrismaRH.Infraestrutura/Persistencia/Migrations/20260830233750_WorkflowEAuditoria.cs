using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrismaRH.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowEAuditoria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "concluida_em",
                table: "resultados_analise",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "id_responsavel",
                table: "resultados_analise",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "justificativa",
                table: "resultados_analise",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            // ⚠️ AJUSTADO A MAO, e o motivo importa.
            //
            // O EF gerou `defaultValue: 0` - o default do CLR para int. Mas
            // StatusInconsistencia comeca em 1 (Detectada), e ZERO NAO E UM
            // VALOR VALIDO do enum.
            //
            // As inconsistencias que ja existem foram encontradas pelo motor e
            // ninguem olhou: elas SAO 'Detectada'. Com o zero, cada uma viraria
            // um enum invalido que o C# leria como um valor sem nome, e a tela
            // mostraria um status que nao existe.
            migrationBuilder.AddColumn<int>(
                name: "status",
                table: "resultados_analise",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "andamentos_inconsistencia",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_organizacao = table.Column<Guid>(type: "uuid", nullable: false),
                    id_resultado_analise = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    id_autor = table.Column<Guid>(type: "uuid", nullable: true),
                    ocorrido_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sequencia = table.Column<int>(type: "integer", nullable: false),
                    texto = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status_anterior = table.Column<int>(type: "integer", nullable: true),
                    status_novo = table.Column<int>(type: "integer", nullable: true),
                    responsavel_anterior = table.Column<Guid>(type: "uuid", nullable: true),
                    responsavel_novo = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_andamentos_inconsistencia", x => x.id);
                    table.ForeignKey(
                        name: "FK_andamentos_inconsistencia_resultados_analise_id_resultado_a~",
                        column: x => x.id_resultado_analise,
                        principalTable: "resultados_analise",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_andamentos_inconsistencia_usuarios_id_autor",
                        column: x => x.id_autor,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "eventos_auditoria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_organizacao = table.Column<Guid>(type: "uuid", nullable: false),
                    id_usuario = table.Column<Guid>(type: "uuid", nullable: true),
                    acao = table.Column<int>(type: "integer", nullable: false),
                    entidade = table.Column<int>(type: "integer", nullable: false),
                    id_entidade = table.Column<Guid>(type: "uuid", nullable: false),
                    descricao = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    contexto = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ocorrido_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_eventos_auditoria", x => x.id);
                    table.ForeignKey(
                        name: "FK_eventos_auditoria_usuarios_id_usuario",
                        column: x => x.id_usuario,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_resultados_analise_id_responsavel",
                table: "resultados_analise",
                column: "id_responsavel");

            migrationBuilder.CreateIndex(
                name: "ix_resultados_analise_responsavel",
                table: "resultados_analise",
                columns: new[] { "id_organizacao", "id_responsavel" });

            migrationBuilder.CreateIndex(
                name: "ix_resultados_analise_status",
                table: "resultados_analise",
                columns: new[] { "id_organizacao", "status", "severidade" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_andamentos_inconsistencia_id_autor",
                table: "andamentos_inconsistencia",
                column: "id_autor");

            migrationBuilder.CreateIndex(
                name: "IX_andamentos_inconsistencia_id_resultado_analise",
                table: "andamentos_inconsistencia",
                column: "id_resultado_analise");

            migrationBuilder.CreateIndex(
                name: "ix_andamentos_inconsistencia_resultado",
                table: "andamentos_inconsistencia",
                columns: new[] { "id_organizacao", "id_resultado_analise", "sequencia" });

            migrationBuilder.CreateIndex(
                name: "ix_eventos_auditoria_entidade",
                table: "eventos_auditoria",
                columns: new[] { "id_organizacao", "entidade", "id_entidade" });

            migrationBuilder.CreateIndex(
                name: "IX_eventos_auditoria_id_usuario",
                table: "eventos_auditoria",
                column: "id_usuario");

            migrationBuilder.CreateIndex(
                name: "ix_eventos_auditoria_organizacao_data",
                table: "eventos_auditoria",
                columns: new[] { "id_organizacao", "ocorrido_em" },
                descending: new[] { false, true });

            migrationBuilder.AddForeignKey(
                name: "FK_resultados_analise_usuarios_id_responsavel",
                table: "resultados_analise",
                column: "id_responsavel",
                principalTable: "usuarios",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_resultados_analise_usuarios_id_responsavel",
                table: "resultados_analise");

            migrationBuilder.DropTable(
                name: "andamentos_inconsistencia");

            migrationBuilder.DropTable(
                name: "eventos_auditoria");

            migrationBuilder.DropIndex(
                name: "IX_resultados_analise_id_responsavel",
                table: "resultados_analise");

            migrationBuilder.DropIndex(
                name: "ix_resultados_analise_responsavel",
                table: "resultados_analise");

            migrationBuilder.DropIndex(
                name: "ix_resultados_analise_status",
                table: "resultados_analise");

            migrationBuilder.DropColumn(
                name: "concluida_em",
                table: "resultados_analise");

            migrationBuilder.DropColumn(
                name: "id_responsavel",
                table: "resultados_analise");

            migrationBuilder.DropColumn(
                name: "justificativa",
                table: "resultados_analise");

            migrationBuilder.DropColumn(
                name: "status",
                table: "resultados_analise");
        }
    }
}
