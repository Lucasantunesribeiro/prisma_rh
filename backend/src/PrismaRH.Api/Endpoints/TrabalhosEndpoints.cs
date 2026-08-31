using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrismaRH.Api.Identidade;
using PrismaRH.Aplicacao.Comum;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Dominio.Assincrono;
using PrismaRH.Dominio.Importacao;
using PrismaRH.Infraestrutura.Fila;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Api.Endpoints;

public sealed record TrabalhoResposta(
    Guid Id,
    string Tipo,
    string Status,
    bool Pendente,
    int Tentativas,
    Guid? IdRecurso,
    string? Erro,
    DateTimeOffset CriadoEm,
    DateTimeOffset? ConcluidoEm);

public sealed record PaginaTrabalhos(int Total, int Pagina, List<TrabalhoResposta> Itens);

/// <summary>
/// Importacao assincrona e consulta de status (Fase 9).
///
/// ## O que muda em relacao ao caminho sincrono
///
/// O `POST /api/importacoes/funcionarios/confirmar` continua existindo e
/// continua sendo o caminho normal: para uma planilha de RH ele responde em
/// segundos, e resposta imediata e melhor que resposta correta daqui a pouco.
///
/// O caminho assincrono existe para quando o arquivo e grande o bastante para a
/// requisicao nao ser o lugar certo. Ele **nao substitui** o outro - e por isso
/// que a regra de negocio mora em `ProcessadorImportacao`, chamada pelos dois.
///
/// ## O upload assincrono e uma transacao so
///
/// Reserva de espaco, gravacao dos bytes e criacao do trabalho acontecem juntos.
/// Se qualquer parte falhar, nada fica: nem blob orfao ocupando o orcamento
/// global, nem trabalho apontando para arquivo que nao existe.
///
/// A publicacao na fila e o **unico passo fora da transacao**, e de proposito:
/// nao da para desfazer uma mensagem ja enviada. A ordem e commit primeiro,
/// mensagem depois - assim a mensagem so existe se o trabalho existir. O
/// contrario produziria mensagem apontando para nada.
/// </summary>
public static class TrabalhosEndpoints
{
    public static IEndpointRouteBuilder MapearTrabalhos(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/api/trabalhos").WithTags("Trabalhos");

        grupo.MapGet("/", ListarAsync)
            .WithSummary("Trabalhos assincronos da organizacao")
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        grupo.MapGet("/{id:guid}", ObterAsync)
            .WithSummary("Status de um trabalho")
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        rotas.MapPost("/api/importacoes/funcionarios/assincrona", EnfileirarAsync)
            .WithSummary("Envia a planilha para processamento em segundo plano")
            .WithTags("Importacoes")
            .DisableAntiforgery()
            .RequireAuthorization(PoliticasAutorizacao.AdministrarPessoas);

        return rotas;
    }

    // ------------------------------------------------------------------ upload

