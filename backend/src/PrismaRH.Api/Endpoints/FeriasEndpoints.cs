using PrismaRH.Api.Producao;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrismaRH.Api.Identidade;
using PrismaRH.Aplicacao.Comum;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Dominio.Ferias;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Api.Endpoints;

/// <summary>
/// Entrada propria, sem Id nem IdOrganizacao (CLAUDE.md secao 24.7).
///
/// O periodo aquisitivo entra pelas DATAS, e nao por um id: ele nao tem tabela.
/// </summary>
public sealed record ConcederFeriasRequisicao(
    DateOnly InicioPeriodoAquisitivo,
    DateOnly Inicio,
    int Dias,
    int DiasAbonoPecuniario);

public sealed record ConcessaoResposta(
    Guid Id,
    DateOnly InicioPeriodoAquisitivo,
    DateOnly FimPeriodoAquisitivo,
    DateOnly Inicio,
    DateOnly Fim,
    int Dias,
    int DiasAbonoPecuniario,
    int DiasBaixados,
    SituacaoConcessao Situacao,
    bool PodeCancelar)
{
    public static ConcessaoResposta De(ConcessaoFerias c, DateOnly hoje) =>
        new(c.Id, c.InicioPeriodoAquisitivo, c.FimPeriodoAquisitivo, c.Inicio, c.Fim,
            c.Dias, c.DiasAbonoPecuniario, c.DiasBaixados,
            c.SituacaoEm(hoje), c.PodeSerCancelada(hoje));
}

public sealed record PeriodoAquisitivoResposta(
    int Numero,
    DateOnly Inicio,
    DateOnly Fim,
    DateOnly InicioConcessao,
    DateOnly LimiteConcessao,
    int DiasDireito,
    SituacaoPeriodoAquisitivo Situacao,
    int DiasParaCompletar,
    bool EmDobra,
    int DiasConcedidos,
    int Saldo,
    int SaldoAbono,
    int FracoesUsadas,
    IReadOnlyList<ConcessaoResposta> Concessoes)
{
    public static PeriodoAquisitivoResposta De(PeriodoComSaldo p, DateOnly hoje) =>
        new(p.Periodo.Numero, p.Periodo.Inicio, p.Periodo.Fim,
            p.Periodo.InicioConcessao, p.Periodo.LimiteConcessao, p.Periodo.DiasDireito,
            p.Periodo.SituacaoEm(hoje), p.Periodo.DiasParaCompletar(hoje),
            p.Periodo.EmDobraSeConcedidoEm(hoje),
            p.DiasConcedidos, p.Saldo, p.SaldoAbono, p.FracoesUsadas,
            [.. p.Concessoes.OrderBy(c => c.Inicio).Select(c => ConcessaoResposta.De(c, hoje))]);
}

public sealed record FeriasDoContratoResposta(
    Guid IdContrato,
    string Matricula,
    DateOnly DataAdmissao,
    DateOnly? DataDesligamento,
    DateOnly Referencia,
    int DiasAdquiridos,
    int SaldoTotal,
    int PeriodosVencidos,
    IReadOnlyList<PeriodoAquisitivoResposta> Periodos);

/// <summary>
/// Ferias de um contrato: o direito (periodos aquisitivos) e a programacao
/// (concessoes).
///
/// O periodo aquisitivo e DERIVADO do calendario e nao tem tabela; a concessao
/// TEM estado e tem. Ver as notas em PeriodosAquisitivos e ConcessaoFerias.
///
/// Esta etapa NAO paga nada. Ela registra que a pessoa vai gozar N dias a
/// partir de tal data - o calculo da remuneracao, do terco e do abono e a
/// etapa seguinte.
///
/// Rota ANINHADA no contrato: a concessao e resolvida pelo pai, que ja passa
/// pelo filtro global (CLAUDE.md secao 24.6).
/// </summary>
public static class FeriasEndpoints
{
    public static IEndpointRouteBuilder MapearFerias(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/api/contratos/{idContrato:guid}/ferias")
            .WithTags("Ferias");

        grupo.MapGet("/periodos", ListarPeriodosAsync)
            .WithSummary("Periodos aquisitivos, com saldo e concessoes")
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        grupo.MapPost("/concessoes", ConcederAsync)
            .WithSummary("Programa ferias de um periodo aquisitivo")
            .RequireAuthorization(PoliticasAutorizacao.AdministrarPessoas);

        grupo.MapDelete("/concessoes/{id:guid}", CancelarAsync)
            .WithSummary("Cancela uma concessao que ainda nao comecou")
            .RequireAuthorization(PoliticasAutorizacao.AdministrarPessoas);

        return rotas;
    }

    /// <summary>
    /// Junta os periodos derivados com as concessoes gravadas.
    ///
    /// Uma consulta so para as concessoes do contrato, e o casamento acontece
    /// em memoria: sao poucas linhas por contrato, e uma consulta por periodo
    /// faria N chamadas ao banco para responder uma tela.
    /// </summary>
    private static List<PeriodoComSaldo> Montar(
        IReadOnlyList<PeriodoAquisitivo> periodos,
        IReadOnlyList<ConcessaoFerias> concessoes) =>
        [.. periodos.Select(p => new PeriodoComSaldo(
            p, [.. concessoes.Where(c => c.EDoPeriodo(p))]))];

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

