using Microsoft.EntityFrameworkCore;
using PrismaRH.Api.Identidade;
using PrismaRH.Aplicacao.Comum;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Dominio.Analises;
using PrismaRH.Dominio.Auditoria;
using PrismaRH.Infraestrutura.Auditoria;
using PrismaRH.Infraestrutura.Ia;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Api.Endpoints;

public sealed record ExplicacaoResposta(
    string Situacao,
    string Texto,
    /// <summary>
    /// **Sempre `true` quando há texto.** A interface é obrigada a rotular
    /// (`CLAUDE.md §37.3`): o leitor precisa saber que aquilo foi escrito por
    /// máquina e pode estar errado.
    /// </summary>
    bool GeradoPorIa,
    bool DoCache,
    string Aviso);

/// <summary>
/// O assistente de inconsistências (Fase 11).
///
/// ## Uma rota, e ela só lê
///
/// `CLAUDE.md §37.4`: **a camada de IA é de leitura. Nenhum caminho de código
/// iniciado por resposta de modelo pode terminar em escrita no banco.**
///
/// Esta rota devolve texto. O único `INSERT` que ela faz é o evento de
/// auditoria — registrando **que** houve explicação, nunca o conteúdo dela, e
/// isso acontece independentemente do que o modelo respondeu.
///
/// ## O isolamento não depende do modelo se comportar
///
/// O resultado é carregado **sob o filtro global**. Um id de outra organização
/// simplesmente não é encontrado, e a IA nunca chega a ser chamada — 404, e não
/// 403, pelo mesmo motivo de sempre: um 403 confirmaria que aquele id existe.
///
/// Isso é `§37.5`: *"o isolamento é arquitetural, não depende do modelo se
/// comportar"*.
/// </summary>
public static class AssistenteEndpoints
{
    /// <summary>
    /// Limite **por organização**, como na consulta de CNPJ da Fase 8 — aqui o
    /// usuário já está autenticado, e o recurso protegido é uma cota
    /// compartilhada que custa dinheiro por chamada.
    /// </summary>
    public const string PoliticaLimite = "assistente-ia";

    public static IEndpointRouteBuilder MapearAssistente(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/api/assistente").WithTags("Assistente");

        grupo.MapGet("/disponivel", Disponivel)
            .WithSummary("A camada de IA esta configurada neste ambiente?")
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        grupo.MapPost("/inconsistencias/{id:guid}/explicacao", ExplicarAsync)
            .WithSummary("Explica, em linguagem simples, uma inconsistencia ja detectada")
            // Quem trata inconsistencia. Nao e leitura geral: cada chamada
            // consome cota de um servico que cobra por token.
            .RequireAuthorization(PoliticasAutorizacao.ProcessarFolha)
            .RequireRateLimiting(PoliticaLimite);

        return rotas;
    }

    /// <summary>
    /// A tela pergunta antes de mostrar o botao. Sem IA configurada, o produto
    /// funciona igual — ela é acessório, e não requisito (`CLAUDE.md §1`).
    /// </summary>
    private static IResult Disponivel(AssistenteInconsistencias assistente) =>
        Results.Ok(new { disponivel = assistente.Disponivel });

    private static async Task<IResult> ExplicarAsync(
        Guid id,
        PrismaRhDbContext db,
        AssistenteInconsistencias assistente,
        IContextoUsuario usuario,
        IRelogio relogio,
        CancellationToken ct)
    {
        // Sob o filtro global. Id de outra organizacao nao e encontrado, e a IA
        // nem chega a ser chamada.
        var resultado = await db.ResultadosAnalise
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (resultado is null)
        {
            return Results.NotFound();
        }

        var correlacao = Guid.CreateVersion7();

        var explicacao = await assistente.ExplicarAsync(
            resultado,
            CatalogoRegras.De(resultado.Codigo)?.Nome ?? resultado.Codigo.ToString(),
            usuario.IdOrganizacao,
            correlacao,
            ct);

        if (explicacao.Situacao != SituacaoIa.Respondeu)
        {
            // Falha da IA **nao** e erro da tela: o analista continua com a
            // descricao que o motor determinístico gerou, que e a informacao
            // que importa. 200 com o motivo dentro, e nao 502.
            return Results.Ok(new ExplicacaoResposta(
                explicacao.Situacao.ToString(), string.Empty, false, false,
                Explicar(explicacao.Situacao)));
        }

        // ⚠️ Auditado porque `CLAUDE.md §37.5` manda registrar quando uma
        // sugestao de IA participa de uma decisao. Registra QUE houve, e o
        // custo em tokens - nunca o texto.
        if (!explicacao.DoCache)
        {
            db.Registrar(
                usuario, relogio,
                AcaoAuditada.ExplicacaoIaGerada,
                EntidadeAuditada.ExplicacaoIa,
                resultado.Id,
                $"Explicacao gerada por IA para a inconsistencia {resultado.Codigo}.",
                $"modelo={OrcamentoIa.Modelo};tokens={explicacao.TokensUsados};correlacao={correlacao:N}");

            await db.SaveChangesAsync(ct);
        }

        return Results.Ok(new ExplicacaoResposta(
            "Respondeu",
            explicacao.Texto,
            GeradoPorIa: true,
            explicacao.DoCache,
            "Texto gerado por inteligencia artificial. Pode conter erro - confira na memoria de calculo."));
    }

    private static string Explicar(SituacaoIa situacao) => situacao switch
    {
        SituacaoIa.NaoConfigurada => "O assistente nao esta configurado neste ambiente.",
        SituacaoIa.LimiteAtingido => "Limite de explicacoes atingido. Tente de novo mais tarde.",
        SituacaoIa.Recusada => "O assistente nao conseguiu explicar esta inconsistencia.",
        _ => "O assistente esta indisponivel no momento.",
    };
}
