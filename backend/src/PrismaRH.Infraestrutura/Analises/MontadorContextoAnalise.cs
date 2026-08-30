using Microsoft.EntityFrameworkCore;
using PrismaRH.Dominio.Analises;
using PrismaRH.Dominio.Folha;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Infraestrutura.Analises;

/// <summary>
/// Monta o retrato que as regras recebem.
///
/// ## Este e o unico lugar que fala com o banco
///
/// As regras sao funcoes puras sobre <see cref="ContextoAnalise"/>. Toda a
/// leitura acontece aqui, **sob o filtro global** - e e isso que faz o
/// isolamento entre organizacoes ser arquitetural em vez de uma conferencia
/// que alguem precisa lembrar de escrever dentro de cada regra.
///
/// Uma regra nova, escrita amanha por outra pessoa, nao tem como vazar dado de
/// outro tenant: ela nao recebe conexao, nao recebe `IdOrganizacao`, e o retrato
/// que chega ate ela ja veio filtrado.
///
/// ## Tres consultas, e nao uma por holerite
///
/// A folha com holeritel e lancamentos vem num `Include`; os contratos da
/// empresa numa consulta; a folha anterior numa terceira. Carregar contrato por
/// holerite dentro do laco produziria uma consulta por pessoa - o classico N+1,
/// que numa folha de mil pessoas vira mil idas ao banco.
/// </summary>
public static class MontadorContextoAnalise
{
    public static async Task<ContextoAnalise> MontarAsync(
        PrismaRhDbContext db, FolhaPagamento folha, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(folha);

        var holerites = await db.FolhasFuncionario
            .AsNoTracking()
            .Where(f => f.IdFolha == folha.Id)
            .Select(f => new
            {
                f.Id,
                f.IdFuncionario,
                f.IdContrato,
                f.SalarioReferencia,
                f.TotalProventos,
                f.TotalDescontos,
                f.Liquido,
                Lancamentos = f.Lancamentos.Select(l => new
                {
                    l.CodigoRubrica,
                    l.NomeRubrica,
                    l.Tipo,
                    l.Origem,
                    l.Valor,
                }).ToList(),
            })
            .ToListAsync(ct);

        // Contratos da EMPRESA da folha, e nao da organizacao inteira: a folha
        // e de uma empresa, e acusar ausencia de gente de outra empresa do
        // mesmo grupo seria ruido garantido.
        var contratos = await db.ContratosTrabalho
            .AsNoTracking()
            .Where(c => c.IdEmpresa == folha.IdEmpresa)
            .Join(
                db.Funcionarios.AsNoTracking(),
                c => c.IdFuncionario,
                f => f.Id,
                (c, f) => new ContratoAnalisado(
                    c.Id, c.IdFuncionario, c.Matricula, f.Nome,
                    c.DataAdmissao, c.DataDesligamento, c.Situacao))
            .ToListAsync(ct);

        var porContrato = contratos.ToDictionary(c => c.IdContrato);
        var anterior = await SalarioNaFolhaAnteriorAsync(db, folha, ct);

        var retrato = holerites
            .Select(h => new HoleriteAnalisado(
                h.Id,
                h.IdFuncionario,
                h.IdContrato,
                porContrato.TryGetValue(h.IdContrato, out var c) ? c.Matricula : "?",
                porContrato.TryGetValue(h.IdContrato, out var f) ? f.NomeFuncionario : "?",
                h.SalarioReferencia,
                h.TotalProventos,
                h.TotalDescontos,
                h.Liquido,
                [.. h.Lancamentos.Select(l => new LancamentoAnalisado(
                    l.CodigoRubrica, l.NomeRubrica, l.Tipo, l.Origem, l.Valor))]))
            .OrderBy(h => h.Matricula, StringComparer.Ordinal)
            .ToList();

        return new ContextoAnalise(
            folha.Id,
            folha.Competencia,
            folha.Tipo,
            folha.Situacao,
            retrato,
            contratos,
            anterior);
    }

    /// <summary>
    /// O salario de referencia de cada contrato na folha MENSAL da competencia
    /// anterior.
    ///
    /// Mensal de proposito: e a folha que todo contrato ativo tem. Comparar com
    /// uma folha de ferias ou de rescisao compararia coisas diferentes, porque
    /// o salario de referencia delas e apurado sobre outro periodo.
    ///
    /// Vazio quando nao ha folha anterior - e a regra de variacao simplesmente
    /// nao tem o que comparar, em vez de tratar a ausencia como zero e acusar
    /// toda admissao.
    /// </summary>
    private static async Task<IReadOnlyDictionary<Guid, decimal>> SalarioNaFolhaAnteriorAsync(
        PrismaRhDbContext db, FolhaPagamento folha, CancellationToken ct)
    {
        var competenciaAnterior = folha.Competencia.Anterior();

        var idAnterior = await db.Folhas
            .AsNoTracking()
            .Where(f => f.IdEmpresa == folha.IdEmpresa
                        && f.Tipo == TipoFolha.Mensal
                        && f.Competencia == competenciaAnterior)
            .Select(f => (Guid?)f.Id)
            .FirstOrDefaultAsync(ct);

        if (idAnterior is not { } id)
        {
            return new Dictionary<Guid, decimal>();
        }

        var salarios = await db.FolhasFuncionario
            .AsNoTracking()
            .Where(f => f.IdFolha == id)
            .Select(f => new { f.IdContrato, f.SalarioReferencia })
            .ToListAsync(ct);

        // Um contrato pode ter mais de um holerite na mesma folha em cenarios
        // futuros; hoje nao tem. `ToDictionary` direto estouraria se um dia
        // tiver, entao agrupa e fica com o maior - que e o que uma conferencia
        // de variacao deveria olhar.
        return salarios
            .GroupBy(s => s.IdContrato)
            .ToDictionary(g => g.Key, g => g.Max(s => s.SalarioReferencia));
    }
}
