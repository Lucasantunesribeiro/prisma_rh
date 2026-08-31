using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrismaRH.Dominio.Auditoria;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Testes.Isolamento;

/// <summary>
/// Workflow de tratamento, auditoria e painel (Fase 7), contra PostgreSQL real.
///
/// Os testes de dominio provam a maquina de estados. Estes provam o que so o
/// sistema inteiro responde: **quem pode tratar, quem so le, se alguem consegue
/// mexer na trilha de auditoria, e se um responsavel de outra organizacao passa.**
///
/// Organizacao I, a mesma do motor de analises - o workflow trata exatamente o
/// que aquele motor produz.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class WorkflowHttpTestes(BancoPostgresFixture banco) : IDisposable
{
    private readonly FabricaApiIsolada _fabrica = new(banco.StringConexao);

    public void Dispose() => _fabrica.Dispose();

    private sealed record Identificado(Guid Id);

    private sealed record AndamentoItem(
        Guid Id, string Tipo, string? Autor, DateTimeOffset OcorridoEm, string? Texto,
        string? StatusAnterior, string? StatusNovo,
        string? ResponsavelAnterior, string? ResponsavelNovo);

    private sealed record InconsistenciaItem(
        Guid Id, Guid IdFolha, string Competencia, string Codigo, string Regra,
        int VersaoRegra, string Categoria, string Severidade, string Status, bool Pendente,
        List<string> ProximosStatus, Guid? IdResponsavel, string? Responsavel,
        string? Justificativa, DateTimeOffset? ConcluidaEm, Guid? IdFolhaFuncionario,
        string? Matricula, string? NomeFuncionario, string Descricao,
        decimal? ValorEsperado, decimal? ValorEncontrado, decimal? Diferenca,
        List<AndamentoItem>? Andamentos);

    private sealed record PaginaInconsistencias(int Total, int Pagina, List<InconsistenciaItem> Itens);

    private sealed record EventoItem(
        Guid Id, string Acao, string Entidade, Guid IdEntidade, string? Usuario,
        string Descricao, string? Contexto, DateTimeOffset OcorridoEm);

    private sealed record PaginaEventos(int Total, int Pagina, List<EventoItem> Itens);

    private sealed record ContagemItem(string Rotulo, int Quantidade);

    private sealed record PendenciaItem(Guid? IdResponsavel, string Responsavel, int Quantidade);

    private sealed record EvolucaoItem(
        string Competencia, int Folhas, int Inconsistencias, int Resolvidas);

    private sealed record PainelItem(
        int FolhasCalculadas, int FolhasFechadas, int InconsistenciasTotais,
        int InconsistenciasPendentes, int InconsistenciasResolvidas,
        decimal? PercentualConformidade, List<ContagemItem> PorSeveridade,
        List<ContagemItem> PorStatus, List<ContagemItem> PorRegra,
        List<PendenciaItem> PorResponsavel, List<EvolucaoItem> Evolucao);

    private sealed record ExecucaoItem(Guid Id, int TotalResultados, List<ResultadoBruto>? Resultados);

    private sealed record ResultadoBruto(Guid Id, string Codigo, string Matricula);

    private sealed record FolhaResumo(Guid Id, string Competencia, string Situacao, int VersaoCalculo);

    private sealed record FolhaEnvelope(FolhaResumo Folha);

    private static int _sufixo;

    /// <summary>Faixa de CPF propria: 50.000 para cima.</summary>
    private static int Semente() => 50_000 + (Interlocked.Increment(ref _sufixo) * 10);

    private Task<HttpClient> AdminAsync() =>
        _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminI);

    private PrismaRhDbContext Contexto(IServiceScope escopo) =>
        escopo.ServiceProvider.GetRequiredService<PrismaRhDbContext>();

    // ------------------------------------------------------------- construcao

    private static async Task GarantirRubricaSalarioAsync(HttpClient admin)
    {
        using var resposta = await admin.PostAsJsonAsync("/api/rubricas", new
        {
            codigo = "SAL",
            nome = "Salario base",
            tipo = "Provento",
            estrategia = "SalarioBaseProporcional",
        });

        Assert.True(resposta.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Produz uma inconsistencia de verdade e devolve o id dela.
    ///
    /// Usa o mesmo cenario do motor de analises: calcula a folha com a pessoa
    /// ativa e cadastra o desligamento depois, sem recalcular. E a ordem da vida
    /// real, e a unica que produz "desligado presente na folha mensal".
    /// </summary>
    private async Task<(Guid IdInconsistencia, Guid IdFolha, string Matricula)> InconsistenciaAsync(
        HttpClient admin, string competencia, string desligamento)
    {
        var semente = Semente();
        var sufixo = semente.ToString("D6");

        await GarantirRubricaSalarioAsync(admin);

        using var respostaCargo = await admin.PostAsJsonAsync("/api/cargos", new
        {
            codigo = $"W{sufixo}",
            nome = $"Cargo workflow {sufixo}",
        });
        respostaCargo.EnsureSuccessStatusCode();
        var cargo = (await respostaCargo.Content.ReadFromJsonAsync<Identificado>())!;

        using var respostaFuncionario = await admin.PostAsJsonAsync("/api/funcionarios", new
        {
            nome = $"Workflow Pessoa {sufixo}",
            cpf = BancoPostgresFixture.CpfDeTeste(semente),
            dataNascimento = "1990-05-20",
        });
        respostaFuncionario.EnsureSuccessStatusCode();
        var funcionario = (await respostaFuncionario.Content.ReadFromJsonAsync<Identificado>())!;

        using var respostaContrato = await admin.PostAsJsonAsync(
            $"/api/funcionarios/{funcionario.Id}/contratos",
            new
            {
                idEmpresa = banco.IdEmpresaI,
                matricula = $"W{sufixo}",
                dataAdmissao = "2030-01-01",
                salarioInicial = 3_000m,
                idCargo = cargo.Id,
                idEstabelecimento = banco.IdEstabelecimentoI,
                jornadaMensalHoras = 220,
            });
        respostaContrato.EnsureSuccessStatusCode();
        var contrato = (await respostaContrato.Content.ReadFromJsonAsync<Identificado>())!;

        using var abertura = await admin.PostAsJsonAsync(
            "/api/folhas", new { idEmpresa = banco.IdEmpresaI, competencia });
        abertura.EnsureSuccessStatusCode();
        var folha = (await abertura.Content.ReadFromJsonAsync<FolhaResumo>())!;

        using var calculo = await admin.PostAsync($"/api/folhas/{folha.Id}/calcular", null);
        calculo.EnsureSuccessStatusCode();

        using var desliga = await admin.PostAsJsonAsync(
            $"/api/contratos/{contrato.Id}/desligamento",
            new { dataDesligamento = desligamento, motivo = "PedidoDeDemissao" });
        desliga.EnsureSuccessStatusCode();

        using var analise = await admin.PostAsync($"/api/folhas/{folha.Id}/analisar", null);
        analise.EnsureSuccessStatusCode();
        var execucao = (await analise.Content.ReadFromJsonAsync<ExecucaoItem>())!;

        var achado = execucao.Resultados!.Single(
            r => r.Codigo == "DesligadoNaFolha" && r.Matricula == $"W{sufixo}");

        return (achado.Id, folha.Id, $"W{sufixo}");
    }

    private static async Task<InconsistenciaItem> TransitarAsync(
        HttpClient cliente, Guid id, string status, string? texto = null)
    {
        using var resposta = await cliente.PostAsJsonAsync(
            $"/api/inconsistencias/{id}/status", new { status, texto });

        resposta.EnsureSuccessStatusCode();

        return (await resposta.Content.ReadFromJsonAsync<InconsistenciaItem>())!;
    }

    // --------------------------------------------------------------- workflow

    [Fact]
    public async Task ONascimentoEDetectada_ComOProximoPassoDECLARADO()
    {
        var admin = await AdminAsync();
        var (id, _, matricula) = await InconsistenciaAsync(admin, "2050-03", "2050-01-10");

        var item = await admin.GetFromJsonAsync<InconsistenciaItem>($"/api/inconsistencias/{id}");

        Assert.NotNull(item);
        Assert.Equal("Detectada", item.Status);
        Assert.True(item.Pendente);
        Assert.Null(item.IdResponsavel);
        Assert.Equal(matricula, item.Matricula);

        // A resposta diz para onde da para ir. Sem isso, a tela teria que
        // duplicar a maquina de estados - e as duas copias divergiriam.
        Assert.Equal(["EmAnalise"], item.ProximosStatus);
        Assert.Empty(item.Andamentos!);
    }

    [Fact]
    public async Task OCaminhoCOMPLETODeixaAHISTORIAToda()
    {
        var admin = await AdminAsync();
        var (id, _, _) = await InconsistenciaAsync(admin, "2050-04", "2050-02-10");

        await TransitarAsync(admin, id, "EmAnalise");

        using var comentario = await admin.PostAsJsonAsync(
            $"/api/inconsistencias/{id}/comentarios",
            new { texto = "Conferi com o RH: o desligamento entrou depois do calculo." });
        comentario.EnsureSuccessStatusCode();

        using var evidencia = await admin.PostAsJsonAsync(
            $"/api/inconsistencias/{id}/evidencias",
            new { texto = "Termo de rescisao assinado, arquivo 2050/02." });
        evidencia.EnsureSuccessStatusCode();

        await TransitarAsync(admin, id, "Corrigida", "Folha recalculada.");
        var final = await TransitarAsync(admin, id, "Resolvida");

        Assert.Equal("Resolvida", final.Status);
        Assert.False(final.Pendente);
        Assert.NotNull(final.ConcluidaEm);

        var tipos = final.Andamentos!.Select(a => a.Tipo).ToList();

        // A linha do tempo inteira, na ordem. "Historico nao e apagado" e
        // criterio de aceite da fase.
        Assert.Equal(
            ["Transicao", "Comentario", "Evidencia", "Transicao", "Transicao"], tipos);

        Assert.Contains(
            final.Andamentos!,
            a => a.Texto == "Conferi com o RH: o desligamento entrou depois do calculo.");
    }

    /// <summary>
    /// A ameaca que o Security Gate nomeia: pular etapas para esconder pendencia.
    /// </summary>
    [Fact]
    public async Task PularDeDetectadaParaRESOLVIDA_ERECUSADO()
    {
        var admin = await AdminAsync();
        var (id, _, _) = await InconsistenciaAsync(admin, "2050-05", "2050-03-10");

        using var resposta = await admin.PostAsJsonAsync(
            $"/api/inconsistencias/{id}/status", new { status = "Resolvida", texto = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Contains(
            "EmAnalise", await resposta.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var item = await admin.GetFromJsonAsync<InconsistenciaItem>($"/api/inconsistencias/{id}");

        Assert.Equal("Detectada", item!.Status);
        Assert.Empty(item.Andamentos!);
    }

    [Fact]
    public async Task JustificarSemMOTIVO_ERECUSADO()
    {
        var admin = await AdminAsync();
        var (id, _, _) = await InconsistenciaAsync(admin, "2050-06", "2050-04-10");

        await TransitarAsync(admin, id, "EmAnalise");

        using var resposta = await admin.PostAsJsonAsync(
            $"/api/inconsistencias/{id}/status", new { status = "Justificada", texto = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Contains(
            "motivo", await resposta.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReabrirVoltaParaAnalise_ESemPerderAJustificativa()
    {
        var admin = await AdminAsync();
        var (id, _, _) = await InconsistenciaAsync(admin, "2050-07", "2050-05-10");

        await TransitarAsync(admin, id, "EmAnalise");
        await TransitarAsync(admin, id, "Justificada", "Acerto combinado em ata.");
        await TransitarAsync(admin, id, "Resolvida");

        var reaberta = await TransitarAsync(admin, id, "EmAnalise", "Nao convenceu.");

        Assert.Equal("EmAnalise", reaberta.Status);
        Assert.Null(reaberta.ConcluidaEm);

        // A justificativa e parte do historico: apaga-la esconderia o que se
        // concluiu antes de a conclusao ser derrubada.
        Assert.Equal("Acerto combinado em ata.", reaberta.Justificativa);
    }

    /// <summary>
    /// **Stored XSS** - a primeira fase em que um usuario escreve texto que
    /// outro vai ler.
    ///
    /// O backend guarda LITERALMENTE o que recebeu, sem interpretar e sem
    /// reescrever. Quem escapa e o React, por padrao, e a tela nunca usa
    /// `dangerouslySetInnerHTML` (`CLAUDE.md secao 24.9`).
    /// </summary>
    [Fact]
    public async Task ComentarioComSCRIPTEGuardadoComoTEXTO()
    {
        var admin = await AdminAsync();
        var (id, _, _) = await InconsistenciaAsync(admin, "2050-08", "2050-06-10");

        const string malicioso = "<script>alert('xss')</script><img src=x onerror=alert(1)>";

        using var resposta = await admin.PostAsJsonAsync(
            $"/api/inconsistencias/{id}/comentarios", new { texto = malicioso });

        resposta.EnsureSuccessStatusCode();

        using var escopo = _fabrica.Services.CreateScope();

        var gravado = await Contexto(escopo).AndamentosInconsistencia
            .IgnoreQueryFilters()
            .Where(a => a.IdResultadoAnalise == id)
            .Select(a => a.Texto)
            .SingleAsync();

        // Igual ao que entrou: nem escapado, nem removido, nem reescrito. O
        // backend nao e o lugar de decidir como aquilo sera renderizado.
        Assert.Equal(malicioso, gravado);
    }

    // ------------------------------------------------------------ responsavel

    [Fact]
    public async Task AtribuirRegistraOANTESEODEPOIS()
    {
        var admin = await AdminAsync();
        var (id, _, _) = await InconsistenciaAsync(admin, "2050-09", "2050-07-10");

        var analista = await UsuarioAsync(BancoPostgresFixture.EmailAnalistaI);

        using var resposta = await admin.PostAsJsonAsync(
            $"/api/inconsistencias/{id}/responsavel", new { idResponsavel = analista });

        resposta.EnsureSuccessStatusCode();
        var item = (await resposta.Content.ReadFromJsonAsync<InconsistenciaItem>())!;

        Assert.Equal(analista, item.IdResponsavel);
        Assert.Equal("Analista I", item.Responsavel);

        var linha = Assert.Single(item.Andamentos!, a => a.Tipo == "Atribuicao");

        Assert.Null(linha.ResponsavelAnterior);
        Assert.Equal("Analista I", linha.ResponsavelNovo);
    }

    /// <summary>
    /// A ameaca que o Security Gate nomeia: "atribuicao de responsavel a
    /// usuario de outra organizacao".
    /// </summary>
    [Fact]
    public async Task ResponsavelDEOUTRAORGANIZACAO_ERECUSADO()
    {
        var admin = await AdminAsync();
        var (id, _, _) = await InconsistenciaAsync(admin, "2050-10", "2050-08-10");

        var deFora = await UsuarioAsync(BancoPostgresFixture.EmailAdminA);

        using var resposta = await admin.PostAsJsonAsync(
            $"/api/inconsistencias/{id}/responsavel", new { idResponsavel = deFora });

        // A defesa e o filtro global: o id de fora simplesmente nao e
        // encontrado. Nao ha comparacao de IdOrganizacao escrita a mao que
        // alguem possa esquecer.
        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);

        var item = await admin.GetFromJsonAsync<InconsistenciaItem>($"/api/inconsistencias/{id}");

        Assert.Null(item!.IdResponsavel);
    }

    // ------------------------------------------------------------ permissoes

    [Fact]
    public async Task Auditor_LE_masNAOTRATA()
    {
        var admin = await AdminAsync();
        var auditor = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAuditorI);

        var (id, _, _) = await InconsistenciaAsync(admin, "2050-11", "2050-09-10");

        using var leitura = await auditor.GetAsync($"/api/inconsistencias/{id}");
        using var lista = await auditor.GetAsync("/api/inconsistencias");
        using var auditoria = await auditor.GetAsync("/api/auditoria");
        using var painel = await auditor.GetAsync("/api/painel");

        Assert.Equal(HttpStatusCode.OK, leitura.StatusCode);
        Assert.Equal(HttpStatusCode.OK, lista.StatusCode);
        Assert.Equal(HttpStatusCode.OK, auditoria.StatusCode);
        Assert.Equal(HttpStatusCode.OK, painel.StatusCode);

        using var transicao = await auditor.PostAsJsonAsync(
            $"/api/inconsistencias/{id}/status", new { status = "EmAnalise", texto = (string?)null });

        using var comentario = await auditor.PostAsJsonAsync(
            $"/api/inconsistencias/{id}/comentarios", new { texto = "nao deveria passar" });

        // "Auditor le tudo e NAO altera dado operacional" - Security Gate,
        // item 6.
        Assert.Equal(HttpStatusCode.Forbidden, transicao.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, comentario.StatusCode);
    }

    [Fact]
    public async Task Visualizador_TambemNAOTRATA()
    {
        var admin = await AdminAsync();
        var visualizador = await _fabrica.ClienteComoAsync(
            BancoPostgresFixture.EmailVisualizadorA);

        var (id, _, _) = await InconsistenciaAsync(admin, "2050-12", "2050-10-10");

        using var resposta = await visualizador.PostAsJsonAsync(
            $"/api/inconsistencias/{id}/status", new { status = "EmAnalise", texto = (string?)null });

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    // ------------------------------------------------------------- isolamento

    [Fact]
    public async Task InconsistenciaDaVIZINHA_DEVOLVE404()
    {
        var admin = await AdminAsync();
        var (id, _, _) = await InconsistenciaAsync(admin, "2051-01", "2050-11-10");

        var vizinha = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);

        using var leitura = await vizinha.GetAsync($"/api/inconsistencias/{id}");
        using var escrita = await vizinha.PostAsJsonAsync(
            $"/api/inconsistencias/{id}/status", new { status = "EmAnalise", texto = (string?)null });

        Assert.Equal(HttpStatusCode.NotFound, leitura.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, escrita.StatusCode);
    }

    // -------------------------------------------------------------- auditoria

    /// <summary>
    /// ⚠️ Resolve a pendencia do `CLAUDE.md secao 24.19 item 6`, aberta na
    /// Fase 4G: o Valor Base do FGTS rescisorio multiplica dinheiro e era
    /// sobrescrito sem deixar rastro de quem, de quando, nem do valor anterior.
    /// </summary>
    [Fact]
    public async Task AlterarOValorBaseDoFGTS_REGISTRAOANTESEODEPOIS()
    {
        var admin = await AdminAsync();
        var semente = Semente();
        var sufixo = semente.ToString("D6");

        using var respostaCargo = await admin.PostAsJsonAsync("/api/cargos", new
        {
            codigo = $"G{sufixo}",
            nome = $"Cargo fgts {sufixo}",
        });
        respostaCargo.EnsureSuccessStatusCode();
        var cargo = (await respostaCargo.Content.ReadFromJsonAsync<Identificado>())!;

        using var respostaFuncionario = await admin.PostAsJsonAsync("/api/funcionarios", new
        {
            nome = $"Fgts Pessoa {sufixo}",
            cpf = BancoPostgresFixture.CpfDeTeste(semente),
            dataNascimento = "1990-05-20",
        });
        respostaFuncionario.EnsureSuccessStatusCode();
        var funcionario = (await respostaFuncionario.Content.ReadFromJsonAsync<Identificado>())!;

        using var respostaContrato = await admin.PostAsJsonAsync(
            $"/api/funcionarios/{funcionario.Id}/contratos",
            new
            {
                idEmpresa = banco.IdEmpresaI,
                matricula = $"G{sufixo}",
                dataAdmissao = "2030-01-01",
                salarioInicial = 3_000m,
                idCargo = cargo.Id,
                idEstabelecimento = banco.IdEstabelecimentoI,
                jornadaMensalHoras = 220,
            });
        respostaContrato.EnsureSuccessStatusCode();
        var contrato = (await respostaContrato.Content.ReadFromJsonAsync<Identificado>())!;

        using var desliga = await admin.PostAsJsonAsync(
            $"/api/contratos/{contrato.Id}/desligamento",
            new { dataDesligamento = "2052-01-15", motivo = "DispensaSemJustaCausa" });
        desliga.EnsureSuccessStatusCode();

        using var primeiro = await admin.PutAsJsonAsync(
            $"/api/contratos/{contrato.Id}/rescisao/valor-base-fgts",
            new { valor = 10_000m, observacao = "Extrato de janeiro." });
        primeiro.EnsureSuccessStatusCode();

        using var correcao = await admin.PutAsJsonAsync(
            $"/api/contratos/{contrato.Id}/rescisao/valor-base-fgts",
            new { valor = 12_500m, observacao = "Extrato corrigido." });
        correcao.EnsureSuccessStatusCode();

        var eventos = await admin.GetFromJsonAsync<PaginaEventos>(
            "/api/auditoria?acao=ValorBaseFgtsInformado");

        Assert.NotNull(eventos);

        var doContrato = eventos.Itens
            .Where(e => e.Contexto!.Contains(contrato.Id.ToString(), StringComparison.Ordinal))
            .OrderBy(e => e.OcorridoEm)
            .ToList();

        // DUAS linhas, e nao uma sobrescrita: a alteracao e um fato.
        Assert.Equal(2, doContrato.Count);
        Assert.Contains("10.000,00", doContrato[0].Descricao, StringComparison.Ordinal);
        Assert.Contains(
            "de 10.000,00 para 12.500,00", doContrato[1].Descricao, StringComparison.Ordinal);
        Assert.Contains("anterior=10000.00", doContrato[1].Contexto!, StringComparison.Ordinal);
        Assert.Equal("Admin I", doContrato[1].Usuario);
    }

    /// <summary>
    /// ⚠️ Resolve a pendencia do `CLAUDE.md secao 24.19 item 7`, aberta na
    /// Fase 6: afrouxar uma tolerancia era indistinguivel de "sempre foi assim".
    /// </summary>
    [Fact]
    public async Task AFROUXARUmaRegistraOSPARAMETROS()
    {
        var admin = await AdminAsync();

        using var resposta = await admin.PutAsJsonAsync("/api/regras-analise/VariacaoSalarial", new
        {
            ativa = true,
            severidade = "Baixa",
            parametros = new Dictionary<string, string> { ["percentualTolerancia"] = "95" },
        });
        resposta.EnsureSuccessStatusCode();

        try
        {
            var eventos = await admin.GetFromJsonAsync<PaginaEventos>(
                "/api/auditoria?acao=RegraAnaliseConfigurada");

            var evento = eventos!.Itens
                .First(e => e.Contexto!.Contains("VariacaoSalarial", StringComparison.Ordinal));

            Assert.Contains("severidade=Baixa", evento.Contexto!, StringComparison.Ordinal);
            Assert.Contains("percentualTolerancia=95", evento.Contexto!, StringComparison.Ordinal);
            Assert.Equal("Admin I", evento.Usuario);
        }
        finally
        {
            using var volta = await admin.PutAsJsonAsync("/api/regras-analise/VariacaoSalarial", new
            {
                ativa = true,
                severidade = "Media",
                parametros = new Dictionary<string, string> { ["percentualTolerancia"] = "30" },
            });
            volta.EnsureSuccessStatusCode();
        }
    }

    /// <summary>
    /// ⚠️ O teste central da fase: **ninguem edita a auditoria**.
    ///
    /// Percorre os verbos de escrita contra as rotas de auditoria com o perfil
    /// mais alto que existe. Nenhum pode passar - nem Administrador da
    /// Plataforma (`CLAUDE.md secao 24.17`).
    /// </summary>
    [Fact]
    public async Task NINGUEMAlteraAAuditoria_NEMOAdministradorDaPlataforma()
    {
        var plataforma = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailPlataformaA);
        var admin = await AdminAsync();

        var eventos = await admin.GetFromJsonAsync<PaginaEventos>("/api/auditoria");
        var alvo = eventos!.Itens.FirstOrDefault()?.Id ?? Guid.NewGuid();

        var rotas = new[] { "/api/auditoria", $"/api/auditoria/{alvo}" };

        foreach (var rota in rotas)
        {
            using var criar = await plataforma.PostAsJsonAsync(rota, new { descricao = "inventado" });
            using var alterar = await plataforma.PutAsJsonAsync(rota, new { descricao = "alterado" });
            using var remendar = await plataforma.PatchAsJsonAsync(rota, new { descricao = "remendado" });
            using var apagar = await plataforma.DeleteAsync(rota);

            foreach (var resposta in new[] { criar, alterar, remendar, apagar })
            {
                // 404 ou 405: a rota nao existe para aquele verbo. O que NAO
                // pode acontecer e um 2xx - seria o caminho para reescrever a
                // trilha.
                Assert.True(
                    resposta.StatusCode is HttpStatusCode.NotFound
                        or HttpStatusCode.MethodNotAllowed,
                    $"{resposta.RequestMessage!.Method} {rota} devolveu {(int)resposta.StatusCode}");
            }
        }
    }

    [Fact]
    public async Task ATRANSICAOEUmEventoAUDITADO()
    {
        var admin = await AdminAsync();
        var (id, _, _) = await InconsistenciaAsync(admin, "2051-02", "2050-12-10");

        await TransitarAsync(admin, id, "EmAnalise");

        var eventos = await admin.GetFromJsonAsync<PaginaEventos>(
            $"/api/auditoria/ResultadoAnalise/{id}");

        var evento = Assert.Single(eventos!.Itens);

        Assert.Equal("InconsistenciaTransitada", evento.Acao);
        Assert.Contains("de=Detectada;para=EmAnalise", evento.Contexto!, StringComparison.Ordinal);
        Assert.Equal("Admin I", evento.Usuario);
    }

    /// <summary>
    /// A auditoria de um comentario registra QUE houve comentario, e nao o
    /// texto dele.
    ///
    /// O texto e o dado mais delicado do produto - justificativa de divergencia
    /// salarial costuma explicar situacao pessoal. Ele vive na linha do tempo,
    /// com o controle de acesso dela, e nao numa segunda tabela
    /// (`CLAUDE.md secao 24.13`).
    /// </summary>
    [Fact]
    public async Task AAuditoriaNAOCOPIAOTextoDoComentario()
    {
        var admin = await AdminAsync();
        var (id, _, _) = await InconsistenciaAsync(admin, "2051-03", "2051-01-10");

        const string sigiloso = "Afastamento por questao de saude da familia.";

        using var comentario = await admin.PostAsJsonAsync(
            $"/api/inconsistencias/{id}/comentarios", new { texto = sigiloso });
        comentario.EnsureSuccessStatusCode();

        var eventos = await admin.GetFromJsonAsync<PaginaEventos>(
            $"/api/auditoria/ResultadoAnalise/{id}");

        var evento = Assert.Single(eventos!.Itens);

        Assert.Equal("InconsistenciaComentada", evento.Acao);
        Assert.DoesNotContain(sigiloso, evento.Descricao, StringComparison.Ordinal);
        Assert.DoesNotContain(sigiloso, evento.Contexto ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AAuditoriaDaVIZINHANaoAparece()
    {
        var admin = await AdminAsync();
        var (id, _, _) = await InconsistenciaAsync(admin, "2051-04", "2051-02-10");

        await TransitarAsync(admin, id, "EmAnalise");

        var vizinha = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);

        var eventos = await vizinha.GetFromJsonAsync<PaginaEventos>(
            $"/api/auditoria/ResultadoAnalise/{id}");

        // O evento existe, e nao e da organizacao dela. O filtro global nao
        // deixa passar.
        Assert.Empty(eventos!.Itens);
    }

    [Fact]
    public async Task AAuditoriaTEMTETONaPaginacao()
    {
        var admin = await AdminAsync();

        var pagina = await admin.GetFromJsonAsync<PaginaEventos>("/api/auditoria?tamanho=99999");

        Assert.NotNull(pagina);
        Assert.True(pagina.Itens.Count <= 200);
    }

    // ------------------------------------------------------------------ painel

    [Fact]
    public async Task OPainelUsaDADOSREAIS()
    {
        var admin = await AdminAsync();
        var (id, _, _) = await InconsistenciaAsync(admin, "2051-05", "2051-03-10");

        var antes = await admin.GetFromJsonAsync<PainelItem>(
            $"/api/painel?idEmpresa={banco.IdEmpresaI}");

        Assert.NotNull(antes);
        Assert.True(antes.FolhasCalculadas > 0);
        Assert.True(antes.InconsistenciasPendentes > 0);

        // "Regras com maior incidencia" traz o NOME da regra, e nao o codigo:
        // o painel e lido por quem nao conhece o enum.
        Assert.Contains(antes.PorRegra, r => r.Rotulo.Contains("Desligado", StringComparison.Ordinal));

        await TransitarAsync(admin, id, "EmAnalise");
        await TransitarAsync(admin, id, "Corrigida", "Recalculada.");
        await TransitarAsync(admin, id, "Resolvida");

        var depois = await admin.GetFromJsonAsync<PainelItem>(
            $"/api/painel?idEmpresa={banco.IdEmpresaI}");

        // O numero se move porque o trabalho aconteceu - nao ha valor semeado.
        Assert.Equal(antes.InconsistenciasResolvidas + 1, depois!.InconsistenciasResolvidas);
        Assert.Equal(antes.InconsistenciasPendentes - 1, depois.InconsistenciasPendentes);
        Assert.NotNull(depois.PercentualConformidade);
        Assert.NotEmpty(depois.Evolucao);
    }

    [Fact]
    public async Task OPainelDaVIZINHANaoContaOQueENOSSO()
    {
        var admin = await AdminAsync();
        await InconsistenciaAsync(admin, "2051-06", "2051-04-10");

        var meu = await admin.GetFromJsonAsync<PainelItem>("/api/painel");
        var vizinha = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminC);
        var dela = await vizinha.GetFromJsonAsync<PainelItem>("/api/painel");

        Assert.True(meu!.InconsistenciasTotais > 0);

        // A organizacao C nao roda analise em teste nenhum: o painel dela nao
        // pode contar o que e da I.
        Assert.Equal(0, dela!.InconsistenciasTotais);
    }

    // ------------------------------------------------------------------ filtros

    [Fact]
    public async Task AListagemFILTRAPorStatusEPorResponsavel()
    {
        var admin = await AdminAsync();
        var (id, idFolha, matricula) = await InconsistenciaAsync(admin, "2051-07", "2051-05-10");

        await TransitarAsync(admin, id, "EmAnalise");

        var analista = await UsuarioAsync(BancoPostgresFixture.EmailAnalistaI);

        using var atribuicao = await admin.PostAsJsonAsync(
            $"/api/inconsistencias/{id}/responsavel", new { idResponsavel = analista });
        atribuicao.EnsureSuccessStatusCode();

        var porStatus = await admin.GetFromJsonAsync<PaginaInconsistencias>(
            $"/api/inconsistencias?status=EmAnalise&idFolha={idFolha}");

        Assert.Contains(porStatus!.Itens, i => i.Matricula == matricula);

        var porResponsavel = await admin.GetFromJsonAsync<PaginaInconsistencias>(
            $"/api/inconsistencias?idResponsavel={analista}");

        Assert.Contains(porResponsavel!.Itens, i => i.Id == id);

        var inexistente = await admin.GetFromJsonAsync<PaginaInconsistencias>(
            $"/api/inconsistencias?status=Resolvida&idFolha={idFolha}");

        Assert.DoesNotContain(inexistente!.Itens, i => i.Id == id);
    }

    [Fact]
    public async Task FiltroDESCONHECIDO_ERECUSADO()
    {
        var admin = await AdminAsync();

        using var resposta = await admin.GetAsync("/api/inconsistencias?status=Sumida");

        // Recusado, e nao ignorado. Ignorar devolveria a lista inteira para
        // quem pediu um filtro - e a pessoa acharia que aquilo era o resultado.
        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task AListagemTEMTETO()
    {
        var admin = await AdminAsync();

        var pagina = await admin.GetFromJsonAsync<PaginaInconsistencias>(
            "/api/inconsistencias?tamanho=99999");

        Assert.NotNull(pagina);
        Assert.True(pagina.Itens.Count <= 100);
    }

    // ------------------------------------------------------------------ apoio

    private async Task<Guid> UsuarioAsync(string email)
    {
        using var escopo = _fabrica.Services.CreateScope();

        return await Contexto(escopo).Usuarios
            .IgnoreQueryFilters()
            .Where(u => u.Email == email)
            .Select(u => u.Id)
            .SingleAsync();
    }
}
