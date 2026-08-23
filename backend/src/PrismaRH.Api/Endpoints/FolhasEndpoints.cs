using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrismaRH.Api.Identidade;
using PrismaRH.Aplicacao.Comum;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Dominio.Folha;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Api.Endpoints;

public sealed record AbrirFolhaRequisicao(Guid IdEmpresa, string Competencia);

public sealed record LancamentoManualRequisicao(Guid IdRubrica, decimal Valor, string? Referencia);

public sealed record FolhaResumoResposta(
    Guid Id,
    Guid IdEmpresa,
    string Empresa,
    string Competencia,
    SituacaoFolha Situacao,
    int VersaoCalculo,
    int QuantidadeFuncionarios,
    decimal TotalProventos,
    decimal TotalDescontos,
    decimal TotalLiquido,
    DateTimeOffset? CalculadaEm,
    DateTimeOffset? FechadaEm);

public sealed record HoleriteResumoResposta(
    Guid Id,
    Guid IdFuncionario,
    string Funcionario,
    string Matricula,
    int Avos,
    int Divisor,
    decimal SalarioReferencia,
    decimal TotalProventos,
    decimal TotalDescontos,
    decimal Liquido);

public sealed record FolhaDetalheResposta(
    FolhaResumoResposta Folha,
    IReadOnlyList<HoleriteResumoResposta> Funcionarios);

public sealed record LinhaMemoriaResposta(int Ordem, string Descricao, string Expressao, decimal Valor);

public sealed record LancamentoResposta(
    Guid Id,
    string CodigoRubrica,
    string NomeRubrica,
    TipoRubrica Tipo,
    OrigemLancamento Origem,
    string? Referencia,
    decimal Valor,
    int Ordem,
    IReadOnlyList<LinhaMemoriaResposta> Memoria);

public sealed record HoleriteResposta(
    HoleriteResumoResposta Resumo,
    string Competencia,
    SituacaoFolha SituacaoFolha,
    IReadOnlyList<LancamentoResposta> Lancamentos);

/// <summary>
/// A folha mensal. Os endpoints sao finos de proposito: abrir, calcular,
/// lancar e fechar sao decisoes do agregado FolhaPagamento, nao daqui.
/// </summary>
public static class FolhasEndpoints
{
    public static IEndpointRouteBuilder MapearFolhas(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/api/folhas").WithTags("Folhas");

        grupo.MapGet("/", ListarAsync)
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        grupo.MapGet("/{id:guid}", ObterAsync)
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        grupo.MapGet("/{id:guid}/funcionarios/{idHolerite:guid}", ObterHoleriteAsync)
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        grupo.MapPost("/", AbrirAsync)
            .RequireAuthorization(PoliticasAutorizacao.ProcessarFolha);

        grupo.MapPost("/{id:guid}/calcular", CalcularAsync)
            .RequireAuthorization(PoliticasAutorizacao.ProcessarFolha);

        grupo.MapPost("/{id:guid}/fechar", FecharAsync)
            .RequireAuthorization(PoliticasAutorizacao.ProcessarFolha);

        grupo.MapPost("/{id:guid}/funcionarios/{idHolerite:guid}/lancamentos", LancarAsync)
            .RequireAuthorization(PoliticasAutorizacao.ProcessarFolha);

        grupo.MapDelete("/{id:guid}/funcionarios/{idHolerite:guid}/lancamentos/{idLancamento:guid}", RemoverLancamentoAsync)
            .RequireAuthorization(PoliticasAutorizacao.ProcessarFolha);

        return rotas;
    }

    // -----------------------------------------------------------------------
    // Leitura
    // -----------------------------------------------------------------------

