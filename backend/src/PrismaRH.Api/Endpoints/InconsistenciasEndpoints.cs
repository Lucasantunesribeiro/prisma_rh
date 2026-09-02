using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrismaRH.Api.Identidade;
using PrismaRH.Aplicacao.Comum;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Dominio.Analises;
using PrismaRH.Dominio.Auditoria;
using PrismaRH.Dominio.Workflow;
using PrismaRH.Infraestrutura.Auditoria;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Api.Endpoints;

public sealed record AndamentoResposta(
    Guid Id,
    string Tipo,
    string? Autor,
    DateTimeOffset OcorridoEm,
    string? Texto,
    string? StatusAnterior,
    string? StatusNovo,
    string? ResponsavelAnterior,
    string? ResponsavelNovo);

public sealed record InconsistenciaResposta(
    Guid Id,
    Guid IdFolha,
    string Competencia,
    string Codigo,
    string Regra,
    int VersaoRegra,
    string Categoria,
    string Severidade,
    string Status,
    bool Pendente,
    IReadOnlyList<string> ProximosStatus,
    Guid? IdResponsavel,
    string? Responsavel,
    string? Justificativa,
    DateTimeOffset? ConcluidaEm,
    Guid? IdFolhaFuncionario,
    string? Matricula,
    string? NomeFuncionario,
    string Descricao,
    decimal? ValorEsperado,
    decimal? ValorEncontrado,
    decimal? Diferenca,
    IReadOnlyList<AndamentoResposta>? Andamentos);

/// <summary>
/// O que muda o status.
///
/// ⚠️ Não contém `Id`, `IdOrganizacao` nem `Severidade` — o id vem da rota, a
/// organização vem do usuário autenticado, e a severidade foi congelada na
/// execução (`CLAUDE.md §24.7`, mass assignment).
/// </summary>
public sealed record TransitarRequisicao(StatusInconsistencia Status, string? Texto);

public sealed record AtribuirRequisicao(Guid? IdResponsavel);

public sealed record TextoRequisicao(string Texto);

/// <summary>
/// Workflow de tratamento das inconsistências (Fase 7).
///
/// ## O que esta fase acrescenta ao motor de análises
///
/// A Fase 6 **lê**: as regras encontram e o resultado fica lá. Aqui o achado
/// vira **trabalho** — alguém assume, escreve o que descobriu, conclui, e tudo
/// isso fica registrado.
///
/// ## Duas conclusões diferentes, de propósito
///
/// `Justificada` = o número estava certo, e o motivo está escrito.
/// `Corrigida` = o número estava errado e alguém arrumou.
///
/// Um único status "tratada" faria as duas virarem a mesma coisa, e "quantas
/// divergências eram erro de verdade?" deixaria de ter resposta.
///
/// ## A máquina de estados vive no domínio
///
/// O Security Gate nomeia a ameaça: "transição de status pulando etapas para
/// esconder pendência". Um `PUT status=Resolvida` direto fecharia qualquer
/// inconsistência sem análise nem justificativa — e o relatório de conformidade
/// viraria ficção. A validação mora em <see cref="TransicoesInconsistencia"/>,
/// e não aqui: quem chamar o domínio direto esbarra nela do mesmo jeito.
/// </summary>
public static class InconsistenciasEndpoints
{
    public static IEndpointRouteBuilder MapearInconsistencias(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/api/inconsistencias").WithTags("Inconsistencias");

        grupo.MapGet("/", ListarAsync)
            .WithSummary("Inconsistencias da organizacao, com filtros e paginacao")
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        grupo.MapGet("/{id:guid}", ObterAsync)
            .WithSummary("Uma inconsistencia, com a linha do tempo")
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        grupo.MapPost("/{id:guid}/status", TransitarAsync)
            .WithSummary("Muda o status, respeitando a maquina de estados")
            .RequireAuthorization(PoliticasAutorizacao.ProcessarFolha);

        grupo.MapPost("/{id:guid}/responsavel", AtribuirAsync)
            .WithSummary("Define quem cuida")
            .RequireAuthorization(PoliticasAutorizacao.ProcessarFolha);

        grupo.MapPost("/{id:guid}/comentarios", ComentarAsync)
            .WithSummary("Acrescenta um comentario a linha do tempo")
            .RequireAuthorization(PoliticasAutorizacao.ProcessarFolha);

        grupo.MapPost("/{id:guid}/evidencias", RegistrarEvidenciaAsync)
            .WithSummary("Registra o que foi conferido")
            .RequireAuthorization(PoliticasAutorizacao.ProcessarFolha);

        return rotas;
    }

