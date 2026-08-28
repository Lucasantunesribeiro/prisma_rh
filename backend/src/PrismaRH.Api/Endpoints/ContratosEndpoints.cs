using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrismaRH.Api.Identidade;
using PrismaRH.Aplicacao.Comum;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Dominio.Contratos;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Api.Endpoints;

public sealed record CriarContratoRequisicao(
    Guid IdEmpresa,
    string Matricula,
    DateOnly DataAdmissao,
    decimal SalarioInicial,
    Guid IdCargo,
    Guid IdEstabelecimento,
    int JornadaMensalHoras);

public sealed record RegistrarAlteracaoRequisicao(
    DateOnly ValidoDe,
    decimal Salario,
    Guid IdCargo,
    Guid IdEstabelecimento,
    int JornadaMensalHoras,
    MotivoVigencia Motivo);

public sealed record DesligarRequisicao(DateOnly DataDesligamento, MotivoDesligamento Motivo);

public sealed record VigenciaResposta(
    Guid Id,
    DateOnly ValidoDe,
    DateOnly? ValidoAte,
    decimal Salario,
    Guid IdCargo,
    Guid IdEstabelecimento,
    int JornadaMensalHoras,
    MotivoVigencia Motivo)
{
    public static VigenciaResposta De(VigenciaContrato v) =>
        new(v.Id, v.ValidoDe, v.ValidoAte, v.Salario, v.IdCargo,
            v.IdEstabelecimento, v.JornadaMensalHoras, v.Motivo);
}

public sealed record ContratoResposta(
    Guid Id,
    Guid IdFuncionario,
    Guid IdEmpresa,
    string Matricula,
    DateOnly DataAdmissao,
    DateOnly? DataDesligamento,
    MotivoDesligamento? MotivoDesligamento,
    SituacaoContrato Situacao,
    VigenciaResposta? VigenciaAtual)
{
    public static ContratoResposta De(ContratoTrabalho c) =>
        new(c.Id, c.IdFuncionario, c.IdEmpresa, c.Matricula, c.DataAdmissao,
            c.DataDesligamento, c.MotivoDesligamento, c.Situacao,
            c.VigenciaAtual is null ? null : VigenciaResposta.De(c.VigenciaAtual));
}

public static class ContratosEndpoints
{
    public static IEndpointRouteBuilder MapearContratos(this IEndpointRouteBuilder rotas)
    {
        var deFuncionario = rotas.MapGroup("/api/funcionarios/{idFuncionario:guid}/contratos")
            .WithTags("Contratos");

        deFuncionario.MapGet("/", ListarDoFuncionarioAsync)
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        deFuncionario.MapPost("/", CriarAsync)
            .RequireAuthorization(PoliticasAutorizacao.AdministrarPessoas);

        var contrato = rotas.MapGroup("/api/contratos/{id:guid}").WithTags("Contratos");

        contrato.MapGet("/vigencias", ListarVigenciasAsync)
            .WithSummary("Historico completo do contrato")
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        contrato.MapGet("/vigencia", ObterVigenciaEmAsync)
            .WithSummary("O que valia numa data - a consulta que o calculo fara")
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        contrato.MapPost("/vigencias", RegistrarAlteracaoAsync)
            .RequireAuthorization(PoliticasAutorizacao.AdministrarPessoas);

        contrato.MapPost("/desligamento", DesligarAsync)
            .RequireAuthorization(PoliticasAutorizacao.AdministrarPessoas);

        return rotas;
    }

    /// <summary>
    /// Carrega o agregado inteiro. O contrato so sabe impor as regras de
    /// vigencia com todas elas em maos - carregar so o cabecalho faria
    /// RegistrarAlteracao decidir sobre um historico que ele nao enxerga.
    /// </summary>
    private static Task<ContratoTrabalho?> CarregarAsync(Guid id, PrismaRhDbContext db, CancellationToken ct) =>
        db.ContratosTrabalho.Include(c => c.Vigencias).FirstOrDefaultAsync(c => c.Id == id, ct);

    private static async Task<IResult> ListarDoFuncionarioAsync(
        Guid idFuncionario,
        PrismaRhDbContext db,
        CancellationToken ct)
    {
        if (!await db.Funcionarios.AnyAsync(f => f.Id == idFuncionario, ct))
        {
            return Results.NotFound();
        }

        var contratos = await db.ContratosTrabalho
            .Include(c => c.Vigencias)
            .AsNoTracking()
            .Where(c => c.IdFuncionario == idFuncionario)
            .OrderByDescending(c => c.DataAdmissao)
            .ToListAsync(ct);

        return Results.Ok(contratos.Select(ContratoResposta.De).ToList());
    }

