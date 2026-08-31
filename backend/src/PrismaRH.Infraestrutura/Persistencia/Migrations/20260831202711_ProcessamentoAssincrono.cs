using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrismaRH.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class ProcessamentoAssincrono : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trabalhos_assincronos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_organizacao = table.Column<Guid>(type: "uuid", nullable: false),
                    id_usuario = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    chave_idempotencia = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    tentativas = table.Column<int>(type: "integer", nullable: false),
                    id_recurso = table.Column<Guid>(type: "uuid", nullable: true),
                    erro = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    iniciado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    concluido_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trabalhos_assincronos", x => x.id);
                    table.ForeignKey(
                        name: "FK_trabalhos_assincronos_usuarios_id_usuario",
                        column: x => x.id_usuario,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "blobs_temporarios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_organizacao = table.Column<Guid>(type: "uuid", nullable: false),
                    id_trabalho = table.Column<Guid>(type: "uuid", nullable: false),
                    conteudo = table.Column<byte[]>(type: "bytea", nullable: false),
                    tamanho_bytes = table.Column<int>(type: "integer", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expira_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_blobs_temporarios", x => x.id);
                    table.ForeignKey(
                        name: "FK_blobs_temporarios_trabalhos_assincronos_id_trabalho",
                        column: x => x.id_trabalho,
                        principalTable: "trabalhos_assincronos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_blobs_expiracao",
                table: "blobs_temporarios",
                column: "expira_em");

            migrationBuilder.CreateIndex(
                name: "ux_blobs_trabalho",
                table: "blobs_temporarios",
                column: "id_trabalho",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trabalhos_assincronos_id_usuario",
                table: "trabalhos_assincronos",
                column: "id_usuario");

            migrationBuilder.CreateIndex(
                name: "ix_trabalhos_organizacao_data",
                table: "trabalhos_assincronos",
                columns: new[] { "id_organizacao", "criado_em" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_trabalhos_chave_idempotencia",
                table: "trabalhos_assincronos",
                column: "chave_idempotencia",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "blobs_temporarios");

            migrationBuilder.DropTable(
                name: "trabalhos_assincronos");
        }
    }
}
