using Microsoft.EntityFrameworkCore;
using PrismaRH.Api.Identidade;
using PrismaRH.Aplicacao.Comum;
using PrismaRH.Dominio.Contratos;
using PrismaRH.Dominio.Ferias;
using PrismaRH.Dominio.Folha;
using PrismaRH.Dominio.Rescisao;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Api.Endpoints;

public sealed record VerbaResposta(
    string Codigo, string Nome, decimal Valor, string Referencia,
    IReadOnlyList<LinhaMemoriaResposta> Memoria);

public sealed record AvisoResposta(
    DevedorDoAviso Devedor, int AnosCompletos, int DiasBase,
    int DiasAcrescidos, int Dias, bool Reduzido);

public sealed record MesProporcionalResposta(
    DateOnly Inicio, DateOnly Fim, int Dias, bool Conta, string Motivo);

public sealed record FeriasProporcionaisResposta(
    DateOnly InicioPeriodo, DateOnly FimPeriodo, int Avos, string Fracao,
    IReadOnlyList<MesProporcionalResposta> Meses);

public sealed record ValorBaseFgtsResposta(
    decimal Informado, decimal ConhecidoPeloSistema, bool AbaixoDoConhecido);

public sealed record RescisaoResposta(
    Guid IdContrato,
    string Matricula,
    MotivoDesligamento Motivo,
    DateOnly DataDesligamento,
    decimal SalarioReferencia,
    bool Suportado,
    string? MotivoDoBloqueio,
    string Fonte,
    AvisoResposta? Aviso,
    FeriasProporcionaisResposta? FeriasProporcionais,
    int DiasFeriasVencidas,
    int Avos13,
    string? Fracao13,
    ValorBaseFgtsResposta? ValorBaseFgts,
    decimal Total,
    IReadOnlyList<VerbaResposta> Verbas);

/// <summary>
/// A rescisao de um contrato desligado.
///
/// SIMULACAO, nao folha. Ela responde "quanto esta rescisao vale e por que",
/// e nao gera holerite - a folha de rescisao e a etapa seguinte. Por isso a
/// rota e GET e nao grava nada.
///
/// O VALOR BASE DO FGTS entra por parametro, informado pelo analista. Ele NAO
/// e calculado: o saldo real da conta vinculada inclui correcao e juros que o
/// Prisma RH nao conhece. O que o sistema sabe - a soma dos depositos que ele
/// mesmo apurou - volta na resposta para comparacao, nunca como substituto.
///
/// TRES MOTIVOS SAO BLOQUEADOS. Para eles a resposta vem com Suportado=false e
/// a razao por escrito, mas COM o contexto (avos, dias, datas): quem le
/// precisa entender o que falta, e nao apenas receber um erro.
/// </summary>
public static class RescisaoEndpoints
{
    public static IEndpointRouteBuilder MapearRescisao(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/api/contratos/{idContrato:guid}/rescisao")
            .WithTags("Rescisao");

        grupo.MapGet("/", ApurarAsync)
            .WithSummary("Simula as verbas rescisorias do contrato desligado")
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        grupo.MapGet("/matriz", MatrizAsync)
            .WithSummary("O que cada motivo de desligamento gera, com a fonte")
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        return rotas;
    }

    private static IResult MatrizAsync() =>
        Results.Ok(MatrizVerbasRescisorias.Todas
            .OrderBy(v => v.Motivo)
            .Select(v => new
            {
                v.Motivo,
                v.Suportado,
                v.DevedorDoAviso,
                v.AvisoPelaMetade,
                v.FeriasProporcionais,
                PercentualMultaFgts = v.PercentualMultaFgts * 100m,
                v.Fonte,
                v.MotivoDoBloqueio,
            })
            .ToList());

