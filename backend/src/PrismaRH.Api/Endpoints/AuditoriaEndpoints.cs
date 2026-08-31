using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrismaRH.Api.Identidade;
using PrismaRH.Dominio.Auditoria;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Api.Endpoints;

public sealed record EventoResposta(
    Guid Id,
    string Acao,
    string Entidade,
    Guid IdEntidade,
    string? Usuario,
    string Descricao,
    string? Contexto,
    DateTimeOffset OcorridoEm);

/// <summary>
/// A trilha de auditoria de negocio (Fase 7).
///
/// ## Somente LEITURA, e isso e a funcionalidade
///
/// Existem duas rotas, e as duas sao `GET`. **Nao ha `POST`, `PUT`, `PATCH` nem
/// `DELETE` aqui** - para perfil nenhum, inclusive Administrador da Plataforma.
///
/// Nao e esquecimento: o `CLAUDE.md secao 24.17` diz que registro de auditoria
/// nao e alteravel por usuario comum, de nenhum perfil, e o Security Gate da
/// Fase 7 repete. Uma trilha que alguem pode editar nao e trilha - e um campo
/// de texto com nome pomposo.
///
/// A gravacao acontece **dentro da transacao da operacao que gerou o evento**,
/// pelo `Auditar.Registrar`. Ou os dois acontecem, ou nenhum dos dois: uma
/// auditoria gravada por fora registraria alteracoes que o banco depois
/// desfez.
///
/// ## Isto nao substitui o log tecnico, nem e substituido por ele
///
/// `CLAUDE.md secao 26`. O log tecnico e rotativo e descartavel, e responde
/// "por que a requisicao demorou". Esta tabela responde "quem alterou o salario
/// dela, quando, e de quanto para quanto" - pergunta que pode aparecer anos
/// depois.
/// </summary>
public static class AuditoriaEndpoints
{
    public static IEndpointRouteBuilder MapearAuditoria(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/api/auditoria").WithTags("Auditoria");

        grupo.MapGet("/", ListarAsync)
            .WithSummary("Eventos da organizacao, com filtros e paginacao")
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        grupo.MapGet("/{entidade}/{id:guid}", PorEntidadeAsync)
            .WithSummary("Tudo o que aconteceu com uma entidade")
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        return rotas;
    }

    private static async Task<IResult> ListarAsync(
        PrismaRhDbContext db,
        CancellationToken ct,
        [FromQuery] string? acao = null,
        [FromQuery] string? entidade = null,
        [FromQuery] Guid? idUsuario = null,
        [FromQuery] DateTimeOffset? de = null,
        [FromQuery] DateTimeOffset? ate = null,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanho = 50)
    {
        var consulta = db.EventosAuditoria.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(acao))
        {
            if (!Enum.TryParse<AcaoAuditada>(acao, true, out var valor))
            {
                return Results.BadRequest(new { detalhe = "Acao desconhecida." });
            }

            consulta = consulta.Where(e => e.Acao == valor);
        }

        if (!string.IsNullOrWhiteSpace(entidade))
        {
            if (!Enum.TryParse<EntidadeAuditada>(entidade, true, out var valor))
            {
                return Results.BadRequest(new { detalhe = "Entidade desconhecida." });
            }

            consulta = consulta.Where(e => e.Entidade == valor);
        }

        if (idUsuario is { } usuario)
        {
            consulta = consulta.Where(e => e.IdUsuario == usuario);
        }

        if (de is { } inicio)
        {
            consulta = consulta.Where(e => e.OcorridoEm >= inicio);
        }

        if (ate is { } fim)
        {
            consulta = consulta.Where(e => e.OcorridoEm <= fim);
        }

        return Results.Ok(await PaginarAsync(db, consulta, pagina, tamanho, ct));
    }

    private static async Task<IResult> PorEntidadeAsync(
        string entidade,
        Guid id,
        PrismaRhDbContext db,
        CancellationToken ct,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanho = 50)
    {
        if (!Enum.TryParse<EntidadeAuditada>(entidade, true, out var valor))
        {
            return Results.NotFound();
        }

        var consulta = db.EventosAuditoria
            .AsNoTracking()
            .Where(e => e.Entidade == valor && e.IdEntidade == id);

        return Results.Ok(await PaginarAsync(db, consulta, pagina, tamanho, ct));
    }

    private static async Task<object> PaginarAsync(
        PrismaRhDbContext db,
        IQueryable<EventoAuditoria> consulta,
        int pagina,
        int tamanho,
        CancellationToken ct)
    {
        // Teto de 200 aqui, e nao de 100 como nas demais: auditoria e lida em
        // bloco para conferir um periodo, e paginar de cinquenta em cinquenta
        // uma trilha de mil eventos e trabalho manual sem proposito. Continua
        // sendo teto - consulta sem limite e proibida (`CLAUDE.md secao 24.18`).
        var porPagina = Math.Clamp(tamanho, 1, 200);
        var salto = (Math.Max(pagina, 1) - 1) * porPagina;

        var total = await consulta.CountAsync(ct);

        var eventos = await consulta
            .OrderByDescending(e => e.OcorridoEm)
            .ThenByDescending(e => e.Id)
            .Skip(salto)
            .Take(porPagina)
            .ToListAsync(ct);

        // Nomes sob o filtro global: usuario de outra organizacao nao volta, e
        // a tela mostra o evento sem nome em vez de vazar o de fora.
        var ids = eventos.Where(e => e.IdUsuario is not null)
            .Select(e => e.IdUsuario!.Value).Distinct().ToList();

        var nomes = ids.Count == 0
            ? []
            : await db.Usuarios
                .AsNoTracking()
                .Where(u => ids.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Nome, ct);

        return new
        {
            Total = total,
            Pagina = Math.Max(pagina, 1),
            Itens = eventos.Select(e => new EventoResposta(
                e.Id,
                e.Acao.ToString(),
                e.Entidade.ToString(),
                e.IdEntidade,
                e.IdUsuario is { } u && nomes.TryGetValue(u, out var nome) ? nome : null,
                e.Descricao,
                e.Contexto,
                e.OcorridoEm)).ToList(),
        };
    }
}
