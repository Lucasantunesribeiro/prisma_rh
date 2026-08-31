using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrismaRH.Api.Identidade;
using PrismaRH.Aplicacao.Comum;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Dominio.Analises;
using PrismaRH.Dominio.Auditoria;
using PrismaRH.Infraestrutura.Auditoria;
using PrismaRH.Infraestrutura.Analises;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Api.Endpoints;

public sealed record ParametroResposta(
    string Chave,
    string Rotulo,
    string Explicacao,
    string Tipo,
    string Padrao,
    string Minimo,
    string Maximo,
    string Valor);

public sealed record RegraResposta(
    string Codigo,
    string Nome,
    string Explicacao,
    string Categoria,
    int Versao,
    bool Ativa,
    string Severidade,
    string SeveridadePadrao,
    bool Configurada,
    DateTimeOffset? AlteradoEm,
    IReadOnlyList<ParametroResposta> Parametros);

/// <summary>
/// O que o cliente pode mudar numa regra.
///
/// ⚠️ **Nao contem `Codigo`.** O codigo vem da rota, e nao do corpo: aceitar os
/// dois abriria a porta para configurar uma regra e gravar em outra
/// (`CLAUDE.md secao 24.7`, mass assignment).
///
/// Tambem nao contem `Versao`, `Categoria` nem `Explicacao` - esses sao da
/// regra, que e codigo do sistema. Aceita-los deixaria o cliente reescrever o
/// catalogo.
/// </summary>
public sealed record ConfigurarRegraRequisicao(
    bool Ativa,
    Severidade Severidade,
    Dictionary<string, string?>? Parametros);

public sealed record ResultadoResposta(
    Guid Id,
    string Codigo,
    string Regra,
    int VersaoRegra,
    string Categoria,
    string Severidade,
    Guid? IdFolhaFuncionario,
    string? Matricula,
    string? NomeFuncionario,
    string Descricao,
    decimal? ValorEsperado,
    decimal? ValorEncontrado,
    decimal? Diferenca,
    string? Contexto);

public sealed record ExecucaoResposta(
    Guid Id,
    Guid IdFolha,
    string Competencia,
    int VersaoCalculoDaFolha,
    DateTimeOffset ExecutadaEm,
    int RegrasExecutadas,
    int TotalResultados,
    int ResultadosAltos,
    int ResultadosMedios,
    int ResultadosBaixos,
    bool Desatualizada,
    IReadOnlyList<ResultadoResposta>? Resultados);

/// <summary>
/// Motor de analises (Fase 6).
///
/// ## Tres niveis de permissao, e nao um
///
/// O Security Gate da Fase 6 e explicito: "configurar regra e administracao;
/// executar analise e operacao; consultar resultado e leitura. Tres niveis
/// distintos."
///
/// <code>
/// GET  /api/regras-analise            -> LerDadosEmpresariais
/// PUT  /api/regras-analise/{codigo}   -> AdministrarEmpresas   (administracao)
/// POST /api/folhas/{id}/analisar      -> ProcessarFolha        (operacao)
/// GET  /api/folhas/{id}/analises      -> LerDadosEmpresariais
/// GET  /api/analises/{id}             -> LerDadosEmpresariais
/// </code>
///
/// A separacao nao e formalidade: afrouxar uma tolerancia e o jeito mais barato
/// de fazer uma divergencia sumir do relatorio, e quem faz isso nao deve ser a
/// mesma pessoa que roda a analise no dia a dia.
///
/// ## O que o usuario NAO pode fazer
///
/// Escrever regra, escrever SQL, escrever expressao. O codigo da regra vem de
/// um enum fechado; os parametros, de uma lista que a propria regra declarou,
/// com tipo e faixa. O que estiver fora e recusado antes de virar
/// comportamento.
/// </summary>
public static class AnalisesEndpoints
{
    public static IEndpointRouteBuilder MapearAnalises(this IEndpointRouteBuilder rotas)
    {
        var regras = rotas.MapGroup("/api/regras-analise").WithTags("Analises");

        regras.MapGet("/", ListarRegrasAsync)
            .WithSummary("Catalogo de regras, com a configuracao da organizacao")
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        regras.MapPut("/{codigo}", ConfigurarRegraAsync)
            .WithSummary("Liga, desliga, muda severidade e parametros de uma regra")
            .RequireAuthorization(PoliticasAutorizacao.AdministrarEmpresas);

        var folhas = rotas.MapGroup("/api/folhas").WithTags("Analises");

        folhas.MapPost("/{id:guid}/analisar", AnalisarAsync)
            .WithSummary("Roda as regras ativas sobre a folha e grava o resultado")
            .RequireAuthorization(PoliticasAutorizacao.ProcessarFolha);

        folhas.MapGet("/{id:guid}/analises", ListarExecucoesAsync)
            .WithSummary("Historico de analises da folha")
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        rotas.MapGet("/api/analises/{id:guid}", ObterExecucaoAsync)
            .WithTags("Analises")
            .WithSummary("Uma execucao, com os resultados")
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        return rotas;
    }