    // ---------------------------------------------------------------- consulta

    /// <summary>
    /// A caixa de trabalho.
    ///
    /// Filtros por status, severidade, responsavel e folha - o `ROADMAP.md`
    /// pede "filtros", e sem eles a lista de uma organizacao com dez folhas
    /// deixa de ser utilizavel no segundo mes.
    /// </summary>
    private static async Task<IResult> ListarAsync(
        PrismaRhDbContext db,
        CancellationToken ct,
        [FromQuery] string? status = null,
        [FromQuery] string? severidade = null,
        [FromQuery] Guid? idResponsavel = null,
        [FromQuery] Guid? idFolha = null,
        [FromQuery] bool? pendentes = null,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanho = 25)
    {
        var consulta = db.ResultadosAnalise.AsNoTracking();

        // Vocabulario fechado: texto que nao casa com o enum e recusado, e nao
        // ignorado. Ignorar devolveria a lista inteira para quem pediu um
        // filtro - e a pessoa acharia que aquilo era o resultado do filtro.
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<StatusInconsistencia>(status, true, out var valor))
            {
                return Results.BadRequest(new { detalhe = "Status desconhecido." });
            }

            consulta = consulta.Where(r => r.Status == valor);
        }

        if (!string.IsNullOrWhiteSpace(severidade))
        {
            if (!Enum.TryParse<Severidade>(severidade, true, out var valor))
            {
                return Results.BadRequest(new { detalhe = "Severidade desconhecida." });
            }

            consulta = consulta.Where(r => r.Severidade == valor);
        }

        if (idResponsavel is { } responsavel)
        {
            consulta = consulta.Where(r => r.IdResponsavel == responsavel);
        }

        if (idFolha is { } folha)
        {
            consulta = consulta.Where(r => r.IdFolha == folha);
        }

        if (pendentes == true)
        {
            consulta = consulta.Where(r => r.Status != StatusInconsistencia.Resolvida);
        }

        var porPagina = Math.Clamp(tamanho, 1, 100);
        var salto = (Math.Max(pagina, 1) - 1) * porPagina;

        var total = await consulta.CountAsync(ct);

        var itens = await consulta
            .OrderByDescending(r => r.Severidade)
            .ThenBy(r => r.Status)
            .ThenBy(r => r.Id)
            .Skip(salto)
            .Take(porPagina)
            .ToListAsync(ct);

        var nomes = await NomesAsync(db, itens.Select(r => r.IdResponsavel), ct);
        var competencias = await CompetenciasAsync(db, itens.Select(r => r.IdFolha), ct);

        return Results.Ok(new
        {
            Total = total,
            Pagina = Math.Max(pagina, 1),
            Itens = itens
                .Select(r => Descrever(r, nomes, competencias, comAndamentos: false))
                .ToList(),
        });
    }

    private static async Task<IResult> ObterAsync(
        Guid id, PrismaRhDbContext db, CancellationToken ct)
    {
        // Pelo filtro global: inconsistencia de outra organizacao nao existe
        // daqui, e a resposta e 404 - nunca 403.
        var resultado = await db.ResultadosAnalise
            .AsNoTracking()
            .Include(r => r.Andamentos)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (resultado is null)
        {
            return Results.NotFound();
        }

        var autores = resultado.Andamentos
            .SelectMany(a => new[] { a.IdAutor, a.ResponsavelAnterior, a.ResponsavelNovo })
            .Append(resultado.IdResponsavel);

        var nomes = await NomesAsync(db, autores, ct);
        var competencias = await CompetenciasAsync(db, [resultado.IdFolha], ct);

        return Results.Ok(Descrever(resultado, nomes, competencias, comAndamentos: true));
    }

    // ---------------------------------------------------------------- escrita

    private static async Task<IResult> TransitarAsync(
        Guid id,
        [FromBody] TransitarRequisicao requisicao,
        PrismaRhDbContext db,
        IContextoUsuario usuario,
        IRelogio relogio,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(requisicao);

        var resultado = await CarregarAsync(db, id, ct);

        if (resultado is null)
        {
            return Results.NotFound();
        }

        var anterior = resultado.Status;
        var recusa = resultado.Transitar(
            requisicao.Status, usuario.IdUsuario, requisicao.Texto, relogio.Agora);

        if (recusa is not null)
        {
            return Results.BadRequest(new { detalhe = recusa });
        }

        db.Registrar(
            usuario, relogio,
            AcaoAuditada.InconsistenciaTransitada,
            EntidadeAuditada.ResultadoAnalise,
            resultado.Id,
            $"Inconsistência '{resultado.Codigo}' de {anterior} para {resultado.Status}.",
            $"de={anterior};para={resultado.Status};matricula={resultado.Matricula}");

        await db.SaveChangesAsync(ct);

        return await ObterAsync(id, db, ct);
    }

    private static async Task<IResult> AtribuirAsync(
        Guid id,
        [FromBody] AtribuirRequisicao requisicao,
        PrismaRhDbContext db,
        IContextoUsuario usuario,
        IRelogio relogio,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(requisicao);

        var resultado = await CarregarAsync(db, id, ct);

        if (resultado is null)
        {
            return Results.NotFound();
        }

        // ⚠️ O responsavel e conferido PELO FILTRO GLOBAL, e nao por uma
        // comparacao de IdOrganizacao escrita a mao. O Security Gate nomeia a
        // ameaca: "atribuicao de responsavel a usuario de outra organizacao".
        //
        // Um id de fora simplesmente nao e encontrado - a defesa e a mesma que
        // protege todo o resto, e nao um `if` que alguem pode esquecer.
        if (requisicao.IdResponsavel is { } candidato && candidato != Guid.Empty)
        {
            var existe = await db.Usuarios.AsNoTracking().AnyAsync(u => u.Id == candidato, ct);

            if (!existe)
            {
                return Results.BadRequest(new
                {
                    detalhe = "Responsável inválido: escolha um usuário desta organização.",
                });
            }
        }

        resultado.Atribuir(requisicao.IdResponsavel, usuario.IdUsuario, relogio.Agora);

        db.Registrar(
            usuario, relogio,
            AcaoAuditada.InconsistenciaAtribuida,
            EntidadeAuditada.ResultadoAnalise,
            resultado.Id,
            resultado.IdResponsavel is null
                ? $"Inconsistência '{resultado.Codigo}' ficou sem responsável."
                : $"Inconsistência '{resultado.Codigo}' atribuída.",
            $"responsavel={resultado.IdResponsavel}");

        await db.SaveChangesAsync(ct);

        return await ObterAsync(id, db, ct);
    }

    private static Task<IResult> ComentarAsync(
        Guid id,
        [FromBody] TextoRequisicao requisicao,
        PrismaRhDbContext db,
        IContextoUsuario usuario,
        IRelogio relogio,
        CancellationToken ct) =>
        EscreverAsync(
            id, requisicao, db, usuario, relogio, ct,
            (r, autor, texto, agora) => r.Comentar(autor, texto, agora),
            AcaoAuditada.InconsistenciaComentada,
            "comentada");

    private static Task<IResult> RegistrarEvidenciaAsync(
        Guid id,
        [FromBody] TextoRequisicao requisicao,
        PrismaRhDbContext db,
        IContextoUsuario usuario,
        IRelogio relogio,
        CancellationToken ct) =>
        EscreverAsync(
            id, requisicao, db, usuario, relogio, ct,
            (r, autor, texto, agora) => r.RegistrarEvidencia(autor, texto, agora),
            AcaoAuditada.EvidenciaRegistrada,
            "recebeu uma evidência");

    /// <summary>
    /// Comentario e evidencia sao a mesma operacao com um rotulo diferente.
    ///
    /// Duas copias divergiriam num teto de tamanho ou numa auditoria esquecida.
    /// </summary>
    private static async Task<IResult> EscreverAsync(
        Guid id,
        TextoRequisicao requisicao,
        PrismaRhDbContext db,
        IContextoUsuario usuario,
        IRelogio relogio,
        CancellationToken ct,
        Func<ResultadoAnalise, Guid, string, DateTimeOffset, string?> escrever,
        AcaoAuditada acao,
        string rotulo)
    {
        ArgumentNullException.ThrowIfNull(requisicao);

        var resultado = await CarregarAsync(db, id, ct);

        if (resultado is null)
        {
            return Results.NotFound();
        }

        var recusa = escrever(resultado, usuario.IdUsuario, requisicao.Texto ?? string.Empty, relogio.Agora);

        if (recusa is not null)
        {
            return Results.BadRequest(new { detalhe = recusa });
        }

        // A auditoria registra QUE houve comentario, e nao o texto dele. O
        // texto e o dado mais delicado do produto - justificativa de divergencia
        // salarial costuma explicar situacao pessoal (`CLAUDE.md secao 24.13`).
        // Ele vive na linha do tempo, com o controle de acesso dela.
        db.Registrar(
            usuario, relogio, acao, EntidadeAuditada.ResultadoAnalise, resultado.Id,
            $"Inconsistência '{resultado.Codigo}' {rotulo}.",
            $"matricula={resultado.Matricula}");

        await db.SaveChangesAsync(ct);

        return await ObterAsync(id, db, ct);
    }

    // ------------------------------------------------------------------ apoio

    private static Task<ResultadoAnalise?> CarregarAsync(
        PrismaRhDbContext db, Guid id, CancellationToken ct) =>
        db.ResultadosAnalise
            .Include(r => r.Andamentos)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    /// <summary>
    /// Nome dos usuarios citados, **sob o filtro global**.
    ///
    /// Um id de outra organizacao simplesmente nao volta, e a tela mostra o
    /// identificador cru em vez do nome. E o comportamento certo: inventar um
    /// nome seria pior, e vazar o de fora seria falha.
    /// </summary>
    private static async Task<Dictionary<Guid, string>> NomesAsync(
        PrismaRhDbContext db, IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var procurados = ids.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();

        if (procurados.Count == 0)
        {
            return [];
        }

        return await db.Usuarios
            .AsNoTracking()
            .Where(u => procurados.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Nome, ct);
    }

    private static async Task<Dictionary<Guid, string>> CompetenciasAsync(
        PrismaRhDbContext db, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var procurados = ids.Distinct().ToList();

        if (procurados.Count == 0)
        {
            return [];
        }

        var folhas = await db.Folhas
            .AsNoTracking()
            .Where(f => procurados.Contains(f.Id))
            .Select(f => new { f.Id, f.Competencia })
            .ToListAsync(ct);

        return folhas.ToDictionary(f => f.Id, f => f.Competencia.ToString());
    }

    private static InconsistenciaResposta Descrever(
        ResultadoAnalise r,
        IReadOnlyDictionary<Guid, string> nomes,
        IReadOnlyDictionary<Guid, string> competencias,
        bool comAndamentos)
    {
        string? Nome(Guid? id) =>
            id is { } valor && nomes.TryGetValue(valor, out var nome) ? nome : null;

        return new InconsistenciaResposta(
            r.Id,
            r.IdFolha,
            competencias.GetValueOrDefault(r.IdFolha, string.Empty),
            r.Codigo.ToString(),
            CatalogoRegras.De(r.Codigo)?.Nome ?? r.Codigo.ToString(),
            r.VersaoRegra,
            r.Categoria.ToString(),
            r.Severidade.ToString(),
            r.Status.ToString(),
            r.Pendente,
            [.. TransicoesInconsistencia.A_partir_de(r.Status).Select(s => s.ToString())],
            r.IdResponsavel,
            Nome(r.IdResponsavel),
            r.Justificativa,
            r.ConcluidaEm,
            r.IdFolhaFuncionario,
            r.Matricula,
            r.NomeFuncionario,
            r.Descricao,
            r.ValorEsperado,
            r.ValorEncontrado,
            r.Diferenca,
            comAndamentos
                ? [.. r.Andamentos.Select(a => new AndamentoResposta(
                    a.Id,
                    a.Tipo.ToString(),
                    Nome(a.IdAutor),
                    a.OcorridoEm,
                    a.Texto,
                    a.StatusAnterior?.ToString(),
                    a.StatusNovo?.ToString(),
                    Nome(a.ResponsavelAnterior),
                    Nome(a.ResponsavelNovo)))]
                : null);
    }
}
