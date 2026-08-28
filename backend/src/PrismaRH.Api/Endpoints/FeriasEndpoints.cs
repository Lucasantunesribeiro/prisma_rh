using Microsoft.EntityFrameworkCore;
using PrismaRH.Api.Identidade;
using PrismaRH.Aplicacao.Comum;
using PrismaRH.Dominio.Ferias;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Api.Endpoints;

public sealed record PeriodoAquisitivoResposta(
    int Numero,
    DateOnly Inicio,
    DateOnly Fim,
    DateOnly InicioConcessao,
    DateOnly LimiteConcessao,
    int DiasDireito,
    SituacaoPeriodoAquisitivo Situacao,
    int DiasParaCompletar,
    bool EmDobra)
{
    public static PeriodoAquisitivoResposta De(PeriodoAquisitivo p, DateOnly hoje) =>
        new(p.Numero, p.Inicio, p.Fim, p.InicioConcessao, p.LimiteConcessao,
            p.DiasDireito, p.SituacaoEm(hoje), p.DiasParaCompletar(hoje),
            p.EmDobraSeConcedidoEm(hoje));
}

public sealed record FeriasDoContratoResposta(
    Guid IdContrato,
    string Matricula,
    DateOnly DataAdmissao,
    DateOnly? DataDesligamento,
    DateOnly Referencia,
    int DiasAdquiridos,
    int PeriodosVencidos,
    IReadOnlyList<PeriodoAquisitivoResposta> Periodos);

/// <summary>
/// Os periodos aquisitivos de ferias de um contrato.
///
/// SOMENTE LEITURA, e isso e a propria natureza da etapa: nao ha nada para
/// gravar. O periodo aquisitivo e derivado da data de admissao e do calendario
/// (ver PeriodosAquisitivos). Quem tem estado e a CONCESSAO de ferias, que
/// chega na etapa 2 e ai sim tera POST proprio.
///
/// Rota ANINHADA no contrato: o periodo e resolvido pelo pai, que ja passa
/// pelo filtro global. Um id de contrato de outra organizacao nao encontra
/// caminho (CLAUDE.md secao 24.6).
/// </summary>
public static class FeriasEndpoints
{
    public static IEndpointRouteBuilder MapearFerias(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/api/contratos/{idContrato:guid}/ferias")
            .WithTags("Ferias");

        grupo.MapGet("/periodos", ListarPeriodosAsync)
            .WithSummary("Periodos aquisitivos de ferias do contrato")
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        return rotas;
    }

    private static async Task<IResult> ListarPeriodosAsync(
        Guid idContrato,
        DateOnly? referencia,
        PrismaRhDbContext db,
        IRelogio relogio,
        CancellationToken ct)
    {
        // Passa pelo filtro global: contrato de outra organizacao nao existe
        // daqui, e a resposta e 404 - nunca 403, que confirmaria o id.
        var contrato = await db.ContratosTrabalho
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == idContrato, ct);

        if (contrato is null)
        {
            return Results.NotFound();
        }

        // A data entra por parametro para a tela poder perguntar "e em
        // dezembro, quantos periodos estarao vencidos?". Sem ela, hoje.
        var hoje = referencia ?? DateOnly.FromDateTime(relogio.Agora.Date);

        var periodos = PeriodosAquisitivos.De(contrato, hoje);

        var adquiridos = periodos
            .Where(p => p.SituacaoEm(hoje) != SituacaoPeriodoAquisitivo.EmAndamento)
            .ToList();

        return Results.Ok(new FeriasDoContratoResposta(
            contrato.Id,
            contrato.Matricula,
            contrato.DataAdmissao,
            contrato.DataDesligamento,
            hoje,
            adquiridos.Sum(p => p.DiasDireito),
            adquiridos.Count(p => p.SituacaoEm(hoje) == SituacaoPeriodoAquisitivo.Vencido),
            [.. periodos.Select(p => PeriodoAquisitivoResposta.De(p, hoje))]));
    }
}
