using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrismaRH.Api.Identidade;
using PrismaRH.Aplicacao.Comum;
using PrismaRH.Dominio.Parametros;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Api.Endpoints;

public sealed record FaixaIrrfRequisicao(decimal LimiteSuperior, decimal Aliquota, decimal ParcelaADeduzir);

public sealed record CriarTabelaIrrfRequisicao(
    DateOnly VigenciaInicio,
    string Fonte,
    decimal DeducaoPorDependente,
    decimal DescontoSimplificado,
    decimal RedutorBase,
    decimal RedutorCoeficiente,
    IReadOnlyList<FaixaIrrfRequisicao> Faixas);

public sealed record FaixaIrrfResposta(
    int Ordem,
    decimal LimiteInferior,
    decimal? LimiteSuperior,
    decimal Aliquota,
    decimal AliquotaPercentual,
    decimal ParcelaADeduzir);

public sealed record TabelaIrrfResposta(
    Guid Id,
    DateOnly VigenciaInicio,
    string Fonte,
    decimal DeducaoPorDependente,
    decimal DescontoSimplificado,
    decimal RedutorBase,
    decimal RedutorCoeficiente,
    decimal LimiteDoRedutor,
    decimal LimiteIsencao,
    bool TemRedutor,
    bool Vigente,
    IReadOnlyList<FaixaIrrfResposta> Faixas)
{
    public static TabelaIrrfResposta De(TabelaIrrf t, bool vigente)
    {
        var faixas = new List<FaixaIrrfResposta>();
        var piso = 0m;

        foreach (var f in t.Faixas)
        {
            // O limite inferior nao e guardado - e o superior da anterior.
            // Calcular aqui poupa a tela de refazer a conta.
            faixas.Add(new FaixaIrrfResposta(
                f.Ordem, piso, f.LimiteSuperior, f.Aliquota, f.AliquotaPercentual, f.ParcelaADeduzir));

            piso = f.LimiteSuperior ?? piso;
        }

        return new TabelaIrrfResposta(
            t.Id, t.VigenciaInicio, t.Fonte, t.DeducaoPorDependente, t.DescontoSimplificado,
            t.RedutorBase, t.RedutorCoeficiente, t.LimiteDoRedutor, t.LimiteIsencao,
            t.TemRedutor, vigente, faixas);
    }
}

/// <summary>
/// As tabelas do IRRF, por vigencia.
///
/// Parametro legal FEDERAL, como INSS e FGTS: sem organizacao, fora do filtro
/// global. Todos leem - o analista precisa conferir a conta do holerite -, e so
/// o Administrador da Plataforma escreve, porque um erro aqui atinge todas as
/// organizacoes de uma vez.
///
/// A ULTIMA faixa nao tem teto. Ela e enviada com um limite qualquer, que o
/// dominio ignora, e volta com <c>limiteSuperior: null</c>.
/// </summary>
public static class TabelasIrrfEndpoints
{
    /// <summary>
    /// Teto de faixas por tabela. Nao e regra legal: e limite de payload
    /// (CLAUDE.md secao 24.7). A tabela real tem cinco.
    /// </summary>
    public const int MaximoFaixas = 20;

    public static IEndpointRouteBuilder MapearTabelasIrrf(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/api/tabelas-irrf").WithTags("Tabelas de IRRF");

        grupo.MapGet("/", ListarAsync)
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        grupo.MapPost("/", CriarAsync)
            .RequireAuthorization(PoliticasAutorizacao.AdministradorPlataforma);

        return rotas;
    }

    private static async Task<IResult> ListarAsync(
        PrismaRhDbContext db, IRelogio relogio, CancellationToken ct)
    {
        var tabelas = await db.TabelasIrrf
            .AsNoTracking()
            .Include(t => t.Faixas)
            .OrderByDescending(t => t.VigenciaInicio)
            .ToListAsync(ct);

        var hoje = DateOnly.FromDateTime(relogio.Agora.Date);
        var vigente = TabelaIrrf.VigenteEm(tabelas, hoje);

        return Results.Ok(tabelas
            .Select(t => TabelaIrrfResposta.De(t, vigente is not null && t.Id == vigente.Id))
            .ToList());
    }

    private static async Task<IResult> CriarAsync(
        [FromBody] CriarTabelaIrrfRequisicao requisicao,
        PrismaRhDbContext db,
        IRelogio relogio,
        CancellationToken ct)
    {
        if (requisicao.Faixas is null || requisicao.Faixas.Count == 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["faixas"] = ["Informe ao menos uma faixa."]
            });
        }

        if (requisicao.Faixas.Count > MaximoFaixas)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["faixas"] = [$"No maximo {MaximoFaixas} faixas."]
            });
        }

        // O indice unico tambem barra, mas devolveria violacao de constraint.
        if (await db.TabelasIrrf.AnyAsync(t => t.VigenciaInicio == requisicao.VigenciaInicio, ct))
        {
            return Results.Conflict(new
            {
                detalhe = $"Ja existe tabela de IRRF com vigencia a partir de {requisicao.VigenciaInicio:dd/MM/yyyy}."
            });
        }

        TabelaIrrf tabela;

        try
        {
            tabela = new TabelaIrrf(
                requisicao.VigenciaInicio,
                requisicao.Fonte,
                requisicao.DeducaoPorDependente,
                requisicao.DescontoSimplificado,
                requisicao.RedutorBase,
                requisicao.RedutorCoeficiente,
                requisicao.Faixas.Select(f => (f.LimiteSuperior, f.Aliquota, f.ParcelaADeduzir)),
                relogio.Agora);
        }
        catch (ArgumentException erro)
        {
            return RespostasValidacao.De(erro);
        }

        db.TabelasIrrf.Add(tabela);
        await db.SaveChangesAsync(ct);

        return Results.Created(
            $"/api/tabelas-irrf/{tabela.Id}",
            TabelaIrrfResposta.De(tabela, vigente: false));
    }
}
