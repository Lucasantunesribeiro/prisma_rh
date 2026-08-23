using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrismaRH.Api.Identidade;
using PrismaRH.Aplicacao.Comum;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Dominio.Contratos;
using PrismaRH.Dominio.Pessoas;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Api.Endpoints;

public sealed record CriarFuncionarioRequisicao(string Nome, string Cpf, DateOnly DataNascimento);

public sealed record AtualizarFuncionarioRequisicao(string Nome, DateOnly DataNascimento);

public sealed record FuncionarioResposta(
    Guid Id,
    string Nome,
    string Cpf,
    string CpfFormatado,
    DateOnly DataNascimento,
    bool Ativo)
{
    /// <summary>
    /// Na LISTAGEM o CPF vai mascarado. CPF e dado pessoal (LGPD): a tela de
    /// lista precisa identificar a pessoa, nao expor o documento inteiro de
    /// centenas de funcionarios de uma vez.
    /// </summary>
    public static FuncionarioResposta Resumida(Funcionario f) =>
        new(f.Id, f.Nome, f.Cpf.Mascarado, f.Cpf.Mascarado, f.DataNascimento, f.Ativo);

    public static FuncionarioResposta Completa(Funcionario f) =>
        new(f.Id, f.Nome, f.Cpf.Valor, f.Cpf.Formatado, f.DataNascimento, f.Ativo);
}

public static class FuncionariosEndpoints
{
    public static IEndpointRouteBuilder MapearFuncionarios(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/api/funcionarios").WithTags("Funcionarios");

        grupo.MapGet("/", ListarAsync)
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        grupo.MapGet("/{id:guid}", ObterAsync)
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        grupo.MapPost("/", CriarAsync)
            .RequireAuthorization(PoliticasAutorizacao.AdministrarPessoas);

        grupo.MapPut("/{id:guid}", AtualizarAsync)
            .RequireAuthorization(PoliticasAutorizacao.AdministrarPessoas);

        return rotas;
    }

    private static async Task<IResult> ListarAsync(
        PrismaRhDbContext db,
        CancellationToken ct,
        [FromQuery] string? nome = null,
        [FromQuery] string? cpf = null,
        [FromQuery] bool? ativo = null,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanho = 25)
    {
        pagina = Math.Max(1, pagina);
        tamanho = Math.Clamp(tamanho, 1, 100);

        // Sem WHERE por organizacao: o filtro global do DbContext ja aplicou.
        var consulta = db.Funcionarios.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(nome))
        {
            consulta = consulta.Where(f => EF.Functions.ILike(f.Nome, $"%{nome.Trim()}%"));
        }

        if (Cpf.TentarCriar(cpf, out var cpfBuscado))
        {
            // Busca por CPF exige o documento completo e valido. Busca parcial
            // por CPF viraria uma forma de descobrir documentos por tentativa.
            consulta = consulta.Where(f => f.Cpf == cpfBuscado);
        }

        if (ativo is not null)
        {
            consulta = consulta.Where(f => f.Ativo == ativo.Value);
        }

        consulta = consulta.OrderBy(f => f.Nome);

        var total = await consulta.CountAsync(ct);
        var itens = await consulta
            .Skip((pagina - 1) * tamanho)
            .Take(tamanho)
            .Select(f => FuncionarioResposta.Resumida(f))
            .ToListAsync(ct);

        return Results.Ok(new { total, pagina, tamanho, itens });
    }

    private static async Task<IResult> ObterAsync(Guid id, PrismaRhDbContext db, CancellationToken ct)
    {
        var funcionario = await db.Funcionarios.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);

        // 404, nunca 403: 403 confirmaria que este id existe em outra organizacao.
        return funcionario is null
            ? Results.NotFound()
            : Results.Ok(FuncionarioResposta.Completa(funcionario));
    }

    private static async Task<IResult> CriarAsync(
        [FromBody] CriarFuncionarioRequisicao requisicao,
        PrismaRhDbContext db,
        IContextoUsuario usuario,
        IRelogio relogio,
        CancellationToken ct)
    {
        if (!Cpf.TentarCriar(requisicao.Cpf, out var cpf))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["cpf"] = ["CPF invalido."]
            });
        }

        if (await db.Funcionarios.AnyAsync(f => f.Cpf == cpf, ct))
        {
            return Results.Conflict(new { detalhe = "Ja existe um funcionario com este CPF nesta organizacao." });
        }

        Funcionario funcionario;

        try
        {
            // A organizacao vem do TOKEN, nunca do corpo.
            funcionario = new Funcionario(
                usuario.IdOrganizacao, requisicao.Nome, cpf, requisicao.DataNascimento, relogio.Agora);
        }
        catch (ArgumentException erro)
        {
            return RespostasValidacao.De(erro);
        }

        db.Funcionarios.Add(funcionario);
        await db.SaveChangesAsync(ct);

        return Results.Created(
            $"/api/funcionarios/{funcionario.Id}",
            FuncionarioResposta.Completa(funcionario));
    }

    private static async Task<IResult> AtualizarAsync(
        Guid id,
        [FromBody] AtualizarFuncionarioRequisicao requisicao,
        PrismaRhDbContext db,
        CancellationToken ct)
    {
        var funcionario = await db.Funcionarios.FirstOrDefaultAsync(f => f.Id == id, ct);

        if (funcionario is null)
        {
            return Results.NotFound();
        }

        try
        {
            // O CPF nao entra: corrigi-lo em silencio quebraria a ligacao com
            // tudo que ja foi calculado para esta pessoa.
            funcionario.Atualizar(requisicao.Nome, requisicao.DataNascimento);
        }
        catch (ArgumentException erro)
        {
            return RespostasValidacao.De(erro);
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(FuncionarioResposta.Completa(funcionario));
    }
}