    // ---------------------------------------------------------------- catalogo

    /// <summary>
    /// O catalogo INTEIRO, com a configuracao da organizacao sobreposta.
    ///
    /// Devolve as seis regras sempre, e nao so as configuradas: regra sem linha
    /// no banco roda ativa no padrao, e esconde-la da tela faria a pessoa achar
    /// que ela nao existe.
    ///
    /// Sem paginacao, e de proposito: o catalogo tem tamanho fixo, definido em
    /// codigo. Nao ha volume que cresca com o uso.
    /// </summary>
    private static async Task<IResult> ListarRegrasAsync(PrismaRhDbContext db, CancellationToken ct)
    {
        var configuradas = await CarregarConfiguracoesAsync(db, ct);

        return Results.Ok(CatalogoRegras.Todas
            .Select(regra => Descrever(regra, configuradas.GetValueOrDefault(regra.Codigo)))
            .ToList());
    }

    private static async Task<IResult> ConfigurarRegraAsync(
        string codigo,
        ConfigurarRegraRequisicao requisicao,
        PrismaRhDbContext db,
        IContextoUsuario usuario,
        IRelogio relogio,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(requisicao);

        // Vocabulario fechado: o texto da rota tem que casar com um valor do
        // enum. Qualquer outra coisa nao vira regra - vira 404.
        if (!Enum.TryParse<CodigoRegra>(codigo, ignoreCase: true, out var valor)
            || !CatalogoRegras.Conhece(valor))
        {
            return Results.NotFound();
        }

        if (!Enum.IsDefined(requisicao.Severidade))
        {
            return Results.BadRequest(new { detalhe = "Severidade invalida." });
        }

        var regra = CatalogoRegras.De(valor)!;

        var (valores, erros) = ValoresParametros.Interpretar(
            regra.Parametros,
            requisicao.Parametros ?? []);

        if (erros.Count > 0)
        {
            return Results.BadRequest(new { detalhe = string.Join(" ", erros), erros });
        }

        var configuracao = await db.RegrasAnalise
            .Include(r => r.Parametros)
            .FirstOrDefaultAsync(r => r.Codigo == valor, ct);

        if (configuracao is null)
        {
            configuracao = new RegraAnalise(usuario.IdOrganizacao, valor, relogio.Agora);
            db.RegrasAnalise.Add(configuracao);
        }

        configuracao.Configurar(
            requisicao.Ativa, requisicao.Severidade, valores, usuario.IdUsuario, relogio.Agora);

        // ⚠️ Resolve a pendencia do `CLAUDE.md secao 24.19 item 7`, aberta na
        // Fase 6: a linha da regra guarda so a ULTIMA alteracao, e afrouxar uma
        // tolerancia e o jeito mais barato de fazer uma divergencia sumir do
        // relatorio. Agora cada alteracao vira um evento com os valores.
        var descricaoParametros = valores.Todos.Count == 0
            ? "sem parametros"
            : string.Join(
                ";",
                valores.Todos.Select(p => $"{p.Key}={DefinicaoParametro.Formatar(p.Value)}"));

        db.Registrar(
            usuario, relogio,
            AcaoAuditada.RegraAnaliseConfigurada, EntidadeAuditada.RegraAnalise, configuracao.Id,
            $"Regra '{regra.Nome}' configurada: "
            + $"{(requisicao.Ativa ? "ativa" : "desligada")}, severidade {requisicao.Severidade}.",
            $"codigo={valor};ativa={requisicao.Ativa};"
            + $"severidade={requisicao.Severidade};{descricaoParametros}");

        await db.SaveChangesAsync(ct);

        return Results.Ok(Descrever(regra, configuracao));
    }

