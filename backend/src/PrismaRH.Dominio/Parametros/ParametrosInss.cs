using PrismaRH.Dominio.Folha;

namespace PrismaRH.Dominio.Parametros;

/// <summary>
/// O que a folha precisa para descontar INSS: a rubrica que recebe o valor e a
/// tabela que valia na competencia.
///
/// Anda junto de proposito. Ter a rubrica sem a tabela produziria um desconto
/// sem faixas; ter a tabela sem a rubrica produziria um valor sem onde
/// aparecer. As duas coisas seriam falhas silenciosas.
///
/// Nulo significa "esta organizacao ainda nao configurou INSS" - a folha
/// calcula normalmente, sem o desconto. Nao e erro: a Fase 3 fechava folha sem
/// encargo nenhum, e uma organizacao recem-criada continua podendo.
/// </summary>
public sealed record ParametrosInss(Rubrica Rubrica, TabelaInss Tabela)
{
    /// <summary>
    /// Monta os parametros, ou devolve null quando falta qualquer um dos dois.
    ///
    /// A tabela e escolhida pelo PRIMEIRO DIA da competencia: e a data que
    /// identifica a folha. Uma tabela que passe a valer no meio do mes nao e
    /// modelada - isso exigiria regra propria e fonte oficial, e nenhuma das
    /// duas existe aqui.
    /// </summary>
    public static ParametrosInss? Montar(
        Rubrica? rubrica,
        IEnumerable<TabelaInss> tabelas,
        Competencia competencia)
    {
        if (rubrica is null || !rubrica.Ativa)
        {
            return null;
        }

        if (rubrica.Estrategia != EstrategiaRubrica.InssProgressivo)
        {
            throw new ArgumentException(
                $"A rubrica {rubrica.Codigo} nao e a rubrica de INSS.", nameof(rubrica));
        }

        var tabela = TabelaInss.VigenteEm(tabelas, competencia.PrimeiroDia);

        return tabela is null ? null : new ParametrosInss(rubrica, tabela);
    }
}
