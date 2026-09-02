using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrismaRH.Api.Identidade;
using PrismaRH.Dominio.Analises;
using PrismaRH.Dominio.Folha;
using PrismaRH.Dominio.Workflow;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Api.Endpoints;

public sealed record ContagemPorRotulo(string Rotulo, int Quantidade);

public sealed record PendenciaPorResponsavel(
    Guid? IdResponsavel, string Responsavel, int Quantidade);

public sealed record EvolucaoCompetencia(
    string Competencia, int Folhas, int Inconsistencias, int Resolvidas);

public sealed record PainelResposta(
    int FolhasCalculadas,
    int FolhasFechadas,
    int InconsistenciasTotais,
    int InconsistenciasPendentes,
    int InconsistenciasResolvidas,
    decimal? PercentualConformidade,
    IReadOnlyList<ContagemPorRotulo> PorSeveridade,
    IReadOnlyList<ContagemPorRotulo> PorStatus,
    IReadOnlyList<ContagemPorRotulo> PorRegra,
    IReadOnlyList<PendenciaPorResponsavel> PorResponsavel,
    IReadOnlyList<EvolucaoCompetencia> Evolucao);

/// <summary>
/// O painel operacional (Fase 7).
///
/// ## Todo numero daqui vem do banco
///
/// O criterio de aceite da fase e explicito: "dashboard usa dados reais do
/// sistema". Nao ha valor semeado, nao ha exemplo e nao ha numero calculado no
/// navegador - cada indicador e uma agregacao **sobre entidade filtrada**, e
/// nunca SQL montado a parte (`CLAUDE.md secao 24.5`).
///
/// ## Por que agregar no banco, e nao em memoria
///
/// A tentacao seria trazer as inconsistencias e contar em C#. Numa organizacao
/// com trinta folhas isso significa carregar milhares de linhas para devolver
/// seis numeros - e o custo cresce com o uso, que e a definicao de consulta que
/// vai doer depois.
///
/// `GroupBy` traduzido para SQL devolve so as contagens.
/// </summary>
public static class PainelEndpoints
{
    /// <summary>
    /// Teto de linhas nas listas do painel.
    ///
    /// Painel e leitura rapida: vinte responsaveis ja e mais do que alguem le
    /// numa tela. Sem teto, uma organizacao grande devolveria uma lista que
    /// cresce sem limite (`CLAUDE.md secao 24.18`).
    /// </summary>
    private const int TetoDeLinhas = 20;

    /// <summary>
    /// Quantas competencias entram na evolucao.
    ///
    /// Doze cobre um ano - o recorte que faz sentido para folha de pagamento.
    /// </summary>
    private const int CompetenciasNaEvolucao = 12;

    public static IEndpointRouteBuilder MapearPainel(this IEndpointRouteBuilder rotas)
    {
        rotas.MapGet("/api/painel", ObterAsync)
            .WithTags("Painel")
            .WithSummary("Indicadores operacionais da organizacao")
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        return rotas;
    }

