using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrismaRH.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class EmailDoUsuarioUnicoGlobalmente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_usuarios_organizacao_email",
                table: "usuarios");

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_email",
                table: "usuarios",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_organizacao",
                table: "usuarios",
                column: "id_organizacao");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_usuarios_email",
                table: "usuarios");

            migrationBuilder.DropIndex(
                name: "ix_usuarios_organizacao",
                table: "usuarios");

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_organizacao_email",
                table: "usuarios",
                columns: new[] { "id_organizacao", "email" },
                unique: true);
        }
    }
}
