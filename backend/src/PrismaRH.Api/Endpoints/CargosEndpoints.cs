using PrismaRH.Api.Producao;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrismaRH.Api.Identidade;
using PrismaRH.Aplicacao.Comum;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Dominio.Contratos;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Api.Endpoints;

public sealed record SalvarCargoRequisicao(string Codigo, string Nome);

public sealed record CargoResposta(Guid Id, string Codigo, string Nome, bool Ativo)
{
    public static CargoResposta De(Cargo c) => new(c.Id, c.Codigo, c.Nome, c.Ativo);
}

public static class CargosEndpoints
{
    public static IEndpointRouteBuilder MapearCargos(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/api/cargos").WithTags("Cargos");

        grupo.MapGet("/", ListarAsync)
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        grupo.MapPost("/", CriarAsync)
            .RequireAuthorization(PoliticasAutorizacao.AdministrarPessoas);

        grupo.MapPut("/{id:guid}", AtualizarAsync)
            .RequireAuthorization(PoliticasAutorizacao.AdministrarPessoas);

        grupo.MapDelete("/{id:guid}", InativarAsync)
            .RequireAuthorization(PoliticasAutorizacao.AdministrarPessoas);

        return rotas;
    }

    private static async Task<IResult> ListarAsync(
        PrismaRhDbContext db,
        CancellationToken ct,
        [FromQuery] int? pagina = null,
        [FromQuery] int? tamanho = null)
    {
        // Codigo e unico por organizacao: ordenacao deterministica de verdade.
        // Sem isso, OFFSET/LIMIT pode repetir ou pular linha entre paginas.
        var consulta = db.Cargos.AsNoTracking().OrderBy(c => c.Codigo);

        return Results.Ok(await Paginacao.PaginarAsync(
            consulta, CargoResposta.De, pagina, tamanho, ct));
    }

    private static async Task<IResult> CriarAsync(
        [FromBody] SalvarCargoRequisicao requisicao,
        PrismaRhDbContext db,
        IContextoUsuario usuario,
        IRelogio relogio,
        CancellationToken ct)
    {
        if (await db.Cargos.AnyAsync(c => c.Codigo == requisicao.Codigo, ct))
        {
            return Results.Conflict(new { detalhe = "Ja existe um cargo com este codigo." });
        }

        Cargo cargo;

        try
        {
            cargo = new Cargo(usuario.IdOrganizacao, requisicao.Codigo, requisicao.Nome, relogio.Agora);
        }
        catch (ArgumentException erro)
        {
            return RespostasValidacao.De(erro);
        }

        db.Cargos.Add(cargo);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/cargos/{cargo.Id}", CargoResposta.De(cargo));
    }

    private static async Task<IResult> AtualizarAsync(
        Guid id,
        [FromBody] SalvarCargoRequisicao requisicao,
        PrismaRhDbContext db,
        CancellationToken ct)
    {
        var cargo = await db.Cargos.FirstOrDefaultAsync(c => c.Id == id, ct);

        if (cargo is null)
        {
            return Results.NotFound();
        }

        try
        {
            cargo.Atualizar(requisicao.Codigo, requisicao.Nome);
        }
        catch (ArgumentException erro)
        {
            return RespostasValidacao.De(erro);
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(CargoResposta.De(cargo));
    }

    private static async Task<IResult> InativarAsync(Guid id, PrismaRhDbContext db, CancellationToken ct)
    {
        var cargo = await db.Cargos.FirstOrDefaultAsync(c => c.Id == id, ct);

        if (cargo is null)
        {
            return Results.NotFound();
        }

        // Inativa em vez de apagar: cargo apagado deixaria vigencias antigas
        // apontando para o vazio, e o historico precisa continuar legivel.
        cargo.Inativar();
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