    private static async Task<IResult> EnfileirarAsync(
        IFormFile? arquivo,
        PrismaRhDbContext db,
        IContextoUsuario usuario,
        IRelogio relogio,
        PublicadorFila fila,
        CancellationToken ct)
    {
        if (arquivo is null || arquivo.Length == 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["arquivo"] = ["Envie um arquivo."],
            });
        }

        // Teto por arquivo conferido ANTES de ler o corpo. Um limite verificado
        // depois de carregar 200 MB na memoria nao protege de nada.
        if (arquivo.Length > OrcamentoSemCusto.TamanhoMaximoArquivoBytes)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["arquivo"] = [$"Arquivo acima de {OrcamentoSemCusto.TamanhoMaximoArquivoBytes / (1024 * 1024)} MB."],
            });
        }

        byte[] bytes;

        await using (var fluxo = arquivo.OpenReadStream())
        {
            if (!FluxoComTeto.Ler(fluxo, OrcamentoSemCusto.TamanhoMaximoArquivoBytes, out bytes))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["arquivo"] = ["Arquivo maior do que o declarado."],
                });
            }
        }

        var hash = Importacao.CalcularHash(bytes);
        var chave = TrabalhoAssincrono.ChaveDeImportacao(usuario.IdOrganizacao, hash);

        // Idempotencia na entrada: o mesmo arquivo, da mesma organizacao, ja
        // enfileirado, devolve o trabalho existente em vez de criar outro.
        // O indice unico no banco e a rede final; isto evita a corrida comum.
        var existente = await db.TrabalhosAssincronos
            .FirstOrDefaultAsync(t => t.ChaveIdempotencia == chave, ct);

        if (existente is not null)
        {
            return Results.Ok(Converter(existente));
        }

        await using var transacao = await db.Database.BeginTransactionAsync(ct);

        // ⚠️ Orcamento GLOBAL, com lock consultivo. Ver `OrcamentoBlobs`.
        if (!await OrcamentoBlobs.TentarReservarAsync(db, bytes.Length, ct))
        {
            await transacao.RollbackAsync(ct);

            // 507 e o codigo certo: nao e culpa do pedido, e falta de espaco.
            // A mensagem diz que o limite e do sistema, e nao da organizacao -
            // senao alguem procuraria uma quota propria que nao existe.
            return Results.Problem(
                statusCode: StatusCodes.Status507InsufficientStorage,
                title: "Sem espaco temporario",
                detail: "O armazenamento temporario compartilhado esta cheio. "
                    + "Aguarde as importacoes em andamento terminarem e tente de novo.");
        }

        var trabalho = new TrabalhoAssincrono(
            usuario.IdOrganizacao, usuario.IdUsuario,
            TipoTrabalho.ImportacaoFuncionarios, chave, relogio.Agora);

        db.TrabalhosAssincronos.Add(trabalho);
        db.BlobsTemporarios.Add(new BlobTemporario(
            usuario.IdOrganizacao, trabalho.Id, bytes, relogio.Agora, OrcamentoSemCusto.RetencaoBlob));

        try
        {
            await db.SaveChangesAsync(ct);
            await transacao.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            await transacao.RollbackAsync(ct);

            // O indice unico da chave de idempotencia: alguem enfileirou o
            // mesmo arquivo entre a consulta e o commit. Nada foi gravado.
            var concorrente = await db.TrabalhosAssincronos
                .FirstOrDefaultAsync(t => t.ChaveIdempotencia == chave, ct);

            return concorrente is not null
                ? Results.Ok(Converter(concorrente))
                : Results.Conflict(new { detalhe = "Conflito ao enfileirar. Tente de novo." });
        }

        // Fora da transacao, DEPOIS do commit: mensagem nao se desfaz.
        await fila.PublicarAsync(MensagemTrabalho.De(trabalho), ct);

        return Results.Accepted($"/api/trabalhos/{trabalho.Id}", Converter(trabalho));
    }

    // ----------------------------------------------------------------- consulta

    private static async Task<IResult> ObterAsync(Guid id, PrismaRhDbContext db, CancellationToken ct)
    {
        // Sob o filtro global: trabalho de outra organizacao devolve 404, e nao
        // 403. Um 403 confirmaria que aquele id existe.
        var trabalho = await db.TrabalhosAssincronos
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        return trabalho is null ? Results.NotFound() : Results.Ok(Converter(trabalho));
    }

    private static async Task<IResult> ListarAsync(
        PrismaRhDbContext db,
        CancellationToken ct,
        [FromQuery] bool? pendentes = null,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanho = 20)
    {
        pagina = Math.Max(1, pagina);
        tamanho = Math.Clamp(tamanho, 1, 100); // teto obrigatorio (`CLAUDE.md 24.18`)

        var consulta = db.TrabalhosAssincronos.AsNoTracking();

        if (pendentes == true)
        {
            consulta = consulta.Where(t =>
                t.Status == StatusTrabalho.Enfileirado || t.Status == StatusTrabalho.Processando);
        }

        var total = await consulta.CountAsync(ct);

        var itens = await consulta
            .OrderByDescending(t => t.CriadoEm)
            .Skip((pagina - 1) * tamanho)
            .Take(tamanho)
            .ToListAsync(ct);

        return Results.Ok(new PaginaTrabalhos(total, pagina, [.. itens.Select(Converter)]));
    }

    private static TrabalhoResposta Converter(TrabalhoAssincrono t) => new(
        t.Id, t.Tipo.ToString(), t.Status.ToString(), t.Pendente,
        t.Tentativas, t.IdRecurso, t.Erro, t.CriadoEm, t.ConcluidoEm);
}
