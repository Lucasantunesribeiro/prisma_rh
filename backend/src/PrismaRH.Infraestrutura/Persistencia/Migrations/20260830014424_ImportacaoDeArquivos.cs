using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrismaRH.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class ImportacaoDeArquivos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "id_linha_importacao",
                table: "funcionarios",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "importacoes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_organizacao = table.Column<Guid>(type: "uuid", nullable: false),
                    id_usuario = table.Column<Guid>(type: "uuid", nullable: false),
                    nome_original_arquivo = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    formato = table.Column<int>(type: "integer", nullable: false),
                    tamanho_bytes = table.Column<long>(type: "bigint", nullable: false),
                    hash_sha256 = table.Column<string>(type: "char(64)", nullable: false),
                    enviada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    total_linhas = table.Column<int>(type: "integer", nullable: false),
                    linhas_validas = table.Column<int>(type: "integer", nullable: false),
                    linhas_com_erro = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_importacoes", x => x.id);
                    table.CheckConstraint("ck_importacoes_contadores", "total_linhas >= 0 and linhas_validas >= 0 and linhas_com_erro >= 0 and total_linhas = linhas_validas + linhas_com_erro");
                    table.CheckConstraint("ck_importacoes_tamanho", "tamanho_bytes > 0");
                    table.ForeignKey(
                        name: "FK_importacoes_usuarios_id_usuario",
                        column: x => x.id_usuario,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "linhas_importacao",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_organizacao = table.Column<Guid>(type: "uuid", nullable: false),
                    id_importacao = table.Column<Guid>(type: "uuid", nullable: false),
                    numero_no_arquivo = table.Column<int>(type: "integer", nullable: false),
                    situacao = table.Column<int>(type: "integer", nullable: false),
                    erros = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_linhas_importacao", x => x.id);
                    table.CheckConstraint("ck_linhas_importacao_numero", "numero_no_arquivo > 0");
                    table.ForeignKey(
                        name: "FK_linhas_importacao_importacoes_id_importacao",
                        column: x => x.id_importacao,
                        principalTable: "importacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_funcionarios_linha_importacao",
                table: "funcionarios",
                column: "id_linha_importacao",
                filter: "id_linha_importacao is not null");

            migrationBuilder.CreateIndex(
                name: "IX_importacoes_id_usuario",
                table: "importacoes",
                column: "id_usuario");

            migrationBuilder.CreateIndex(
                name: "ix_importacoes_organizacao_enviada_em",
                table: "importacoes",
                columns: new[] { "id_organizacao", "enviada_em" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_importacoes_organizacao_hash",
                table: "importacoes",
                columns: new[] { "id_organizacao", "hash_sha256" });

            migrationBuilder.CreateIndex(
                name: "ux_linhas_importacao_numero",
                table: "linhas_importacao",
                columns: new[] { "id_importacao", "numero_no_arquivo" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_funcionarios_linhas_importacao_id_linha_importacao",
                table: "funcionarios",
                column: "id_linha_importacao",
                principalTable: "linhas_importacao",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_funcionarios_linhas_importacao_id_linha_importacao",
                table: "funcionarios");

            migrationBuilder.DropTable(
                name: "linhas_importacao");

            migrationBuilder.DropTable(
                name: "importacoes");

            migrationBuilder.DropIndex(
                name: "ix_funcionarios_linha_importacao",
                table: "funcionarios");

            migrationBuilder.DropColumn(
                name: "id_linha_importacao",
                table: "funcionarios");
        }
    }
}