    private static async Task<IResult> ApurarAsync(
        Guid idContrato,
        decimal? valorBaseFgts,
        PrismaRhDbContext db,
        IRelogio relogio,
        CancellationToken ct)
    {
        // Passa pelo filtro global: contrato de outra organizacao nao existe
        // daqui, e a resposta e 404 - nunca 403.
        var contrato = await db.ContratosTrabalho
            .Include(c => c.Vigencias)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == idContrato, ct);

        if (contrato is null)
        {
            return Results.NotFound();
        }

        if (contrato.DataDesligamento is not { } desligamento || contrato.MotivoDesligamento is null)
        {
            return Results.Conflict(new
            {
                detalhe = "Contrato ainda esta ativo: nao ha rescisao a apurar."
            });
        }

        // CLT art. 477 e seguintes: o salario de referencia e o da data do
        // desligamento. Mesma logica das ferias (art. 142).
        var vigencia = contrato.VigenciaEm(desligamento);
        var salario = vigencia?.Salario ?? 0m;

        // Ferias VENCIDAS: os periodos ja adquiridos e ainda com saldo, na
        // data do desligamento.
        var concessoes = await db.ConcessoesFerias
            .AsNoTracking()
            .Where(c => c.IdContrato == idContrato)
            .ToListAsync(ct);

        var diasVencidas = PeriodosAquisitivos.Adquiridos(contrato, desligamento)
            .Select(p => new PeriodoComSaldo(p, [.. concessoes.Where(c => c.EDoPeriodo(p))]))
            .Sum(p => p.Saldo);

        // O que o SISTEMA conhece de FGTS: a soma dos lancamentos de FGTS nos
        // holerites deste contrato. Serve para comparar com o informado, nunca
        // para substitui-lo.
        var conhecido = await db.LancamentosFolha
            .AsNoTracking()
            .Where(l => l.Estrategia == EstrategiaRubrica.FgtsMensal
                        && db.FolhasFuncionario.Any(f => f.Id == l.IdFolhaFuncionario
                                                         && f.IdContrato == idContrato))
            .SumAsync(l => (decimal?)l.Valor, ct) ?? 0m;

        var baseFgts = valorBaseFgts is { } informado
            ? new ValorBaseFgts(informado, conhecido)
            : null;

        Dominio.Rescisao.Rescisao apuracao;

        try
        {
            apuracao = CalculadoraRescisao.Apurar(contrato, salario, diasVencidas, baseFgts);
        }
        catch (ArgumentException erro)
        {
            return RespostasValidacao.De(erro);
        }

        return Results.Ok(new RescisaoResposta(
            contrato.Id,
            contrato.Matricula,
            apuracao.Motivo,
            apuracao.DataDesligamento,
            apuracao.SalarioReferencia,
            apuracao.Suportado,
            apuracao.MotivoDoBloqueio,
            apuracao.Fonte,
            apuracao.Aviso is { } a
                ? new AvisoResposta(a.Devedor, a.AnosCompletos, a.DiasBase, a.DiasAcrescidos, a.Dias, a.Reduzido)
                : null,
            apuracao.FeriasProporcionais is { } f
                ? new FeriasProporcionaisResposta(
                    f.InicioPeriodo, f.FimPeriodo, f.Avos, f.Fracao,
                    [.. f.Meses.Select(m => new MesProporcionalResposta(m.Inicio, m.Fim, m.Dias, m.Conta, m.Motivo))])
                : null,
            apuracao.DiasFeriasVencidas,
            apuracao.Avos13?.Avos ?? 0,
            apuracao.Avos13?.Fracao,
            apuracao.ValorBaseFgts is { } b
                ? new ValorBaseFgtsResposta(b.Informado, b.ConhecidoPeloSistema, b.AbaixoDoConhecido)
                : null,
            apuracao.Total,
            [.. apuracao.Verbas.Select(v => new VerbaResposta(
                v.Codigo, v.Nome, v.Valor, v.Referencia,
                [.. v.Passos.Select((p, i) => new LinhaMemoriaResposta(i + 1, p.Descricao, p.Expressao, p.Valor))]))]));
    }
}