        var concessoes = await db.ConcessoesFerias
            .AsNoTracking()
            .Where(c => c.IdContrato == idContrato)
            .ComTeto()
            .ToListAsync(ct);

        var periodos = Montar(PeriodosAquisitivos.De(contrato, hoje), concessoes);

        var adquiridos = periodos
            .Where(p => p.Periodo.SituacaoEm(hoje) != SituacaoPeriodoAquisitivo.EmAndamento)
            .ToList();

        return Results.Ok(new FeriasDoContratoResposta(
            contrato.Id,
            contrato.Matricula,
            contrato.DataAdmissao,
            contrato.DataDesligamento,
            hoje,
            adquiridos.Sum(p => p.Periodo.DiasDireito),
            adquiridos.Sum(p => p.Saldo),
            adquiridos.Count(p => p.Periodo.SituacaoEm(hoje) == SituacaoPeriodoAquisitivo.Vencido),
            [.. periodos.Select(p => PeriodoAquisitivoResposta.De(p, hoje))]));
    }

    private static async Task<IResult> ConcederAsync(
        Guid idContrato,
        [FromBody] ConcederFeriasRequisicao requisicao,
        PrismaRhDbContext db,
        IContextoUsuario usuario,
        IRelogio relogio,
        CancellationToken ct)
    {
        var contrato = await db.ContratosTrabalho
            .FirstOrDefaultAsync(c => c.Id == idContrato, ct);

        if (contrato is null)
        {
            return Results.NotFound();
        }

        var hoje = DateOnly.FromDateTime(relogio.Agora.Date);

        // O periodo e procurado entre os DERIVADOS, e nao aceito como o
        // cliente mandou: assim ninguem inventa um periodo que o contrato nao
        // tem. A referencia vai ate o inicio pretendido para que se possa
        // programar ferias de um periodo que ainda vai completar.
        var referencia = requisicao.Inicio > hoje ? requisicao.Inicio : hoje;

        var periodo = PeriodosAquisitivos.De(contrato, referencia)
            .FirstOrDefault(p => p.Inicio == requisicao.InicioPeriodoAquisitivo);

        if (periodo is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["inicioPeriodoAquisitivo"] =
                    ["Este contrato nao tem periodo aquisitivo comecando nessa data."]
            });
        }

        var concessoes = await db.ConcessoesFerias
            .AsNoTracking()
            .Where(c => c.IdContrato == idContrato)
            .ComTeto()
            .ToListAsync(ct);

        var comSaldo = new PeriodoComSaldo(periodo, [.. concessoes.Where(c => c.EDoPeriodo(periodo))]);

        var recusas = RegrasDeConcessao.Conferir(
            comSaldo, requisicao.Dias, requisicao.DiasAbonoPecuniario, requisicao.Inicio);

        if (recusas.Count > 0)
        {
            // Todas de uma vez: quem preenche o formulario merece ver tudo que
            // esta errado, e nao descobrir um problema por tentativa.
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["concessao"] = [.. recusas.Select(RegrasDeConcessao.Explicar)]
            });
        }

        ConcessaoFerias concessao;

        try
        {
            concessao = new ConcessaoFerias(
                usuario.IdOrganizacao,
                idContrato,
                periodo,
                requisicao.Inicio,
                requisicao.Dias,
                requisicao.DiasAbonoPecuniario,
                relogio.Agora);
        }
        catch (ArgumentException erro)
        {
            return RespostasValidacao.De(erro);
        }

        db.ConcessoesFerias.Add(concessao);
        await db.SaveChangesAsync(ct);

        return Results.Created(
            $"/api/contratos/{idContrato}/ferias/concessoes/{concessao.Id}",
            ConcessaoResposta.De(concessao, hoje));
    }

    private static async Task<IResult> CancelarAsync(
        Guid idContrato,
        Guid id,
        PrismaRhDbContext db,
        IRelogio relogio,
        CancellationToken ct)
    {
        // Resolvida pelo PAI e pelo proprio id: trocar o idContrato da URL por
        // outro nao alcanca a concessao de ninguem.
        var concessao = await db.ConcessoesFerias
            .FirstOrDefaultAsync(c => c.Id == id && c.IdContrato == idContrato, ct);

        if (concessao is null)
        {
            return Results.NotFound();
        }

        var hoje = DateOnly.FromDateTime(relogio.Agora.Date);

        if (!concessao.PodeSerCancelada(hoje))
        {
            return Results.Conflict(new
            {
                detalhe = "Ferias que ja comecaram nao se cancelam por aqui: "
                    + "envolve retorno ao trabalho e acerto do que foi pago."
            });
        }

        db.ConcessoesFerias.Remove(concessao);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