    private static async Task<IResult> ObterAsync(
        PrismaRhDbContext db,
        CancellationToken ct,
        [FromQuery] Guid? idEmpresa = null)
    {
        var folhas = db.Folhas.AsNoTracking();
        var inconsistencias = db.ResultadosAnalise.AsNoTracking();

        if (idEmpresa is { } empresa)
        {
            folhas = folhas.Where(f => f.IdEmpresa == empresa);

            // A inconsistencia nao guarda a empresa: ela guarda a folha. O
            // filtro atravessa por ali, e continua sendo uma consulta so.
            inconsistencias = inconsistencias.Where(
                r => folhas.Any(f => f.Id == r.IdFolha));
        }

        var calculadas = await folhas.CountAsync(f => f.Situacao != SituacaoFolha.Rascunho, ct);
        var fechadas = await folhas.CountAsync(f => f.Situacao == SituacaoFolha.Fechada, ct);

        var total = await inconsistencias.CountAsync(ct);
        var resolvidas = await inconsistencias
            .CountAsync(r => r.Status == StatusInconsistencia.Resolvida, ct);

        var porSeveridade = await inconsistencias
            .GroupBy(r => r.Severidade)
            .Select(g => new { g.Key, Quantidade = g.Count() })
            .ToListAsync(ct);

        var porStatus = await inconsistencias
            .GroupBy(r => r.Status)
            .Select(g => new { g.Key, Quantidade = g.Count() })
            .ToListAsync(ct);

        var porRegra = await inconsistencias
            .GroupBy(r => r.Codigo)
            .Select(g => new { g.Key, Quantidade = g.Count() })
            .OrderByDescending(g => g.Quantidade)
            .Take(TetoDeLinhas)
            .ToListAsync(ct);

        // So as pendentes: "pendencias por responsavel" pergunta quem tem
        // trabalho na mao, e nao quem ja terminou.
        var porResponsavel = await inconsistencias
            .Where(r => r.Status != StatusInconsistencia.Resolvida)
            .GroupBy(r => r.IdResponsavel)
            .Select(g => new { g.Key, Quantidade = g.Count() })
            .OrderByDescending(g => g.Quantidade)
            .Take(TetoDeLinhas)
            .ToListAsync(ct);

        var evolucao = await EvolucaoAsync(db, folhas, inconsistencias, ct);

        var idsResponsaveis = porResponsavel
            .Where(g => g.Key is not null).Select(g => g.Key!.Value).ToList();

        var nomes = idsResponsaveis.Count == 0
            ? []
            : await db.Usuarios
                .AsNoTracking()
                .Where(u => idsResponsaveis.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Nome, ct);

        return Results.Ok(new PainelResposta(
            calculadas,
            fechadas,
            total,
            total - resolvidas,
            resolvidas,

            // Percentual de conformidade: quanto do que foi apontado ja esta
            // encerrado. NULO quando nao ha inconsistencia nenhuma - "100% de
            // conformidade" numa organizacao que nunca rodou analise seria uma
            // afirmacao que o sistema nao tem como sustentar.
            total == 0 ? null : Math.Round(resolvidas * 100m / total, 1),

            [.. porSeveridade
                .OrderByDescending(g => g.Key)
                .Select(g => new ContagemPorRotulo(g.Key.ToString(), g.Quantidade))],

            [.. porStatus
                .OrderBy(g => g.Key)
                .Select(g => new ContagemPorRotulo(g.Key.ToString(), g.Quantidade))],

            [.. porRegra.Select(g => new ContagemPorRotulo(
                CatalogoRegras.De(g.Key)?.Nome ?? g.Key.ToString(), g.Quantidade))],

            [.. porResponsavel.Select(g => new PendenciaPorResponsavel(
                g.Key,
                g.Key is { } id && nomes.TryGetValue(id, out var nome) ? nome : "Sem responsável",
                g.Quantidade))],

            evolucao));
    }

    /// <summary>
    /// Folhas e inconsistencias das ultimas competencias.
    ///
    /// A competencia e persistida como INT pelo conversor de valor, entao
    /// `x.Competencia.Ano` nao e traduzivel - a licao ja custou um 500 na
    /// Fase 4F. Aqui se agrupa pelo proprio valor convertido, que o EF entende.
    /// </summary>
    private static async Task<List<EvolucaoCompetencia>> EvolucaoAsync(
        PrismaRhDbContext db,
        IQueryable<FolhaPagamento> folhas,
        IQueryable<ResultadoAnalise> inconsistencias,
        CancellationToken ct)
    {
        var porCompetencia = await folhas
            .Where(f => f.Situacao != SituacaoFolha.Rascunho)
            .GroupBy(f => f.Competencia)
            .Select(g => new { Competencia = g.Key, Folhas = g.Count() })
            .OrderByDescending(g => g.Competencia)
            .Take(CompetenciasNaEvolucao)
            .ToListAsync(ct);

        if (porCompetencia.Count == 0)
        {
            return [];
        }

        var competencias = porCompetencia.Select(g => g.Competencia).ToList();

        // As inconsistencias vem pela EXECUCAO, que guarda a competencia. O
        // resultado nao a guarda de proposito: duplicar dado que a execucao ja
        // tem seria a copia que envelhece.
        var achados = await db.ExecucoesAnalise
            .AsNoTracking()
            .Where(e => competencias.Contains(e.Competencia))
            .Join(
                inconsistencias,
                e => e.Id,
                r => r.IdExecucaoAnalise,
                (e, r) => new { e.Competencia, r.Status })
            .GroupBy(x => x.Competencia)
            .Select(g => new
            {
                Competencia = g.Key,
                Total = g.Count(),
                Resolvidas = g.Count(x => x.Status == StatusInconsistencia.Resolvida),
            })
            .ToListAsync(ct);

        var porChave = achados.ToDictionary(a => a.Competencia);

        return [.. porCompetencia
            .OrderBy(g => g.Competencia)
            .Select(g =>
            {
                porChave.TryGetValue(g.Competencia, out var achado);

                return new EvolucaoCompetencia(
                    g.Competencia.ToString(),
                    g.Folhas,
                    achado?.Total ?? 0,
                    achado?.Resolvidas ?? 0);
            })];
    }
}