    private static async Task<IResult> ListarAsync(
        [FromQuery] Guid? idEmpresa,
        [FromQuery] string? competencia,
        PrismaRhDbContext db,
        CancellationToken ct)
    {
        var consulta = db.Folhas.AsNoTracking();

        if (idEmpresa is { } empresa)
        {
            consulta = consulta.Where(f => f.IdEmpresa == empresa);
        }

        if (!string.IsNullOrWhiteSpace(competencia))
        {
            if (!Competencia.TryParse(competencia, out var alvo))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["competencia"] = ["Competencia invalida. Use 08/2026."]
                });
            }

            consulta = consulta.Where(f => f.Competencia == alvo);
        }

        var folhas = await consulta
            .OrderByDescending(f => f.Competencia)
            .Join(db.Empresas, f => f.IdEmpresa, e => e.Id, (f, e) => new { Folha = f, e.RazaoSocial })
            .Select(x => new FolhaResumoResposta(
                x.Folha.Id,
                x.Folha.IdEmpresa,
                x.RazaoSocial,
                x.Folha.Competencia.ToString(),
                x.Folha.Situacao,
                x.Folha.VersaoCalculo,
                x.Folha.Funcionarios.Count,
                x.Folha.TotalProventos,
                x.Folha.TotalDescontos,
                x.Folha.TotalLiquido,
                x.Folha.CalculadaEm,
                x.Folha.FechadaEm))
            .ToListAsync(ct);

        return Results.Ok(folhas);
    }

    private static async Task<IResult> ObterAsync(Guid id, PrismaRhDbContext db, CancellationToken ct)
    {
        var resumo = await ResumoAsync(db, id, ct);

        if (resumo is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new FolhaDetalheResposta(resumo, await HoleritesAsync(db, id, ct)));
    }

    private static async Task<IResult> ObterHoleriteAsync(
        Guid id, Guid idHolerite, PrismaRhDbContext db, CancellationToken ct)
    {
        var folha = await db.Folhas.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);

        if (folha is null)
        {
            return Results.NotFound();
        }

        var resumo = (await HoleritesAsync(db, id, ct)).FirstOrDefault(h => h.Id == idHolerite);

        if (resumo is null)
        {
            return Results.NotFound();
        }

        var lancamentos = await db.LancamentosFolha
            .AsNoTracking()
            .Where(l => l.IdFolhaFuncionario == idHolerite)
            .OrderBy(l => l.Ordem)
            .Select(l => new LancamentoResposta(
                l.Id,
                l.CodigoRubrica,
                l.NomeRubrica,
                l.Tipo,
                l.Origem,
                l.Referencia,
                l.Valor,
                l.Ordem,
                l.Memoria
                    .OrderBy(m => m.Ordem)
                    .Select(m => new LinhaMemoriaResposta(m.Ordem, m.Descricao, m.Expressao, m.Valor))
                    .ToList()))
            .ToListAsync(ct);

        return Results.Ok(new HoleriteResposta(
            resumo, folha.Competencia.ToString(), folha.Situacao, lancamentos));
    }

    // -----------------------------------------------------------------------
    // Escrita
    // -----------------------------------------------------------------------

    private static async Task<IResult> AbrirAsync(
        [FromBody] AbrirFolhaRequisicao requisicao,
        PrismaRhDbContext db,
        IContextoUsuario usuario,
        IRelogio relogio,
        CancellationToken ct)
    {
        if (!Competencia.TryParse(requisicao.Competencia, out var competencia))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["competencia"] = ["Competencia invalida. Use 08/2026."]
            });
        }

        // O filtro global ja restringe a organizacao do token: se a empresa
        // for de outra organizacao, ela simplesmente nao existe nesta consulta.
        if (!await db.Empresas.AnyAsync(e => e.Id == requisicao.IdEmpresa, ct))
        {
            return Results.NotFound(new { detalhe = "Empresa nao encontrada." });
        }

        if (await db.Folhas.AnyAsync(
                f => f.IdEmpresa == requisicao.IdEmpresa && f.Competencia == competencia, ct))
        {
            return Results.Conflict(new
            {
                detalhe = $"A folha de {competencia} desta empresa ja foi aberta."
            });
        }

        var folha = new FolhaPagamento(usuario.IdOrganizacao, requisicao.IdEmpresa, competencia, relogio.Agora);

        db.Folhas.Add(folha);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/folhas/{folha.Id}", await ResumoAsync(db, folha.Id, ct));
    }

    private static async Task<IResult> CalcularAsync(
        Guid id, PrismaRhDbContext db, IRelogio relogio, CancellationToken ct)
    {
        var folha = await CarregarParaEscritaAsync(db, id, ct);

        if (folha is null)
        {
            return Results.NotFound();
        }

        var rubricaSalario = await db.Rubricas.FirstOrDefaultAsync(
            r => r.Ativa && r.Estrategia == EstrategiaRubrica.SalarioBaseProporcional, ct);

        if (rubricaSalario is null)
        {
            return Results.Conflict(new
            {
                detalhe = "Nenhuma rubrica de salario-base ativa. Cadastre uma antes de calcular."
            });
        }

        // Os contratos vem COM as vigencias: o motor nao acessa banco durante
        // o calculo (CLAUDE.md secao 10), entao tudo que ele precisa tem que
        // chegar carregado.
        var contratos = await db.ContratosTrabalho
            .Include(c => c.Vigencias)
            .Where(c => c.IdEmpresa == folha.IdEmpresa)
            .ToListAsync(ct);

        try
        {
            folha.Calcular(contratos, rubricaSalario, relogio.Agora);
        }
        catch (InvalidOperationException erro)
        {
            return RespostasValidacao.De(erro);
        }
        catch (ArgumentException erro)
        {
            return RespostasValidacao.De(erro);
        }

        await db.SaveChangesAsync(ct);

        return Results.Ok(new FolhaDetalheResposta(
            (await ResumoAsync(db, id, ct))!, await HoleritesAsync(db, id, ct)));
    }

    private static async Task<IResult> FecharAsync(
        Guid id, PrismaRhDbContext db, IRelogio relogio, CancellationToken ct)
    {
        var folha = await db.Folhas.Include(f => f.Funcionarios).FirstOrDefaultAsync(f => f.Id == id, ct);

        if (folha is null)
        {
            return Results.NotFound();
        }

        try
        {
            folha.Fechar(relogio.Agora);
        }
        catch (InvalidOperationException erro)
        {
            return RespostasValidacao.De(erro);
        }

        await db.SaveChangesAsync(ct);

        return Results.Ok(await ResumoAsync(db, id, ct));
    }

    private static async Task<IResult> LancarAsync(
        Guid id,
        Guid idHolerite,
        [FromBody] LancamentoManualRequisicao requisicao,
        PrismaRhDbContext db,
        CancellationToken ct)
    {
        var folha = await CarregarParaEscritaAsync(db, id, ct);

        if (folha is null)
        {
            return Results.NotFound();
        }

        var rubrica = await db.Rubricas.FirstOrDefaultAsync(r => r.Id == requisicao.IdRubrica, ct);

        if (rubrica is null)
        {
            return Results.NotFound(new { detalhe = "Rubrica nao encontrada." });
        }

        try
        {
            folha.AdicionarLancamentoManual(idHolerite, rubrica, requisicao.Valor, requisicao.Referencia);
        }
        catch (InvalidOperationException erro)
        {
            return RespostasValidacao.De(erro);
        }
        catch (ArgumentException erro)
        {
            return RespostasValidacao.De(erro);
        }

        await db.SaveChangesAsync(ct);

        return await ObterHoleriteAsync(id, idHolerite, db, ct);
    }

    private static async Task<IResult> RemoverLancamentoAsync(
        Guid id, Guid idHolerite, Guid idLancamento, PrismaRhDbContext db, CancellationToken ct)
    {
        var folha = await CarregarParaEscritaAsync(db, id, ct);

        if (folha is null)
        {
            return Results.NotFound();
        }

        bool removeu;

        try
        {
            removeu = folha.RemoverLancamento(idHolerite, idLancamento);
        }
        catch (InvalidOperationException erro)
        {
            return RespostasValidacao.De(erro);
        }

        if (!removeu)
        {
            return Results.NotFound();
        }

        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    // -----------------------------------------------------------------------
    // Apoio
    // -----------------------------------------------------------------------

    /// <summary>
    /// Carrega o agregado com os holerites e seus lancamentos.
    ///
    /// A memoria de calculo NAO vem junto: ela e apagada em cascata pelo banco
    /// quando um lancamento calculado e removido, e trazer todas as linhas de
    /// uma empresa inteira so para descartar seria desperdicio.
    /// </summary>
    private static Task<FolhaPagamento?> CarregarParaEscritaAsync(
        PrismaRhDbContext db, Guid id, CancellationToken ct) =>
        db.Folhas
            .Include(f => f.Funcionarios)
            .ThenInclude(ff => ff.Lancamentos)
            .FirstOrDefaultAsync(f => f.Id == id, ct);

    private static Task<FolhaResumoResposta?> ResumoAsync(
        PrismaRhDbContext db, Guid id, CancellationToken ct) =>
        db.Folhas
            .AsNoTracking()
            .Where(f => f.Id == id)
            .Join(db.Empresas, f => f.IdEmpresa, e => e.Id, (f, e) => new { Folha = f, e.RazaoSocial })
            .Select(x => new FolhaResumoResposta(
                x.Folha.Id,
                x.Folha.IdEmpresa,
                x.RazaoSocial,
                x.Folha.Competencia.ToString(),
                x.Folha.Situacao,
                x.Folha.VersaoCalculo,
                x.Folha.Funcionarios.Count,
                x.Folha.TotalProventos,
                x.Folha.TotalDescontos,
                x.Folha.TotalLiquido,
                x.Folha.CalculadaEm,
                x.Folha.FechadaEm))
            .FirstOrDefaultAsync(ct)!;

    private static async Task<IReadOnlyList<HoleriteResumoResposta>> HoleritesAsync(
        PrismaRhDbContext db, Guid idFolha, CancellationToken ct) =>
        await db.FolhasFuncionario
            .AsNoTracking()
            .Where(ff => ff.IdFolha == idFolha)
            .Join(db.Funcionarios, ff => ff.IdFuncionario, f => f.Id, (ff, f) => new { Holerite = ff, f.Nome })
            .Join(db.ContratosTrabalho, x => x.Holerite.IdContrato, c => c.Id,
                (x, c) => new { x.Holerite, x.Nome, c.Matricula })
            .OrderBy(x => x.Nome)
            .Select(x => new HoleriteResumoResposta(
                x.Holerite.Id,
                x.Holerite.IdFuncionario,
                x.Nome,
                x.Matricula,
                x.Holerite.Avos,
                x.Holerite.Divisor,
                x.Holerite.SalarioReferencia,
                x.Holerite.TotalProventos,
                x.Holerite.TotalDescontos,
                x.Holerite.Liquido))
            .ToListAsync(ct);
}