    // ---------------------------------------------------------------- execucao

    /// <summary>
    /// Roda as regras e grava a execucao.
    ///
    /// **Rascunho nao e analisado.** Uma folha em rascunho ainda nao tem
    /// holerite calculado: analisa-la produziria "todo mundo ausente" e nada
    /// mais - um relatorio inteiro de alarme falso que ensina a ignorar o
    /// relatorio.
    ///
    /// Analisar de novo cria uma execucao NOVA, e nao substitui a anterior. O
    /// `ROADMAP.md` pede historico de execucao, e comparar duas passadas e
    /// exatamente o que mostra se a correcao funcionou.
    /// </summary>
    private static async Task<IResult> AnalisarAsync(
        Guid id,
        PrismaRhDbContext db,
        IContextoUsuario usuario,
        IRelogio relogio,
        CancellationToken ct)
    {
        // Pelo filtro global: folha de outra organizacao nao existe daqui, e a
        // resposta e 404 - nunca 403.
        var folha = await db.Folhas.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);

        if (folha is null)
        {
            return Results.NotFound();
        }

        if (folha.Situacao == Dominio.Folha.SituacaoFolha.Rascunho)
        {
            return Results.BadRequest(new
            {
                detalhe = "Calcule a folha antes de analisar. Em rascunho nao ha holerite para conferir.",
            });
        }

        var contexto = await MontadorContextoAnalise.MontarAsync(db, folha, ct);
        var configuracoes = await CarregarConfiguracoesAsync(db, ct);

        var execucao = MotorAnalises.Executar(
            usuario.IdOrganizacao,
            contexto,
            configuracoes,
            folha.VersaoCalculo,
            usuario.IdUsuario,
            relogio.Agora);

        db.ExecucoesAnalise.Add(execucao);

        db.Registrar(
            usuario, relogio,
            AcaoAuditada.AnaliseExecutada, EntidadeAuditada.ExecucaoAnalise, execucao.Id,
            $"Analise da folha de {folha.Competencia}: "
            + $"{execucao.TotalResultados} inconsistencias em {execucao.RegrasExecutadas} regras.",
            $"folha={folha.Id};altas={execucao.ResultadosAltos};"
            + $"medias={execucao.ResultadosMedios};baixas={execucao.ResultadosBaixos}");

        await db.SaveChangesAsync(ct);

