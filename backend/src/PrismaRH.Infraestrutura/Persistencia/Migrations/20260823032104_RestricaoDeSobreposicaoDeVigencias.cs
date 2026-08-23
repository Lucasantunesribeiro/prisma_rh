using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrismaRH.Infraestrutura.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class RestricaoDeSobreposicaoDeVigencias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_vigencias_uma_aberta_por_contrato",
                table: "vigencias_contrato");

            // btree_gist permite usar o operador de igualdade (=) sobre uuid
            // dentro de um indice GiST. Sem ele nao da para combinar
            // "mesmo contrato" com "periodos que se cruzam" na mesma constraint.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");

            // Impede QUALQUER sobreposicao de periodos no mesmo contrato.
            //
            // daterange(valido_de, valido_ate, '[]') inclui as duas pontas; com
            // valido_ate nulo o intervalo fica aberto para a direita, que e
            // exatamente o significado de "vigencia atual".
            //
            // DEFERRABLE INITIALLY DEFERRED e o ponto central: a checagem
            // acontece no COMMIT, e nao a cada comando. O EF insere a vigencia
            // nova antes de fechar a anterior, e nesse instante intermediario
            // as duas parecem abertas - uma constraint imediata recusaria a
            // operacao legitima.
            migrationBuilder.Sql("""
                ALTER TABLE vigencias_contrato
                ADD CONSTRAINT ex_vigencias_sem_sobreposicao
                EXCLUDE USING gist (
                    id_contrato WITH =,
                    daterange(valido_de, valido_ate, '[]') WITH &&
                ) DEFERRABLE INITIALLY DEFERRED;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE vigencias_contrato DROP CONSTRAINT IF EXISTS ex_vigencias_sem_sobreposicao;");

            migrationBuilder.CreateIndex(
                name: "ix_vigencias_uma_aberta_por_contrato",
                table: "vigencias_contrato",
                column: "id_contrato",
                unique: true,
                filter: "valido_ate IS NULL");
        }
    }
}
