using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrismaRH.Api.Identidade;
using PrismaRH.Aplicacao.Comum;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Dominio.Empresas;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Api.Endpoints;

public sealed record SalvarEstabelecimentoRequisicao(string Codigo, string Nome);

public sealed record EstabelecimentoResposta(
    Guid Id,
    Guid IdEmpresa,
    string Codigo,
    string Nome,
    bool Ativo,
    DateTimeOffset CriadoEm)
{
    public static EstabelecimentoResposta De(Estabelecimento e) =>
        new(e.Id, e.IdEmpresa, e.Codigo, e.Nome, e.Ativo, e.CriadoEm);
}

public static class EstabelecimentosEndpoints
{
    public static IEndpointRouteBuilder MapearEstabelecimentos(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/api/empresas/{idEmpresa:guid}/estabelecimentos")
            .WithTags("Estabelecimentos");

        grupo.MapGet("/", ListarAsync)
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        grupo.MapPost("/", CriarAsync)
            .RequireAuthorization(PoliticasAutorizacao.AdministrarEmpresas);

        grupo.MapPut("/{id:guid}", AtualizarAsync)
            .RequireAuthorization(PoliticasAutorizacao.AdministrarEmpresas);

        grupo.MapDelete("/{id:guid}", InativarAsync)
            .RequireAuthorization(PoliticasAutorizacao.AdministrarEmpresas);

        return rotas;
    }

    /// <summary>
    /// Confirma que a empresa existe DENTRO da organizacao do token. Sem esta
    /// checagem, um id de empresa do vizinho devolveria lista vazia em vez de
    /// 404 - e lista vazia nao distingue "nao existe" de "nao e sua".
    /// </summary>
    private static Task<bool> EmpresaVisivelAsync(Guid idEmpresa, PrismaRhDbContext db, CancellationToken ct) =>
        db.Empresas.AnyAsync(e => e.Id == idEmpresa, ct);

    private static async Task<IResult> ListarAsync(
        Guid idEmpresa,
        PrismaRhDbContext db,
        CancellationToken ct)
    {
        if (!await EmpresaVisivelAsync(idEmpresa, db, ct))
        {
            return Results.NotFound();
        }

        var itens = await db.Estabelecimentos
            .AsNoTracking()
            .Where(e => e.IdEmpresa == idEmpresa)
            .OrderBy(e => e.Codigo)
            .Select(e => EstabelecimentoResposta.De(e))
            .ToListAsync(ct);

        return Results.Ok(itens);
    }

    private static async Task<IResult> CriarAsync(
        Guid idEmpresa,
        [FromBody] SalvarEstabelecimentoRequisicao requisicao,
        PrismaRhDbContext db,
        IContextoUsuario usuario,
        IRelogio relogio,
        CancellationToken ct)
    {
        if (!await EmpresaVisivelAsync(idEmpresa, db, ct))
        {
            return Results.NotFound();
        }

        if (await db.Estabelecimentos.AnyAsync(e => e.IdEmpresa == idEmpresa && e.Codigo == requisicao.Codigo, ct))
        {
            return Results.Conflict(new { detalhe = "Ja existe um estabelecimento com este codigo nesta empresa." });
        }

        Estabelecimento estabelecimento;

        try
        {
            estabelecimento = new Estabelecimento(
                usuario.IdOrganizacao,
                idEmpresa,
                requisicao.Codigo,
                requisicao.Nome,
                relogio.Agora);
        }
        catch (ArgumentException erro)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [erro.ParamName ?? "requisicao"] = [erro.Message]
            });
        }

        db.Estabelecimentos.Add(estabelecimento);
        await db.SaveChangesAsync(ct);

        return Results.Created(
            $"/api/empresas/{idEmpresa}/estabelecimentos/{estabelecimento.Id}",
            EstabelecimentoResposta.De(estabelecimento));
    }

    private static async Task<IResult> AtualizarAsync(
        Guid idEmpresa,
        Guid id,
        [FromBody] SalvarEstabelecimentoRequisicao requisicao,
        PrismaRhDbContext db,
        CancellationToken ct)
    {
        var estabelecimento = await db.Estabelecimentos
            .FirstOrDefaultAsync(e => e.Id == id && e.IdEmpresa == idEmpresa, ct);

        if (estabelecimento is null)
        {
            return Results.NotFound();
        }

        try
        {
            estabelecimento.Atualizar(requisicao.Codigo, requisicao.Nome);
        }
        catch (ArgumentException erro)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [erro.ParamName ?? "requisicao"] = [erro.Message]
            });
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(EstabelecimentoResposta.De(estabelecimento));
    }

    private static async Task<IResult> InativarAsync(
        Guid idEmpresa,
        Guid id,
        PrismaRhDbContext db,
        CancellationToken ct)
    {
        var estabelecimento = await db.Estabelecimentos
            .FirstOrDefaultAsync(e => e.Id == id && e.IdEmpresa == idEmpresa, ct);

        if (estabelecimento is null)
        {
            return Results.NotFound();
        }

        estabelecimento.Inativar();
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
