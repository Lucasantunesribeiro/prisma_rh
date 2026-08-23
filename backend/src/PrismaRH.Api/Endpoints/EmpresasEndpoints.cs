using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrismaRH.Api.Identidade;
using PrismaRH.Aplicacao.Comum;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Dominio.Empresas;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Api.Endpoints;

public sealed record CriarEmpresaRequisicao(string RazaoSocial, string Cnpj, string? NomeFantasia);

public sealed record AtualizarEmpresaRequisicao(string RazaoSocial, string? NomeFantasia);

public sealed record EmpresaResposta(
    Guid Id,
    string RazaoSocial,
    string? NomeFantasia,
    string Cnpj,
    string CnpjFormatado,
    bool Ativa,
    DateTimeOffset CriadaEm)
{
    public static EmpresaResposta De(Empresa e) =>
        new(e.Id, e.RazaoSocial, e.NomeFantasia, e.Cnpj.Valor, e.Cnpj.Formatado, e.Ativa, e.CriadaEm);
}

public static class EmpresasEndpoints
{
    public static IEndpointRouteBuilder MapearEmpresas(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/api/empresas").WithTags("Empresas");

        grupo.MapGet("/", ListarAsync)
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        grupo.MapGet("/{id:guid}", ObterAsync)
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        grupo.MapPost("/", CriarAsync)
            .RequireAuthorization(PoliticasAutorizacao.AdministrarEmpresas);

        grupo.MapPut("/{id:guid}", AtualizarAsync)
            .RequireAuthorization(PoliticasAutorizacao.AdministrarEmpresas);

        grupo.MapDelete("/{id:guid}", InativarAsync)
            .RequireAuthorization(PoliticasAutorizacao.AdministrarEmpresas);

        return rotas;
    }

    private static async Task<IResult> ListarAsync(
        PrismaRhDbContext db,
        CancellationToken ct,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanho = 25)
    {
        pagina = Math.Max(1, pagina);
        tamanho = Math.Clamp(tamanho, 1, 100);

        // Sem WHERE por organizacao aqui de proposito: o filtro global do
        // PrismaRhDbContext ja o aplicou. Escrever de novo daria a impressao de
        // que o isolamento depende de lembrar - e ele nao pode depender disso.
        var consulta = db.Empresas.AsNoTracking().OrderBy(e => e.RazaoSocial);

        var total = await consulta.CountAsync(ct);
        var itens = await consulta
            .Skip((pagina - 1) * tamanho)
            .Take(tamanho)
            .Select(e => EmpresaResposta.De(e))
            .ToListAsync(ct);

        return Results.Ok(new { total, pagina, tamanho, itens });
    }

    private static async Task<IResult> ObterAsync(Guid id, PrismaRhDbContext db, CancellationToken ct)
    {
        var empresa = await db.Empresas.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

        // 404, nunca 403: responder "proibido" confirmaria que o recurso existe
        // e permitiria mapear os IDs do vizinho uma requisicao por vez.
        return empresa is null ? Results.NotFound() : Results.Ok(EmpresaResposta.De(empresa));
    }

    private static async Task<IResult> CriarAsync(
        [FromBody] CriarEmpresaRequisicao requisicao,
        PrismaRhDbContext db,
        IContextoUsuario usuario,
        IRelogio relogio,
        CancellationToken ct)
    {
        if (!Cnpj.TentarCriar(requisicao.Cnpj, out var cnpj))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["cnpj"] = ["CNPJ invalido."]
            });
        }

        if (await db.Empresas.AnyAsync(e => e.Cnpj == cnpj, ct))
        {
            return Results.Conflict(new { detalhe = "Ja existe uma empresa com este CNPJ nesta organizacao." });
        }

        Empresa empresa;

        try
        {
            // A organizacao vem do TOKEN. Se o corpo trouxer um idOrganizacao,
            // ele e simplesmente ignorado - nao existe no contrato.
            empresa = new Empresa(
                usuario.IdOrganizacao,
                requisicao.RazaoSocial,
                cnpj,
                relogio.Agora,
                requisicao.NomeFantasia);
        }
        catch (ArgumentException erro)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [erro.ParamName ?? "requisicao"] = [erro.Message]
            });
        }

        db.Empresas.Add(empresa);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/empresas/{empresa.Id}", EmpresaResposta.De(empresa));
    }

    private static async Task<IResult> AtualizarAsync(
        Guid id,
        [FromBody] AtualizarEmpresaRequisicao requisicao,
        PrismaRhDbContext db,
        CancellationToken ct)
    {
        var empresa = await db.Empresas.FirstOrDefaultAsync(e => e.Id == id, ct);

        if (empresa is null)
        {
            return Results.NotFound();
        }

        try
        {
            empresa.Atualizar(requisicao.RazaoSocial, requisicao.NomeFantasia);
        }
        catch (ArgumentException erro)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [erro.ParamName ?? "requisicao"] = [erro.Message]
            });
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(EmpresaResposta.De(empresa));
    }

    private static async Task<IResult> InativarAsync(Guid id, PrismaRhDbContext db, CancellationToken ct)
    {
        var empresa = await db.Empresas.FirstOrDefaultAsync(e => e.Id == id, ct);

        if (empresa is null)
        {
            return Results.NotFound();
        }

        // Inativa em vez de apagar: empresa apagada levaria junto o historico
        // de folha das fases seguintes.
        empresa.Inativar();
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
