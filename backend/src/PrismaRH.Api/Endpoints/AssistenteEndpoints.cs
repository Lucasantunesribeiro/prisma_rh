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

        // ------------------------------------------------------ Fase 11B
        grupo.MapPost("/folhas/{id:guid}/resumo", ResumirAsync)
            .WithSummary("Resumo executivo da folha: numeros do C#, prosa da IA")
            // Resumo executivo e para QUEM LE a folha - Auditor incluso. O
            // controle de custo aqui e o limite por organizacao, e nao o perfil.
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais)
            .RequireRateLimiting(PoliticaLimite);

        // ------------------------------------------------------ Fase 11C
        grupo.MapGet("/consultas/vocabulario", Vocabulario)
            .WithSummary("Os campos e comparacoes que uma pergunta pode usar")
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        grupo.MapPost("/consultas", ConsultarAsync)
            .WithSummary("Converte uma pergunta em portugues num filtro controlado")
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais)
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

    // ------------------------------------------------------------ Fase 11B

    public sealed record ResumoResposta(
        string Situacao,
        RetratoDaFolha Retrato,
        string Texto,
        bool GeradoPorIa,
        bool DoCache,
        string Aviso);

    /// <summary>
    /// O resumo executivo.
    ///
    /// ⚠️ A resposta traz o <see cref="RetratoDaFolha"/> **sempre**, mesmo
    /// quando a IA falha. Os números são apurados por consulta determinística e
    /// não dependem do modelo — é o `ROADMAP.md` da 11B ao pé da letra: *"nunca
    /// é a fonte de um número"*. Com o provedor fora do ar, a tela perde o
    /// parágrafo e mantém o resumo numérico inteiro.
    /// </summary>
    private static async Task<IResult> ResumirAsync(
        Guid id,
        PrismaRhDbContext db,
        ResumoDaFolha resumo,
        IContextoUsuario usuario,
        IRelogio relogio,
        CancellationToken ct)
    {
        // Sob o filtro global: folha de outra organizacao nao existe daqui.
        var retrato = await ResumoDaFolha.ApurarAsync(db, id, ct);

        if (retrato is null)
        {
            return Results.NotFound();
        }

        var correlacao = Guid.CreateVersion7();

        var pronto = await resumo.ResumirAsync(retrato, id, usuario.IdOrganizacao, correlacao, ct);

        if (pronto.Situacao != SituacaoIa.Respondeu)
        {
            return Results.Ok(new ResumoResposta(
                pronto.Situacao.ToString(), retrato, string.Empty, false, false,
                Explicar(pronto.Situacao)));
        }

        if (!pronto.DoCache)
        {
            db.Registrar(
                usuario, relogio,
                AcaoAuditada.ResumoIaGerado,
                EntidadeAuditada.ExplicacaoIa,
                id,
                $"Resumo executivo gerado por IA para a folha {retrato.Competencia}.",
                $"modelo={OrcamentoIa.Modelo};tokens={pronto.TokensUsados};correlacao={correlacao:N}");

            await db.SaveChangesAsync(ct);
        }

        return Results.Ok(new ResumoResposta(
            "Respondeu", retrato, pronto.Texto, GeradoPorIa: true, pronto.DoCache,
            "Texto gerado por inteligencia artificial. Os numeros ao lado vem do "
            + "calculo do sistema, nao do modelo."));
    }

    // ------------------------------------------------------------ Fase 11C

    public sealed record PerguntaRequisicao(string? Pergunta);

    public sealed record AchadoResposta(
        Guid Id, string Codigo, string Regra, string Categoria, string Severidade,
        string Status, string Descricao, decimal? ValorEncontrado, decimal? Diferenca);

    public sealed record ConsultaResposta(
        string Situacao,
        IReadOnlyList<string> Entendido,
        IReadOnlyList<string> NaoEntendido,
        int Total,
        bool Truncado,
        IReadOnlyList<AchadoResposta> Itens,
        string Aviso);

    /// <summary>
    /// O catálogo, para a tela poder mostrar o que dá para perguntar.
    ///
    /// Sem isso a funcionalidade vira adivinhação: a pessoa pergunta sobre
    /// salário, recebe "não entendi" e conclui que o produto é ruim, quando na
    /// verdade aquele campo nunca esteve no vocabulário.
    /// </summary>
    private static IResult Vocabulario(ConsultaLinguagemNatural consulta) =>
        Results.Ok(new
        {
            disponivel = consulta.Disponivel,
            campos = VocabularioConsulta.Catalogo.Select(c => new
            {
                campo = c.Campo.ToString(),
                significado = c.Significado,
                comparacoes = c.Operadores.Select(o => o.ToString()).ToList(),
                valores = c.ValoresPossiveis,
            }).ToList(),
        });

    /// <summary>
    /// A consulta em linguagem natural.
    ///
    /// ## O que esta rota NÃO faz
    ///
    /// Não executa SQL vindo do modelo. O modelo devolve tuplas de texto; o
    /// `VocabularioConsulta` confere cada uma; o `ConsultaLinguagemNatural`
    /// monta `Where` tipado sobre `db.ResultadosAnalise`, que **já nasce sob o
    /// filtro global de organização**.
    ///
    /// ## Por que ela devolve o que entendeu
    ///
    /// Quem pergunta em português precisa ver em que a pergunta virou. Sem
    /// isso, uma interpretação errada devolve uma lista plausível que responde
    /// outra coisa — e ninguém percebe.
    /// </summary>
    private static async Task<IResult> ConsultarAsync(
        PerguntaRequisicao requisicao,
        PrismaRhDbContext db,
        ConsultaLinguagemNatural consulta,
        IContextoUsuario usuario,
        IRelogio relogio,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(requisicao);

        if (string.IsNullOrWhiteSpace(requisicao.Pergunta))
        {
            return Results.BadRequest(new { detalhe = "Escreva a pergunta." });
        }

        if (requisicao.Pergunta.Length > ConsultaLinguagemNatural.MaximoCaracteresPergunta)
        {
            return Results.BadRequest(new
            {
                detalhe = "Pergunta acima de "
                    + $"{ConsultaLinguagemNatural.MaximoCaracteresPergunta} caracteres.",
            });
        }

        var correlacao = Guid.CreateVersion7();

        var interpretada = await consulta.InterpretarAsync(requisicao.Pergunta, correlacao, ct);

        if (interpretada.Situacao != SituacaoIa.Respondeu)
        {
            return Results.Ok(new ConsultaResposta(
                interpretada.Situacao.ToString(), [], [], 0, false, [],
                Explicar(interpretada.Situacao)));
        }

        if (interpretada.Filtros.Count == 0)
        {
            // ⚠️ Zero filtro NAO vira "devolve tudo". Quem pediu um recorte e
            // recebe a tabela inteira acredita que aquilo e o recorte.
            return Results.Ok(new ConsultaResposta(
                "NaoEntendida", [], interpretada.Recusados, 0, false, [],
                "Nao consegui transformar esta pergunta nos campos disponiveis. "
                + "Veja a lista de campos e tente de outro jeito."));
        }

        // ⚠️ Parte de `db.ResultadosAnalise`, que ja esta sob o filtro global.
        // Nao ha `IgnoreQueryFilters` neste caminho, e a ausencia e o controle.
        var consultavel = ConsultaLinguagemNatural.Aplicar(
            db.ResultadosAnalise.AsNoTracking(),
            interpretada.Filtros,
            db.Folhas.AsNoTracking());

        var total = await consultavel.CountAsync(ct);

        var itens = await consultavel
            .OrderByDescending(r => r.Severidade)
            .ThenBy(r => r.Status)
            .ThenBy(r => r.Id)
            .Take(ConsultaLinguagemNatural.MaximoLinhas)
            .Select(r => new AchadoResposta(
                r.Id,
                r.Codigo.ToString(),
                string.Empty,
                r.Categoria.ToString(),
                r.Severidade.ToString(),
                r.Status.ToString(),
                r.Descricao,
                r.ValorEncontrado,
                r.Diferenca))
            .ToListAsync(ct);

        db.Registrar(
            usuario, relogio,
            AcaoAuditada.ConsultaIaExecutada,
            EntidadeAuditada.ExplicacaoIa,
            correlacao,
            "Consulta em linguagem natural convertida em filtro.",
            $"filtros={string.Join(" e ", interpretada.Filtros.Select(f => f.Descrever()))}"
            + $";linhas={total};tokens={interpretada.TokensUsados}");

        await db.SaveChangesAsync(ct);

        return Results.Ok(new ConsultaResposta(
            "Respondeu",
            [.. interpretada.Filtros.Select(f => f.Descrever())],
            interpretada.Recusados,
            total,
            total > ConsultaLinguagemNatural.MaximoLinhas,
            [.. itens.Select(i => i with
            {
                Regra = CatalogoRegras.De(Enum.Parse<CodigoRegra>(i.Codigo))?.Nome ?? i.Codigo,
            })],
            "O filtro foi proposto por inteligencia artificial e conferido pelo "
            + "sistema. Confira acima o que ele entendeu da sua pergunta."));
    }

    private static string Explicar(SituacaoIa situacao) => situacao switch
    {
        SituacaoIa.NaoConfigurada => "O assistente nao esta configurado neste ambiente.",
        SituacaoIa.LimiteAtingido => "Limite de explicacoes atingido. Tente de novo mais tarde.",
        SituacaoIa.Recusada => "O assistente nao conseguiu explicar esta inconsistencia.",
        _ => "O assistente esta indisponivel no momento.",
    };
}
