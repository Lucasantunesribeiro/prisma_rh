using PrismaRH.Api.Producao;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrismaRH.Api.Identidade;
using PrismaRH.Aplicacao.Comum;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Dominio.Pessoas;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Api.Endpoints;

/// <summary>
/// Entrada propria, sem Id nem IdOrganizacao: o cliente nao escolhe de quem e
/// o dependente (CLAUDE.md secao 24.7, protecao contra overposting).
/// </summary>
public sealed record CriarDependenteRequisicao(
    string Nome,
    DateOnly DataNascimento,
    RelacaoDependente Relacao,
    DateOnly? InicioDeducaoIrrf,
    DateOnly? FimDeducaoIrrf);

public sealed record AtualizarDependenteRequisicao(
    string Nome,
    DateOnly DataNascimento,
    RelacaoDependente Relacao,
    DateOnly? InicioDeducaoIrrf,
    DateOnly? FimDeducaoIrrf);

public sealed record DependenteResposta(
    Guid Id,
    Guid IdFuncionario,
    string Nome,
    DateOnly DataNascimento,
    RelacaoDependente Relacao,
    bool DedutivelIrrf,
    DateOnly? InicioDeducaoIrrf,
    DateOnly? FimDeducaoIrrf)
{
    public static DependenteResposta De(Dependente d) =>
        new(d.Id, d.IdFuncionario, d.Nome, d.DataNascimento, d.Relacao,
            d.DedutivelIrrf, d.InicioDeducaoIrrf, d.FimDeducaoIrrf);
}

/// <summary>
/// Os dependentes de um funcionario.
///
/// Rota ANINHADA no funcionario de proposito: o dependente e resolvido pelo
/// pai, que ja passa pelo filtro global. Assim um id de dependente de outra
/// organizacao nao encontra caminho, e a defesa contra IDOR nao depende de
/// alguem lembrar de conferir a organizacao a mao (CLAUDE.md secao 24.6).
///
/// Sao dados pessoais de TERCEIROS - pessoas que nao usam o sistema e nao
/// consentiram com nada. Por isso o cadastro guarda o minimo, e a leitura
/// exige o mesmo perfil que ve dado empresarial.
/// </summary>
public static class DependentesEndpoints
{
    /// <summary>
    /// Teto de dependentes por funcionario.
    ///
    /// Nao e regra legal: e limite de recurso (CLAUDE.md secao 24.18). Sem ele
    /// uma organizacao poderia inflar uma pessoa com milhares de linhas e
    /// tornar o calculo dela caro para todas as outras.
    /// </summary>
    public const int MaximoPorFuncionario = 30;

    public static IEndpointRouteBuilder MapearDependentes(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/api/funcionarios/{idFuncionario:guid}/dependentes")
            .WithTags("Dependentes");

        grupo.MapGet("/", ListarAsync)
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        grupo.MapPost("/", CriarAsync)
            .RequireAuthorization(PoliticasAutorizacao.AdministrarPessoas);

        grupo.MapPut("/{id:guid}", AtualizarAsync)
            .RequireAuthorization(PoliticasAutorizacao.AdministrarPessoas);

        grupo.MapDelete("/{id:guid}", RemoverAsync)
            .RequireAuthorization(PoliticasAutorizacao.AdministrarPessoas);

        return rotas;
    }

    private static async Task<IResult> ListarAsync(
        Guid idFuncionario,
        PrismaRhDbContext db,
        CancellationToken ct)
    {
        if (!await db.Funcionarios.AnyAsync(f => f.Id == idFuncionario, ct))
        {
            return Results.NotFound();
        }

        // Sem paginacao porque a lista e limitada por MaximoPorFuncionario: o
        // teto ja existe, e paginar 30 linhas seria cerimonia sem ganho.
        var dependentes = await db.Dependentes
            .AsNoTracking()
            .Where(d => d.IdFuncionario == idFuncionario)
            .OrderBy(d => d.DataNascimento)
            .ComTeto()
            .ToListAsync(ct);

        return Results.Ok(dependentes.Select(DependenteResposta.De).ToList());
    }

    private static async Task<IResult> CriarAsync(
        Guid idFuncionario,
        [FromBody] CriarDependenteRequisicao requisicao,
        PrismaRhDbContext db,
        IContextoUsuario usuario,
        IRelogio relogio,
        CancellationToken ct)
    {
        // Passa pelo filtro global: funcionario de outra organizacao nao
        // existe daqui, e a resposta e 404 - nunca 403, que confirmaria o id.
        if (!await db.Funcionarios.AnyAsync(f => f.Id == idFuncionario, ct))
        {
            return Results.NotFound();
        }

        var quantidade = await db.Dependentes.CountAsync(d => d.IdFuncionario == idFuncionario, ct);

        if (quantidade >= MaximoPorFuncionario)
        {
            return Results.Conflict(new
            {
                detalhe = $"Funcionario ja tem o maximo de {MaximoPorFuncionario} dependentes."
            });
        }

        Dependente dependente;

        try
        {
            dependente = new Dependente(
                usuario.IdOrganizacao,
                idFuncionario,
                requisicao.Nome,
                requisicao.DataNascimento,
                requisicao.Relacao,
                requisicao.InicioDeducaoIrrf,
                requisicao.FimDeducaoIrrf,
                relogio.Agora);
        }
        catch (ArgumentException erro)
        {
            return RespostasValidacao.De(erro);
        }

        db.Dependentes.Add(dependente);
        await db.SaveChangesAsync(ct);

        return Results.Created(
            $"/api/funcionarios/{idFuncionario}/dependentes/{dependente.Id}",
            DependenteResposta.De(dependente));
    }

    private static async Task<IResult> AtualizarAsync(
        Guid idFuncionario,
        Guid id,
        [FromBody] AtualizarDependenteRequisicao requisicao,
        PrismaRhDbContext db,
        CancellationToken ct)
    {
        // Resolvido pelo PAI e pelo proprio id: trocar o idFuncionario da URL
        // por outro nao alcanca o dependente de ninguem.
        var dependente = await db.Dependentes
            .FirstOrDefaultAsync(d => d.Id == id && d.IdFuncionario == idFuncionario, ct);

        if (dependente is null)
        {
            return Results.NotFound();
        }

        try
        {
            dependente.Atualizar(requisicao.Nome, requisicao.DataNascimento, requisicao.Relacao);
            dependente.DefinirDeducaoIrrf(requisicao.InicioDeducaoIrrf, requisicao.FimDeducaoIrrf);
        }
        catch (ArgumentException erro)
        {
            return RespostasValidacao.De(erro);
        }

        await db.SaveChangesAsync(ct);

        return Results.Ok(DependenteResposta.De(dependente));
    }

    private static async Task<IResult> RemoverAsync(
        Guid idFuncionario,
        Guid id,
        PrismaRhDbContext db,
        CancellationToken ct)
    {
        var dependente = await db.Dependentes
            .FirstOrDefaultAsync(d => d.Id == id && d.IdFuncionario == idFuncionario, ct);

        if (dependente is null)
        {
            return Results.NotFound();
        }

        // Remocao de verdade, sem soft delete: dado pessoal de terceiro sem
        // finalidade nao deve ser retido (CLAUDE.md secao 25). A folha ja
        // calculada nao depende desta linha - ela guarda a quantidade que
        // valeu no seu proprio calculo.
        db.Dependentes.Remove(dependente);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
