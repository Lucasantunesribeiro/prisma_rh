using PrismaRH.Api.Producao;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrismaRH.Api.Identidade;
using PrismaRH.Aplicacao.Comum;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Dominio.Folha;
using PrismaRH.Dominio.Auditoria;
using PrismaRH.Infraestrutura.Auditoria;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Api.Endpoints;

public sealed record CriarRubricaRequisicao(
    string Codigo,
    string Nome,
    TipoRubrica Tipo,
    EstrategiaRubrica Estrategia,
    BaseCalculo BasesIncidentes = BaseCalculo.Nenhuma);

public sealed record RenomearRubricaRequisicao(string Nome);

/// <summary>
/// Alterar incidencia e requisicao propria, e nao um campo em Renomear: sao
/// duas coisas com consequencias diferentes. Renomear nao muda calculo algum;
/// mudar incidencia muda o proximo calculo de toda folha aberta.
/// </summary>
public sealed record AlterarIncidenciasRequisicao(BaseCalculo BasesIncidentes);

public sealed record RubricaResposta(
    Guid Id,
    string Codigo,
    string Nome,
    TipoRubrica Tipo,
    EstrategiaRubrica Estrategia,
    BaseCalculo BasesIncidentes,
    bool Ativa)
{
    public static RubricaResposta De(Rubrica r) =>
        new(r.Id, r.Codigo, r.Nome, r.Tipo, r.Estrategia, r.BasesIncidentes, r.Ativa);
}

/// <summary>
/// Catalogo de rubricas. E parametrizacao da empresa, e nao operacao do dia a
/// dia: por isso escreve quem administra empresas, e nao quem processa folha.
/// </summary>
public static class RubricasEndpoints
{
    public static IEndpointRouteBuilder MapearRubricas(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/api/rubricas").WithTags("Rubricas");

        grupo.MapGet("/", ListarAsync)
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        grupo.MapPost("/", CriarAsync)
            .RequireAuthorization(PoliticasAutorizacao.AdministrarEmpresas);

        grupo.MapPut("/{id:guid}", RenomearAsync)
            .RequireAuthorization(PoliticasAutorizacao.AdministrarEmpresas);

        grupo.MapPut("/{id:guid}/incidencias", AlterarIncidenciasAsync)
            .RequireAuthorization(PoliticasAutorizacao.AdministrarEmpresas);

        grupo.MapDelete("/{id:guid}", InativarAsync)
            .RequireAuthorization(PoliticasAutorizacao.AdministrarEmpresas);

        return rotas;
    }

    private static async Task<IResult> ListarAsync(
        PrismaRhDbContext db,
        CancellationToken ct,
        [FromQuery] bool? ativas = null,
        [FromQuery] int? pagina = null,
        [FromQuery] int? tamanho = null)
    {
        var consulta = db.Rubricas.AsNoTracking();

        if (ativas == true)
        {
            consulta = consulta.Where(r => r.Ativa);
        }

        return Results.Ok(await Paginacao.PaginarAsync(
            consulta.OrderBy(r => r.Codigo), RubricaResposta.De, pagina, tamanho, ct));
    }

    private static async Task<IResult> CriarAsync(
        [FromBody] CriarRubricaRequisicao requisicao,
        PrismaRhDbContext db,
        IContextoUsuario usuario,
        IRelogio relogio,
        CancellationToken ct)
    {
        var codigo = (requisicao.Codigo ?? string.Empty).Trim().ToUpperInvariant();

        if (await db.Rubricas.AnyAsync(r => r.Codigo == codigo, ct))
        {
            return Results.Conflict(new { detalhe = "Ja existe uma rubrica com este codigo." });
        }

        // O indice unico parcial no banco tambem barra isso, mas a mensagem
        // dele seria uma violacao de constraint. Aqui da para explicar.
        if (requisicao.Estrategia == EstrategiaRubrica.SalarioBaseProporcional
            && await db.Rubricas.AnyAsync(
                r => r.Ativa && r.Estrategia == EstrategiaRubrica.SalarioBaseProporcional, ct))
        {
            return Results.Conflict(new
            {
                detalhe = "Ja existe uma rubrica de salario-base ativa. Inative a atual antes de criar outra."
            });
        }

        Rubrica rubrica;

        try
        {
            rubrica = new Rubrica(
                usuario.IdOrganizacao, codigo, requisicao.Nome,
                requisicao.Tipo, requisicao.Estrategia, requisicao.BasesIncidentes, relogio.Agora);
        }
        catch (ArgumentException erro)
        {
            return RespostasValidacao.De(erro);
        }

        db.Rubricas.Add(rubrica);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/rubricas/{rubrica.Id}", RubricaResposta.De(rubrica));
    }

    private static async Task<IResult> RenomearAsync(
        Guid id,
        [FromBody] RenomearRubricaRequisicao requisicao,
        PrismaRhDbContext db,
        CancellationToken ct)
    {
        var rubrica = await db.Rubricas.FirstOrDefaultAsync(r => r.Id == id, ct);

        if (rubrica is null)
        {
            return Results.NotFound();
        }

        try
        {
            rubrica.Renomear(requisicao.Nome);
        }
        catch (ArgumentException erro)
        {
            return RespostasValidacao.De(erro);
        }

        await db.SaveChangesAsync(ct);

        return Results.Ok(RubricaResposta.De(rubrica));
    }

    /// <summary>
    /// Altera em quais bases a rubrica incide.
    ///
    /// Nao mexe em folha ja calculada: o lancamento congelou a incidencia que
    /// valia no calculo. Quem quiser a regra nova numa folha aberta recalcula.
    /// </summary>
    private static async Task<IResult> AlterarIncidenciasAsync(
        Guid id,
        [FromBody] AlterarIncidenciasRequisicao requisicao,
        PrismaRhDbContext db,
        CancellationToken ct)
    {
        var rubrica = await db.Rubricas.FirstOrDefaultAsync(r => r.Id == id, ct);

        if (rubrica is null)
        {
            return Results.NotFound();
        }

        try
        {
            rubrica.AlterarIncidencias(requisicao.BasesIncidentes);
        }
        catch (ArgumentException erro)
        {
            return RespostasValidacao.De(erro);
        }

        await db.SaveChangesAsync(ct);

        return Results.Ok(RubricaResposta.De(rubrica));
    }

    /// <summary>
    /// Inativa. Nao apaga: os lancamentos de folhas fechadas apontam para esta
    /// rubrica, e apagar deixaria holerites historicos orfaos.
    /// </summary>
    private static async Task<IResult> InativarAsync(Guid id, PrismaRhDbContext db, CancellationToken ct)
    {
        var rubrica = await db.Rubricas.FirstOrDefaultAsync(r => r.Id == id, ct);

        if (rubrica is null)
        {
            return Results.NotFound();
        }

        rubrica.Inativar();
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