        return Results.Ok(Descrever(execucao, folha.VersaoCalculo, comResultados: true));
    }

    private static async Task<IResult> ListarExecucoesAsync(
        Guid id,
        PrismaRhDbContext db,
        CancellationToken ct,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanho = 25)
    {
        var folha = await db.Folhas.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);

        if (folha is null)
        {
            return Results.NotFound();
        }

        // Teto na listagem, como toda listagem nova desde a Fase 4G.
        var porPagina = Math.Clamp(tamanho, 1, 100);
        var salto = (Math.Max(pagina, 1) - 1) * porPagina;

        var consulta = db.ExecucoesAnalise
            .AsNoTracking()
            .Where(e => e.IdFolha == id)
            .OrderByDescending(e => e.ExecutadaEm);

        var total = await consulta.CountAsync(ct);

        var execucoes = await consulta.Skip(salto).Take(porPagina).ToListAsync(ct);

        return Results.Ok(new
        {
            Total = total,
            Pagina = Math.Max(pagina, 1),
            Itens = execucoes
                .Select(e => Descrever(e, folha.VersaoCalculo, comResultados: false))
                .ToList(),
        });
    }

    private static async Task<IResult> ObterExecucaoAsync(
        Guid id, PrismaRhDbContext db, CancellationToken ct)
    {
        var execucao = await db.ExecucoesAnalise
            .AsNoTracking()
            .Include(e => e.Resultados)
            .FirstOrDefaultAsync(e => e.Id == id, ct);

        if (execucao is null)
        {
            return Results.NotFound();
        }

        var versaoAtual = await db.Folhas
            .AsNoTracking()
            .Where(f => f.Id == execucao.IdFolha)
            .Select(f => (int?)f.VersaoCalculo)
            .FirstOrDefaultAsync(ct);

        return Results.Ok(Descrever(
            execucao, versaoAtual ?? execucao.VersaoCalculoDaFolha, comResultados: true));
    }

    // ------------------------------------------------------------------ apoio

    /// <summary>
    /// A configuracao da organizacao, por codigo.
    ///
    /// Passa pelo filtro global. Configuracao gravada para um codigo que esta
    /// versao do sistema nao conhece mais e descartada aqui, em vez de derrubar
    /// a listagem inteira.
    /// </summary>
    private static async Task<Dictionary<CodigoRegra, RegraAnalise>> CarregarConfiguracoesAsync(
        PrismaRhDbContext db, CancellationToken ct)
    {
        var linhas = await db.RegrasAnalise
            .AsNoTracking()
            .Include(r => r.Parametros)
            .ToListAsync(ct);

        return linhas
            .Where(r => CatalogoRegras.Conhece(r.Codigo))
            .GroupBy(r => r.Codigo)
            .ToDictionary(g => g.Key, g => g.First());
    }

    private static RegraResposta Descrever(IRegraAnalise regra, RegraAnalise? configuracao)
    {
        var gravados = configuracao?.ValoresGravados() ?? new Dictionary<string, string?>();
        var (valores, _) = ValoresParametros.Interpretar(regra.Parametros, gravados);

        return new RegraResposta(
            regra.Codigo.ToString(),
            regra.Nome,
            regra.Explicacao,
            regra.Categoria.ToString(),
            regra.Versao,
            configuracao?.Ativa ?? true,
            (configuracao?.Severidade ?? regra.SeveridadePadrao).ToString(),
            regra.SeveridadePadrao.ToString(),
            configuracao is not null,
            configuracao?.AlteradoEm,
            [.. regra.Parametros.Select(d => new ParametroResposta(
                d.Chave,
                d.Rotulo,
                d.Explicacao,
                d.Tipo.ToString(),
                DefinicaoParametro.Formatar(d.Padrao),
                DefinicaoParametro.Formatar(d.Minimo),
                DefinicaoParametro.Formatar(d.Maximo),
                DefinicaoParametro.Formatar(valores.Obter(d.Chave))))]);
    }

    private static ExecucaoResposta Descrever(
        ExecucaoAnalise execucao, int versaoAtualDaFolha, bool comResultados)
    {
        return new ExecucaoResposta(
            execucao.Id,
            execucao.IdFolha,
            execucao.Competencia.ToString(),
            execucao.VersaoCalculoDaFolha,
            execucao.ExecutadaEm,
            execucao.RegrasExecutadas,
            execucao.TotalResultados,
            execucao.ResultadosAltos,
            execucao.ResultadosMedios,
            execucao.ResultadosBaixos,

            // A folha foi recalculada depois desta analise? Entao ela nao fala
            // mais da folha que esta no ar. Dizer isso e melhor que apagar a
            // execucao velha: apagar perderia o historico que o roadmap pede.
            execucao.VersaoCalculoDaFolha != versaoAtualDaFolha,

            comResultados
                ? [.. execucao.Resultados
                    .OrderByDescending(r => r.Severidade)
                    .ThenBy(r => r.Matricula, StringComparer.Ordinal)
                    .Select(r => new ResultadoResposta(
                        r.Id,
                        r.Codigo.ToString(),
                        CatalogoRegras.De(r.Codigo)?.Nome ?? r.Codigo.ToString(),
                        r.VersaoRegra,
                        r.Categoria.ToString(),
                        r.Severidade.ToString(),
                        r.IdFolhaFuncionario,
                        r.Matricula,
                        r.NomeFuncionario,
                        r.Descricao,
                        r.ValorEsperado,
                        r.ValorEncontrado,
                        r.Diferenca,
                        r.Contexto))]
                : null);
    }
}
