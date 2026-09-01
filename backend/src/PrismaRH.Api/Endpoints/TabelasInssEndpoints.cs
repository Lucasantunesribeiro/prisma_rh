using PrismaRH.Api.Producao;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrismaRH.Api.Identidade;
using PrismaRH.Aplicacao.Comum;
using PrismaRH.Dominio.Parametros;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Api.Endpoints;

public sealed record FaixaInssRequisicao(decimal LimiteSuperior, decimal Aliquota);

public sealed record CriarTabelaInssRequisicao(
    DateOnly VigenciaInicio,
    string Fonte,
    IReadOnlyList<FaixaInssRequisicao> Faixas);

public sealed record FaixaInssResposta(
    int Ordem, decimal LimiteInferior, decimal LimiteSuperior, decimal Aliquota, decimal AliquotaPercentual);

public sealed record TabelaInssResposta(
    Guid Id,
    DateOnly VigenciaInicio,
    string Fonte,
    decimal Teto,
    bool Vigente,
    IReadOnlyList<FaixaInssResposta> Faixas)
{
    public static TabelaInssResposta De(TabelaInss t, bool vigente)
    {
        var faixas = new List<FaixaInssResposta>();
        var piso = 0m;

        foreach (var f in t.Faixas)
        {
            // O limite inferior nao e guardado - e o superior da faixa
            // anterior. Calcular aqui poupa a tela de refazer a conta.
            faixas.Add(new FaixaInssResposta(
                f.Ordem, piso, f.LimiteSuperior, f.Aliquota, f.AliquotaPercentual));

            piso = f.LimiteSuperior;
        }

        return new TabelaInssResposta(t.Id, t.VigenciaInicio, t.Fonte, t.Teto, vigente, faixas);
    }
}

/// <summary>
/// As tabelas progressivas de INSS, por vigencia.
///
/// Parametro legal FEDERAL: nao pertence a organizacao alguma e nao passa pelo
/// filtro global. Todos leem, porque o analista precisa conferir a conta do
/// holerite; so o Administrador da Plataforma escreve, porque um erro aqui
/// atinge todas as organizacoes de uma vez.
///
/// Cadastrar a tabela de 2027 e chamar o POST com a vigencia nova. O algoritmo
/// progressivo nao muda.
/// </summary>
public static class TabelasInssEndpoints
{
    public static IEndpointRouteBuilder MapearTabelasInss(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/api/tabelas-inss").WithTags("Tabelas de INSS");

        grupo.MapGet("/", ListarAsync)
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        grupo.MapPost("/", CriarAsync)
            .RequireAuthorization(PoliticasAutorizacao.AdministradorPlataforma);

        return rotas;
    }

    private static async Task<IResult> ListarAsync(
        PrismaRhDbContext db, IRelogio relogio, CancellationToken ct)
    {
        var tabelas = await db.TabelasInss
            .AsNoTracking()
            .Include(t => t.Faixas)
            .OrderByDescending(t => t.VigenciaInicio)
            .ComTeto()
            .ToListAsync(ct);

        var hoje = DateOnly.FromDateTime(relogio.Agora.Date);
        var vigente = TabelaInss.VigenteEm(tabelas, hoje);

        return Results.Ok(tabelas
            .Select(t => TabelaInssResposta.De(t, vigente is not null && t.Id == vigente.Id))
            .ToList());
    }

    private static async Task<IResult> CriarAsync(
        [FromBody] CriarTabelaInssRequisicao requisicao,
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

        // O indice unico no banco tambem barra, mas a mensagem dele seria uma
        // violacao de constraint. Aqui da para explicar.
        if (await db.TabelasInss.AnyAsync(t => t.VigenciaInicio == requisicao.VigenciaInicio, ct))
        {
            return Results.Conflict(new
            {
                detalhe = $"Ja existe tabela de INSS com vigencia a partir de {requisicao.VigenciaInicio:dd/MM/yyyy}."
            });
        }

        TabelaInss tabela;

        try
        {
            tabela = new TabelaInss(
                requisicao.VigenciaInicio,
                requisicao.Fonte,
                requisicao.Faixas.Select(f => (f.LimiteSuperior, f.Aliquota)),
                relogio.Agora);
        }
        catch (ArgumentException erro)
        {
            return RespostasValidacao.De(erro);
        }

        db.TabelasInss.Add(tabela);
        await db.SaveChangesAsync(ct);

        return Results.Created(
            $"/api/tabelas-inss/{tabela.Id}",
            TabelaInssResposta.De(tabela, vigente: false));
    }
}
