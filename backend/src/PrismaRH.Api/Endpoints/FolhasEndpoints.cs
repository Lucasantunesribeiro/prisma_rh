using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrismaRH.Api.Identidade;
using PrismaRH.Aplicacao.Comum;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Dominio.Ferias;
using PrismaRH.Dominio.Rescisao;
using PrismaRH.Dominio.Folha;
using PrismaRH.Dominio.Parametros;
using PrismaRH.Dominio.Auditoria;
using PrismaRH.Infraestrutura.Auditoria;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Api.Endpoints;

public sealed record AbrirFolhaRequisicao(
    Guid IdEmpresa,
    string Competencia,
    TipoFolha Tipo = TipoFolha.Mensal);

public sealed record LancamentoManualRequisicao(Guid IdRubrica, decimal Valor, string? Referencia);

public sealed record FolhaResumoResposta(
    Guid Id,
    Guid IdEmpresa,
    string Empresa,
    string Competencia,
    TipoFolha Tipo,
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
    BaseCalculo BasesIncidentes,
    IReadOnlyList<LinhaMemoriaResposta> Memoria);

/// <summary>
/// Uma base de calculo do holerite, com os codigos das rubricas que a
/// formaram.
///
/// A composicao e DERIVADA, nao gravada: cada lancamento ja carrega a
/// incidencia congelada, entao dizer quais entraram na base e filtrar o que
/// ja veio. Gravar os passos duplicaria dado que esta na mesma tela - ao
/// contrario do salario proporcional, cujos passos usam valores que nao
/// sobrevivem em lugar nenhum.
/// </summary>
public sealed record BaseApuradaResposta(
    BaseCalculo Base,
    decimal Valor,
    IReadOnlyList<string> Composta);