    private static async Task<IResult> CriarAsync(
        Guid idFuncionario,
        [FromBody] CriarContratoRequisicao requisicao,
        PrismaRhDbContext db,
        IContextoUsuario usuario,
        IRelogio relogio,
        CancellationToken ct)
    {
        // Todas as checagens abaixo passam pelo filtro global: se a empresa, o
        // cargo ou o estabelecimento forem de outra organizacao, simplesmente
        // nao existem daqui, e a resposta e 404.
        if (!await db.Funcionarios.AnyAsync(f => f.Id == idFuncionario, ct))
        {
            return Results.NotFound();
        }

        if (!await db.Empresas.AnyAsync(e => e.Id == requisicao.IdEmpresa, ct))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["idEmpresa"] = ["Empresa nao encontrada."]
            });
        }

        if (!await db.Cargos.AnyAsync(c => c.Id == requisicao.IdCargo, ct))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["idCargo"] = ["Cargo nao encontrado."]
            });
        }

        var estabelecimentoValido = await db.Estabelecimentos
            .AnyAsync(e => e.Id == requisicao.IdEstabelecimento && e.IdEmpresa == requisicao.IdEmpresa, ct);

        if (!estabelecimentoValido)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["idEstabelecimento"] = ["Estabelecimento nao encontrado nesta empresa."]
            });
        }

        if (await db.ContratosTrabalho.AnyAsync(
                c => c.IdEmpresa == requisicao.IdEmpresa && c.Matricula == requisicao.Matricula, ct))
        {
            return Results.Conflict(new { detalhe = "Ja existe um contrato com esta matricula nesta empresa." });
        }

        ContratoTrabalho contrato;

        try
        {
            contrato = new ContratoTrabalho(
                usuario.IdOrganizacao,
                idFuncionario,
                requisicao.IdEmpresa,
                requisicao.Matricula,
                requisicao.DataAdmissao,
                requisicao.SalarioInicial,
                requisicao.IdCargo,
                requisicao.IdEstabelecimento,
                requisicao.JornadaMensalHoras,
                relogio.Agora);
        }
        catch (ArgumentException erro)
        {
            return RespostasValidacao.De(erro);
        }

        db.ContratosTrabalho.Add(contrato);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/contratos/{contrato.Id}", ContratoResposta.De(contrato));
    }

    private static async Task<IResult> ListarVigenciasAsync(Guid id, PrismaRhDbContext db, CancellationToken ct)
    {
        var contrato = await CarregarAsync(id, db, ct);

        if (contrato is null)
        {
            return Results.NotFound();
        }

        // Mais recente primeiro: a linha do tempo na tela le de cima para baixo.
        return Results.Ok(contrato.Vigencias
            .OrderByDescending(v => v.ValidoDe)
            .Select(VigenciaResposta.De)
            .ToList());
    }

    private static async Task<IResult> ObterVigenciaEmAsync(
        Guid id,
        [FromQuery] DateOnly data,
        PrismaRhDbContext db,
        CancellationToken ct)
    {
        var contrato = await CarregarAsync(id, db, ct);

        if (contrato is null)
        {
            return Results.NotFound();
        }

        var vigencia = contrato.VigenciaEm(data);

        // Data anterior a admissao ou posterior ao desligamento nao e erro:
        // e a resposta correta de que nao havia contrato valendo naquele dia.
        return vigencia is null
            ? Results.NoContent()
            : Results.Ok(VigenciaResposta.De(vigencia));
    }

    private static async Task<IResult> RegistrarAlteracaoAsync(
        Guid id,
        [FromBody] RegistrarAlteracaoRequisicao requisicao,
        PrismaRhDbContext db,
        IRelogio relogio,
        CancellationToken ct)
    {
        var contrato = await CarregarAsync(id, db, ct);

        if (contrato is null)
        {
            return Results.NotFound();
        }

        if (!await db.Cargos.AnyAsync(c => c.Id == requisicao.IdCargo, ct))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["idCargo"] = ["Cargo nao encontrado."]
            });
        }

        var estabelecimentoValido = await db.Estabelecimentos
            .AnyAsync(e => e.Id == requisicao.IdEstabelecimento && e.IdEmpresa == contrato.IdEmpresa, ct);

        if (!estabelecimentoValido)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["idEstabelecimento"] = ["Estabelecimento nao encontrado nesta empresa."]
            });
        }

        VigenciaContrato nova;

        try
        {
            nova = contrato.RegistrarAlteracao(
                requisicao.ValidoDe,
                requisicao.Salario,
                requisicao.IdCargo,
                requisicao.IdEstabelecimento,
                requisicao.JornadaMensalHoras,
                requisicao.Motivo,
                relogio.Agora);
        }
        catch (ArgumentException erro)
        {
            return RespostasValidacao.De(erro);
        }
        catch (InvalidOperationException erro)
        {
            return RespostasValidacao.De(erro);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException erro) when (RespostasValidacao.DeSobreposicao(erro) is { } conflito)
        {
            return conflito;
        }

        return Results.Created($"/api/contratos/{id}/vigencias", VigenciaResposta.De(nova));
    }

    private static async Task<IResult> DesligarAsync(
        Guid id,
        [FromBody] DesligarRequisicao requisicao,
        PrismaRhDbContext db,
        CancellationToken ct)
    {
        var contrato = await CarregarAsync(id, db, ct);

        if (contrato is null)
        {
            return Results.NotFound();
        }

        try
        {
            contrato.Desligar(requisicao.DataDesligamento, requisicao.Motivo);
        }
        catch (ArgumentException erro)
        {
            return RespostasValidacao.De(erro);
        }
        catch (InvalidOperationException erro)
        {
            return RespostasValidacao.De(erro);
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(ContratoResposta.De(contrato));
    }
}
