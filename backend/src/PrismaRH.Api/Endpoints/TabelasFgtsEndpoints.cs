using PrismaRH.Api.Producao;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrismaRH.Api.Identidade;
using PrismaRH.Aplicacao.Comum;
using PrismaRH.Dominio.Parametros;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Api.Endpoints;

public sealed record CriarTabelaFgtsRequisicao(
    DateOnly VigenciaInicio,
    decimal Aliquota,
    string Fonte);

public sealed record TabelaFgtsResposta(
    Guid Id,
    DateOnly VigenciaInicio,
    decimal Aliquota,
    decimal AliquotaPercentual,
    string Fonte,
    bool Vigente)
{
    public static TabelaFgtsResposta De(TabelaFgts t, bool vigente) =>
        new(t.Id, t.VigenciaInicio, t.Aliquota, t.AliquotaPercentual, t.Fonte, vigente);
}

/// <summary>
/// A aliquota de FGTS por vigencia.
///
/// Mesma natureza das tabelas de INSS: parametro legal FEDERAL, sem
/// id_organizacao e fora do filtro global. Todos leem, porque o analista
/// precisa conferir de onde saiu o valor do holerite; so o Administrador da
/// Plataforma escreve, porque um erro aqui erra o deposito de todas as
/// organizacoes ao mesmo tempo.
///
/// A aliquota trafega como FRACAO (0.08), nao percentual. A resposta tambem
/// devolve AliquotaPercentual para a tela nao precisar refazer a conta - mas a
/// escrita continua sendo em fracao, e o dominio recusa 8 em vez de 0.08.
/// </summary>
public static class TabelasFgtsEndpoints
{
    public static IEndpointRouteBuilder MapearTabelasFgts(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/api/tabelas-fgts").WithTags("Tabelas de FGTS");

        grupo.MapGet("/", ListarAsync)
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        grupo.MapPost("/", CriarAsync)
            .RequireAuthorization(PoliticasAutorizacao.AdministradorPlataforma);

        return rotas;
    }

    private static async Task<IResult> ListarAsync(
        PrismaRhDbContext db, IRelogio relogio, CancellationToken ct)
    {
        // Sem paginacao por ser lista fechada: uma linha por mudanca de
        // aliquota na historia do FGTS. Desde 1990 sao duas.
        var tabelas = await db.TabelasFgts
            .AsNoTracking()
            .OrderByDescending(t => t.VigenciaInicio)
            .ComTeto()
            .ToListAsync(ct);

        var hoje = DateOnly.FromDateTime(relogio.Agora.Date);
        var vigente = TabelaFgts.VigenteEm(tabelas, hoje);

        return Results.Ok(tabelas
            .Select(t => TabelaFgtsResposta.De(t, vigente is not null && t.Id == vigente.Id))
            .ToList());
    }

    private static async Task<IResult> CriarAsync(
        [FromBody] CriarTabelaFgtsRequisicao requisicao,
        PrismaRhDbContext db,
        IRelogio relogio,
        CancellationToken ct)
    {
        // O indice unico no banco tambem barra, mas devolveria violacao de
        // constraint. Aqui da para explicar o que aconteceu.
        if (await db.TabelasFgts.AnyAsync(t => t.VigenciaInicio == requisicao.VigenciaInicio, ct))
        {
            return Results.Conflict(new
            {
                detalhe = $"Ja existe aliquota de FGTS com vigencia a partir de {requisicao.VigenciaInicio:dd/MM/yyyy}."
            });
        }

        TabelaFgts tabela;

        try
        {
            tabela = new TabelaFgts(
                requisicao.VigenciaInicio,
                requisicao.Aliquota,
                requisicao.Fonte,
                relogio.Agora);
        }
        catch (ArgumentException erro)
        {
            return RespostasValidacao.De(erro);
        }

        db.TabelasFgts.Add(tabela);
        await db.SaveChangesAsync(ct);

        return Results.Created(
            $"/api/tabelas-fgts/{tabela.Id}",
            TabelaFgtsResposta.De(tabela, vigente: false));
    }
}