public sealed record HoleriteResposta(
    HoleriteResumoResposta Resumo,
    string Competencia,
    SituacaoFolha SituacaoFolha,
    IReadOnlyList<LancamentoResposta> Lancamentos,
    IReadOnlyList<BaseApuradaResposta> Bases);

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
                x.Folha.Tipo,
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
                l.BasesIncidentes,
                l.Memoria
                    .OrderBy(m => m.Ordem)
                    .Select(m => new LinhaMemoriaResposta(m.Ordem, m.Descricao, m.Expressao, m.Valor))
                    .ToList()))
            .ToListAsync(ct);

        var bases = await db.BasesApuradas
            .AsNoTracking()
            .Where(b => b.IdFolhaFuncionario == idHolerite)
            .OrderBy(b => b.Base)
            .Select(b => new { b.Base, b.Valor })
            .ToListAsync(ct);

        // A composicao e montada em memoria, sobre os lancamentos ja lidos
        // acima: o teste de bit nao traduz para SQL, e uma segunda ida ao
        // banco por base seria tres consultas para dizer o que ja esta aqui.
        var basesResposta = bases
            .Select(b => new BaseApuradaResposta(
                b.Base,
                b.Valor,
                [.. lancamentos
                    .Where(l => l.BasesIncidentes.HasFlag(b.Base) && l.Tipo != TipoRubrica.Desconto)
                    .Select(l => l.CodigoRubrica)]))
            .ToList();

        return Results.Ok(new HoleriteResposta(
            resumo, folha.Competencia.ToString(), folha.Situacao, lancamentos, basesResposta));
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

        if (!Enum.IsDefined(requisicao.Tipo))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["tipo"] = ["Tipo de folha desconhecido."]
            });
        }

        // A checagem inclui o TIPO: a mesma empresa pode ter, em agosto, a
        // folha mensal E a de ferias. O indice unico no banco garante o mesmo,
        // mas a mensagem daqui explica.
        if (await db.Folhas.AnyAsync(
                f => f.IdEmpresa == requisicao.IdEmpresa
                     && f.Competencia == competencia
                     && f.Tipo == requisicao.Tipo, ct))
        {
            return Results.Conflict(new
            {
                detalhe = $"A folha {requisicao.Tipo} de {competencia} desta empresa ja foi aberta."
            });
        }

        var folha = new FolhaPagamento(
            usuario.IdOrganizacao, requisicao.IdEmpresa, competencia, relogio.Agora, requisicao.Tipo);

        db.Folhas.Add(folha);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/folhas/{folha.Id}", await ResumoAsync(db, folha.Id, ct));
    }

    private static async Task<IResult> CalcularAsync(
        Guid id,
        PrismaRhDbContext db,
        IContextoUsuario usuario,
        IRelogio relogio,
        CancellationToken ct)
    {
        var folha = await CarregarParaEscritaAsync(db, id, ct);

        if (folha is null)
        {
            return Results.NotFound();
        }

        if (folha.Tipo == TipoFolha.Ferias)
        {
            return await CalcularFeriasAsync(folha, db, relogio, ct);
        }

        if (folha.Tipo is TipoFolha.DecimoTerceiroAdiantamento or TipoFolha.DecimoTerceiro)
        {
            return await Calcular13Async(folha, db, relogio, ct);
        }

        if (folha.Tipo == TipoFolha.Rescisao)
        {
            return await CalcularRescisaoAsync(folha, db, relogio, ct);
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

        // O catalogo inteiro, para reaplicar a incidencia atual nos
        // lancamentos manuais. Sem paginacao de proposito: e o catalogo da
        // organizacao, e o motor precisa dele completo em memoria.
        var catalogo = await db.Rubricas.ToListAsync(ct);

        try
        {
            folha.Calcular(
                contratos, rubricaSalario, catalogo,
                await EncargosAsync(db, folha.Competencia, ct),
                await DependentesPorFuncionarioAsync(db, folha.Competencia, ct),
                relogio.Agora);
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
        Guid id,
        PrismaRhDbContext db,
        IContextoUsuario usuario,
        IRelogio relogio,
        CancellationToken ct)
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

        // Fechamento de folha e evento auditado (`CLAUDE.md secao 24.17`): e o
        // ato que transforma um calculo em fato historico.
        db.Registrar(
            usuario, relogio,
            AcaoAuditada.FolhaFechada, EntidadeAuditada.FolhaPagamento, folha.Id,
            $"Folha {folha.Tipo} de {folha.Competencia} fechada.",
            $"liquido={folha.TotalLiquido};pessoas={folha.Funcionarios.Count}");

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
            folha.AdicionarLancamentoManual(
                idHolerite, rubrica, requisicao.Valor, requisicao.Referencia,
                await EncargosAsync(db, folha.Competencia, ct));
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
            removeu = folha.RemoverLancamento(
                idHolerite, idLancamento,
                await EncargosAsync(db, folha.Competencia, ct));
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
    /// <summary>
    /// Monta os parametros de INSS para a competencia da folha.
    ///
    /// Devolve null quando a organizacao ainda nao tem rubrica de INSS ativa,
    /// ou quando nenhuma tabela comecou ate aquela competencia. Nos dois casos
    /// a folha calcula sem o desconto - e o comportamento da Fase 3, que
    /// continua valido para quem nao configurou encargo.
    /// </summary>
    /// <summary>
    /// Calcula a folha de RESCISAO: paga os acertos dos desligados na
    /// competencia.
    ///
    /// Exige as NOVE rubricas de rescisao cadastradas, pelo mesmo motivo das
    /// ferias: faltando alguma, uma verba sairia em silencio do acerto.
    ///
    /// Contratos com motivo BLOQUEADO nao entram, e a resposta diz quantos
    /// ficaram de fora - um holerite vazio no meio da folha pareceria erro de
    /// calculo em vez de motivo sem fonte.
    /// </summary>
    private static async Task<IResult> CalcularRescisaoAsync(
        FolhaPagamento folha, PrismaRhDbContext db, IRelogio relogio, CancellationToken ct)
    {
        var rubricas = await db.Rubricas
            .Where(r => r.Ativa && r.Estrategia == EstrategiaRubrica.VerbaRescisoria)
            .ToDictionaryAsync(r => r.Codigo, ct);

        string[] exigidas =
        [
            "SALDO", "AVISO", "FERVEN", "FERVEN13", "FERPROP",
            "FERPROP13", "DEC13PROP", "DEC13AV", "MULTAFGTS",
        ];

        var faltando = exigidas.Where(c => !rubricas.ContainsKey(c)).ToList();

        if (faltando.Count > 0)
        {
            return Results.Conflict(new
            {
                detalhe = "Faltam rubricas de rescisao ativas: " + string.Join(", ", faltando)
                    + ". Cadastre-as antes de calcular."
            });
        }

        var primeiro = folha.Competencia.PrimeiroDia;
        var ultimo = folha.Competencia.UltimoDia;

        var contratos = await db.ContratosTrabalho
            .Include(c => c.Vigencias)
            .Where(c => c.IdEmpresa == folha.IdEmpresa
                        && c.DataDesligamento != null
                        && c.DataDesligamento >= primeiro
                        && c.DataDesligamento <= ultimo)
            .ToListAsync(ct);

        var ids = contratos.Select(c => c.Id).ToList();

        var concessoes = await db.ConcessoesFerias
            .AsNoTracking()
            .Where(c => ids.Contains(c.IdContrato))
            .ToListAsync(ct);

        // Dias de ferias vencidas por contrato, na data de saida de cada um.
        var vencidas = contratos.ToDictionary(
            c => c.Id,
            c => PeriodosAquisitivos.Adquiridos(c, c.DataDesligamento!.Value)
                .Select(p => new PeriodoComSaldo(p, [.. concessoes.Where(x => x.EDoPeriodo(p))]))
                .Sum(p => p.Saldo));

        var informados = await db.ValoresBaseFgts
            .AsNoTracking()
            .Where(v => ids.Contains(v.IdContrato))
            .ToListAsync(ct);

        // O FGTS que o sistema conhece, por contrato - so para comparacao.
        var conhecidos = await db.LancamentosFolha
            .AsNoTracking()
            .Where(l => l.Estrategia == EstrategiaRubrica.FgtsMensal)
            .Join(db.FolhasFuncionario, l => l.IdFolhaFuncionario, f => f.Id,
                (l, f) => new { f.IdContrato, l.Valor })
            .Where(x => ids.Contains(x.IdContrato))
            .GroupBy(x => x.IdContrato)
            .Select(g => new { IdContrato = g.Key, Total = g.Sum(x => x.Valor) })
            .ToListAsync(ct);

        var conhecidoPorContrato = conhecidos.ToDictionary(c => c.IdContrato, c => c.Total);

        var basesFgts = informados.ToDictionary(
            v => v.IdContrato,
            v => new ValorBaseFgts(
                v.Valor,
                conhecidoPorContrato.TryGetValue(v.IdContrato, out var c) ? c : 0m));

        IReadOnlyList<Guid> ignorados;

        try
        {
            ignorados = folha.CalcularRescisao(
                contratos, rubricas, vencidas, basesFgts,
                await EncargosAsync(db, folha.Competencia, ct),
                await DependentesPorFuncionarioAsync(db, folha.Competencia, ct),
                relogio.Agora);
        }
        catch (InvalidOperationException erro)
        {
            return RespostasValidacao.De(erro);
        }

        await db.SaveChangesAsync(ct);

        var detalhe = new FolhaDetalheResposta(
            (await ResumoAsync(db, folha.Id, ct))!, await HoleritesAsync(db, folha.Id, ct));

        return ignorados.Count == 0
            ? Results.Ok(detalhe)
            : Results.Ok(new
            {
                detalhe.Folha,
                detalhe.Funcionarios,
                // Quantos ficaram de fora por motivo bloqueado. Nao e erro -
                // e informacao que a tela precisa mostrar.
                ContratosIgnorados = ignorados,
            });
    }

    /// <summary>
    /// Calcula a folha de FERIAS: paga as concessoes que comecam na
    /// competencia.
    ///
    /// Exige as QUATRO rubricas de ferias cadastradas. Faltando alguma, a
    /// folha sairia incompleta em silencio - e o funcionario receberia menos
    /// do que deveria sem nada parecer errado.
    /// </summary>
    /// <summary>
    /// Calcula a folha do 13o SALARIO - o adiantamento ou a anual.
    ///
    /// Exige as QUATRO rubricas do 13o cadastradas, pelo mesmo motivo das
    /// ferias e da rescisao: faltando uma, a parcela correspondente sairia do
    /// holerite em silencio. E aqui o silencio seria pior - faltando a
    /// informativa DEC13FG, a folha anual fecharia certinho no liquido e
    /// recolheria FGTS a MENOS, sem nada parecer errado.
    ///
    /// ## De onde vem o adiantamento ja pago
    ///
    /// Das folhas de ADIANTAMENTO do mesmo ano, ja calculadas. E estado
    /// derivado, nao um campo que alguem digita - mesma decisao dos periodos
    /// aquisitivos e dos avos.
    ///
    /// A soma e feita no BANCO, por estrategia congelada no lancamento. Ler a
    /// rubrica atual traria o valor errado se o catalogo tiver mudado depois do
    /// pagamento.
    /// </summary>
    private static async Task<IResult> Calcular13Async(
        FolhaPagamento folha, PrismaRhDbContext db, IRelogio relogio, CancellationToken ct)
    {
        var rubricas = await db.Rubricas
            .Where(r => r.Ativa && Rubrica.EstrategiasDe13.Contains(r.Estrategia))
            .ToDictionaryAsync(r => r.Estrategia, ct);

        var faltando = Rubrica.EstrategiasDe13
            .Where(e => !rubricas.ContainsKey(e))
            .ToList();

        if (faltando.Count > 0)
        {
            return Results.Conflict(new
            {
                detalhe = "Faltam rubricas de 13o salario ativas: " + string.Join(", ", faltando)
                    + ". Cadastre-as antes de calcular."
            });
        }

        var contratos = await db.ContratosTrabalho
            .Include(c => c.Vigencias)
            .Where(c => c.IdEmpresa == folha.IdEmpresa)
            .ToListAsync(ct);

        var adiantamentos = new Dictionary<Guid, decimal>();

        if (folha.Tipo == TipoFolha.DecimoTerceiro)
        {
            // As doze competencias do ano, e nao "Competencia.Ano == ano".
            //
            // Competencia e gravada como o inteiro 202611 por um value
            // converter (ver FolhaPagamentoConfiguracao): para o EF Core a
            // propriedade e opaca, e ler .Ano dela nao tem traducao para SQL -
            // a consulta estourava com 500. Mesma licao do Contains de
            // IReadOnlySet na Fase 4E.
            //
            // ARRAY, pelo mesmo motivo de sempre: o EF traduz Contains de array
            // para IN (...), com os inteiros ja convertidos.
            var competenciasDoAno = Enumerable.Range(1, 12)
                .Select(mes => new Competencia(folha.Competencia.Ano, mes))
                .ToArray();

            var pagos = await db.LancamentosFolha
                .AsNoTracking()
                .Where(l => l.Estrategia == EstrategiaRubrica.DecimoTerceiroAdiantamento)
                .Join(db.FolhasFuncionario, l => l.IdFolhaFuncionario, f => f.Id,
                    (l, f) => new { f.IdContrato, f.IdFolha, l.Valor })
                .Join(db.Folhas, x => x.IdFolha, f => f.Id,
                    (x, f) => new { x.IdContrato, x.Valor, f.Competencia, f.IdEmpresa, f.Tipo })
                .Where(x => x.IdEmpresa == folha.IdEmpresa
                            && x.Tipo == TipoFolha.DecimoTerceiroAdiantamento
                            && competenciasDoAno.Contains(x.Competencia))
                .GroupBy(x => x.IdContrato)
                .Select(g => new { IdContrato = g.Key, Total = g.Sum(x => x.Valor) })
                .ToListAsync(ct);

            adiantamentos = pagos.ToDictionary(x => x.IdContrato, x => x.Total);
        }

        try
        {
            folha.Calcular13(
                contratos, rubricas, adiantamentos,
                await EncargosAsync(db, folha.Competencia, ct),
                await DependentesPorFuncionarioAsync(db, folha.Competencia, ct),
                relogio.Agora);
        }
        catch (InvalidOperationException erro)
        {
            return RespostasValidacao.De(erro);
        }

        await db.SaveChangesAsync(ct);

        return Results.Ok(new FolhaDetalheResposta(
            (await ResumoAsync(db, folha.Id, ct))!, await HoleritesAsync(db, folha.Id, ct)));
    }

    private static async Task<IResult> CalcularFeriasAsync(
        FolhaPagamento folha, PrismaRhDbContext db, IRelogio relogio, CancellationToken ct)
    {
        var rubricas = await db.Rubricas
            .Where(r => r.Ativa && Rubrica.EstrategiasDeFerias.Contains(r.Estrategia))
            .ToDictionaryAsync(r => r.Estrategia, ct);

        var faltando = Rubrica.EstrategiasDeFerias
            .Where(e => !rubricas.ContainsKey(e))
            .ToList();

        if (faltando.Count > 0)
        {
            return Results.Conflict(new
            {
                detalhe = "Faltam rubricas de ferias ativas: " + string.Join(", ", faltando)
                    + ". Cadastre-as antes de calcular."
            });
        }

        var contratos = await db.ContratosTrabalho
            .Include(c => c.Vigencias)
            .Where(c => c.IdEmpresa == folha.IdEmpresa)
            .ToListAsync(ct);

        var idsContratos = contratos.Select(c => c.Id).ToList();

        // So as concessoes que COMECAM na competencia. O filtro por data vai
        // ao banco, e nao para a memoria: uma empresa grande tem muito mais
        // concessao acumulada do que ferias no mes.
        var primeiro = folha.Competencia.PrimeiroDia;
        var ultimo = folha.Competencia.UltimoDia;

        var concessoes = await db.ConcessoesFerias
            .AsNoTracking()
            .Where(c => idsContratos.Contains(c.IdContrato)
                        && c.Inicio >= primeiro && c.Inicio <= ultimo)
            .ToListAsync(ct);

        try
        {
            folha.CalcularFerias(
                contratos, concessoes, rubricas,
                await EncargosAsync(db, folha.Competencia, ct),
                await DependentesPorFuncionarioAsync(db, folha.Competencia, ct),
                relogio.Agora);
        }
        catch (InvalidOperationException erro)
        {
            return RespostasValidacao.De(erro);
        }

        await db.SaveChangesAsync(ct);

        return Results.Ok(new FolhaDetalheResposta(
            (await ResumoAsync(db, folha.Id, ct))!, await HoleritesAsync(db, folha.Id, ct)));
    }

    /// <summary>
    /// Todos os parametros legais da competencia, de uma vez.
    ///
    /// Os tres nao sao independentes na hora de usar: o IRRF deduz o INSS. Ler
    /// os tres juntos deixa isso explicito e evita a assinatura de quatro
    /// parametros nulaveis que o motor tinha comecado a virar.
    /// </summary>
    private static async Task<ParametrosEncargos> EncargosAsync(
        PrismaRhDbContext db, Competencia competencia, CancellationToken ct) =>
        new(
            await ParametrosInssAsync(db, competencia, ct),
            await ParametrosFgtsAsync(db, competencia, ct),
            await ParametrosIrrfAsync(db, competencia, ct));

    /// <summary>
    /// Quantos dependentes de cada funcionario abatem IRRF NESTA competencia.
    ///
    /// A pergunta e feita ao banco com o periodo declarado em cada dependente,
    /// e nao carregando todos para filtrar em memoria: uma organizacao com
    /// muitos funcionarios traria muito dado pessoal de terceiro para dentro
    /// do processo sem necessidade (CLAUDE.md secao 25).
    ///
    /// A comparacao repete DedutivelEm: o periodo TOCA a competencia. Quem
    /// passa a contar no dia 20 conta o mes inteiro.
    ///
    /// Passa pelo filtro global, entao so enxerga a organizacao autenticada.
    /// </summary>
    private static async Task<IReadOnlyDictionary<Guid, int>> DependentesPorFuncionarioAsync(
        PrismaRhDbContext db, Competencia competencia, CancellationToken ct)
    {
        var primeiro = competencia.PrimeiroDia;
        var ultimo = competencia.UltimoDia;

        var contagens = await db.Dependentes
            .AsNoTracking()
            .Where(d => d.InicioDeducaoIrrf != null
                        && d.InicioDeducaoIrrf <= ultimo
                        && (d.FimDeducaoIrrf == null || d.FimDeducaoIrrf >= primeiro))
            .GroupBy(d => d.IdFuncionario)
            .Select(g => new { IdFuncionario = g.Key, Quantidade = g.Count() })
            .ToListAsync(ct);

        return contagens.ToDictionary(c => c.IdFuncionario, c => c.Quantidade);
    }

    /// <summary>
    /// Monta os parametros de IRRF para a competencia da folha.
    ///
    /// Null quando a organizacao nao tem rubrica de IRRF ativa ou quando
    /// nenhuma tabela comecou ate ali. Nos dois casos a folha calcula sem o
    /// desconto - melhor do que aplicar a tabela mais proxima que encontrar.
    /// </summary>
    private static async Task<ParametrosIrrf?> ParametrosIrrfAsync(
        PrismaRhDbContext db, Competencia competencia, CancellationToken ct)
    {
        var rubrica = await db.Rubricas.FirstOrDefaultAsync(
            r => r.Ativa && r.Estrategia == EstrategiaRubrica.IrrfMensal, ct);

        if (rubrica is null)
        {
            return null;
        }

        var tabelas = await db.TabelasIrrf.AsNoTracking().Include(t => t.Faixas).ToListAsync(ct);

        return ParametrosIrrf.Montar(rubrica, tabelas, competencia);
    }

    private static async Task<ParametrosInss?> ParametrosInssAsync(
        PrismaRhDbContext db, Competencia competencia, CancellationToken ct)
    {
        var rubrica = await db.Rubricas.FirstOrDefaultAsync(
            r => r.Ativa && r.Estrategia == EstrategiaRubrica.InssProgressivo, ct);

        if (rubrica is null)
        {
            return null;
        }

        // As faixas vem juntas: o motor nao acessa banco durante o calculo
        // (CLAUDE.md secao 10).
        var tabelas = await db.TabelasInss.AsNoTracking().Include(t => t.Faixas).ToListAsync(ct);

        return ParametrosInss.Montar(rubrica, tabelas, competencia);
    }

    /// <summary>
    /// Monta os parametros de FGTS para a competencia da folha.
    ///
    /// Null quando a organizacao nao tem rubrica de FGTS ativa ou quando
    /// nenhuma aliquota comecou ate ali. Nos dois casos a folha calcula sem a
    /// linha informativa, e o liquido nao muda - FGTS nunca entrou nele.
    /// </summary>
    private static async Task<ParametrosFgts?> ParametrosFgtsAsync(
        PrismaRhDbContext db, Competencia competencia, CancellationToken ct)
    {
        var rubrica = await db.Rubricas.FirstOrDefaultAsync(
            r => r.Ativa && r.Estrategia == EstrategiaRubrica.FgtsMensal, ct);

        if (rubrica is null)
        {
            return null;
        }

        var tabelas = await db.TabelasFgts.AsNoTracking().ToListAsync(ct);

        return ParametrosFgts.Montar(rubrica, tabelas, competencia);
    }

    private static Task<FolhaPagamento?> CarregarParaEscritaAsync(
        PrismaRhDbContext db, Guid id, CancellationToken ct) =>
        db.Folhas
            .Include(f => f.Funcionarios)
            .ThenInclude(ff => ff.Lancamentos)
            // Sem carregar as bases, ApurarBases nao encontraria as linhas
            // existentes e criaria tres novas a cada recalculo. O indice unico
            // ux_bases_apuradas_holerite_base recusaria - com uma violacao de
            // constraint no lugar de um erro compreensivel.
            .Include(f => f.Funcionarios)
            .ThenInclude(ff => ff.Bases)
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
                x.Folha.Tipo,
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
