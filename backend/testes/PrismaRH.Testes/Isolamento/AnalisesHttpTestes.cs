using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Testes.Isolamento;

/// <summary>
/// Motor de analises pelas rotas HTTP (Fase 6), contra PostgreSQL real.
///
/// Os testes de dominio provam a logica das seis regras. Estes provam o que so
/// o sistema inteiro responde: **quem pode configurar, quem pode executar, quem
/// pode ler - e se alguem consegue enxergar a folha do vizinho.**
///
/// Organizacao I, exclusiva desta suite: as regras conferem a folha INTEIRA da
/// empresa, entao numa organizacao compartilhada a regra de ausencia acusaria
/// todo contrato criado pelas outras suites.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class AnalisesHttpTestes(BancoPostgresFixture banco) : IDisposable
{
    private readonly FabricaApiIsolada _fabrica = new(banco.StringConexao);

    public void Dispose() => _fabrica.Dispose();

    private const string Regras = "/api/regras-analise";

    private sealed record Identificado(Guid Id);

    private sealed record ParametroItem(
        string Chave, string Rotulo, string Tipo, string Padrao,
        string Minimo, string Maximo, string Valor);

    private sealed record RegraItem(
        string Codigo, string Nome, string Categoria, int Versao,
        bool Ativa, string Severidade, string SeveridadePadrao, bool Configurada,
        List<ParametroItem> Parametros);

    private sealed record ResultadoItem(
        Guid Id, string Codigo, string Regra, int VersaoRegra, string Categoria,
        string Severidade, Guid? IdFolhaFuncionario, string? Matricula,
        string? NomeFuncionario, string Descricao,
        decimal? ValorEsperado, decimal? ValorEncontrado, decimal? Diferenca, string? Contexto);

    private sealed record ExecucaoItem(
        Guid Id, Guid IdFolha, string Competencia, int VersaoCalculoDaFolha,
        DateTimeOffset ExecutadaEm, int RegrasExecutadas, int TotalResultados,
        int ResultadosAltos, int ResultadosMedios, int ResultadosBaixos,
        bool Desatualizada, List<ResultadoItem>? Resultados);

    private sealed record PaginaExecucoes(int Total, int Pagina, List<ExecucaoItem> Itens);

    private sealed record FolhaResumo(Guid Id, string Competencia, string Situacao, int VersaoCalculo);

    private static int _sufixo;

    /// <summary>Faixa de CPF propria: 30.000 para cima.</summary>
    private static int Semente() => 30_000 + (Interlocked.Increment(ref _sufixo) * 10);

    private Task<HttpClient> AdminAsync() =>
        _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminI);

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

        // 409 quando ja existe - e sucesso aqui: so pode haver uma ativa.
        Assert.True(
            resposta.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict,
            $"rubrica SAL: {resposta.StatusCode}");
    }

    /// <summary>
    /// Desliga um contrato ja existente.
    ///
    /// ⚠️ Separado de <see cref="ContratoAsync"/> de proposito, e a razao e o
    /// proprio cenario da regra: o motor de calculo **nao cria holerite mensal
    /// para quem ja saiu**. Desligar antes de calcular produz uma folha sem a
    /// pessoa - o comportamento correto, e nada para a regra conferir.
    ///
    /// O defeito que a regra procura acontece na outra ordem, que e a ordem da
    /// vida real: a folha e calculada com a pessoa ativa, o desligamento e
    /// cadastrado depois, e ninguem recalcula.
    /// </summary>
    private static async Task DesligarAsync(HttpClient admin, Guid idContrato, string data)
    {
        using var resposta = await admin.PostAsJsonAsync(
            $"/api/contratos/{idContrato}/desligamento",
            new { dataDesligamento = data, motivo = "PedidoDeDemissao" });

        resposta.EnsureSuccessStatusCode();
    }

    private async Task<Guid> ContratoAsync(
        HttpClient admin, int semente, decimal salario, string admissao)
    {
        var sufixo = semente.ToString("D6");

        using var respostaCargo = await admin.PostAsJsonAsync("/api/cargos", new
        {
            codigo = $"A{sufixo}",
            nome = $"Cargo analise {sufixo}",
        });
        respostaCargo.EnsureSuccessStatusCode();
        var cargo = (await respostaCargo.Content.ReadFromJsonAsync<Identificado>())!;

        using var respostaFuncionario = await admin.PostAsJsonAsync("/api/funcionarios", new
        {
            nome = $"Analise Pessoa {sufixo}",
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
                matricula = $"A{sufixo}",
                dataAdmissao = admissao,
                salarioInicial = salario,
                idCargo = cargo.Id,
                idEstabelecimento = banco.IdEstabelecimentoI,
                jornadaMensalHoras = 220,
            });
        respostaContrato.EnsureSuccessStatusCode();

        return (await respostaContrato.Content.ReadFromJsonAsync<Identificado>())!.Id;
    }

    private async Task<FolhaEnvelope> FolhaCalculadaAsync(HttpClient admin, string competencia)
    {
        using var abertura = await admin.PostAsJsonAsync(
            "/api/folhas", new { idEmpresa = banco.IdEmpresaI, competencia });

        abertura.EnsureSuccessStatusCode();
        var folha = (await abertura.Content.ReadFromJsonAsync<FolhaResumo>())!;

        using var calculo = await admin.PostAsync($"/api/folhas/{folha.Id}/calcular", null);
        calculo.EnsureSuccessStatusCode();

        return (await calculo.Content.ReadFromJsonAsync<FolhaEnvelope>())!;
    }

    private sealed record HoleriteResumo(Guid Id, string Matricula, decimal TotalProventos);

    private sealed record FolhaEnvelope(FolhaResumo Folha, List<HoleriteResumo> Funcionarios);

    /// <summary>
    /// Uma rubrica de desconto com valor informado, para lancar a mao.
    ///
    /// A organizacao I nao tem INSS cadastrado - de proposito, para que as
    /// folhas desta suite sejam simples. Sem nenhum desconto, a regra de
    /// percentual nunca teria o que conferir.
    /// </summary>
    private static async Task<Guid> RubricaDescontoAsync(HttpClient admin)
    {
        using var criacao = await admin.PostAsJsonAsync("/api/rubricas", new
        {
            codigo = "VT",
            nome = "Vale transporte",
            tipo = "Desconto",
            estrategia = "ValorInformado",
        });

        if (criacao.StatusCode == HttpStatusCode.Created)
        {
            return (await criacao.Content.ReadFromJsonAsync<Identificado>())!.Id;
        }

        var existentes = await admin.PaginaDe<RubricaResumo>("/api/rubricas");

        return existentes!.Single(r => r.Codigo == "VT" && r.Ativa).Id;
    }

    private sealed record RubricaResumo(Guid Id, string Codigo, bool Ativa);

    private static async Task<ExecucaoItem> AnalisarAsync(HttpClient cliente, Guid idFolha)
    {
        using var resposta = await cliente.PostAsync($"/api/folhas/{idFolha}/analisar", null);
        resposta.EnsureSuccessStatusCode();

        return (await resposta.Content.ReadFromJsonAsync<ExecucaoItem>())!;
    }

    // ------------------------------------------------------------ o catalogo

    [Fact]
    public async Task OCatalogoVemInteiro_MesmoSemNadaConfigurado()
    {
        var admin = await AdminAsync();

        var catalogo = await admin.GetFromJsonAsync<List<RegraItem>>(Regras);

        Assert.NotNull(catalogo);

        // As seis regras, sempre. Regra sem linha no banco roda ativa no
        // padrao - esconde-la faria a pessoa achar que ela nao existe.
        Assert.Equal(6, catalogo.Count);
        Assert.All(catalogo, r => Assert.True(r.Versao >= 1));

        // AusenteDaFolha nao e configurada por teste nenhum desta suite, entao
        // afirmar sobre ela nao depende da ordem de execucao.
        var ausente = catalogo.Single(r => r.Codigo == "AusenteDaFolha");

        Assert.True(ausente.Ativa);
        Assert.False(ausente.Configurada);
        Assert.Equal("Alta", ausente.Severidade);
        Assert.Empty(ausente.Parametros);

        // A declaracao do parametro vem do CODIGO, e nao da configuracao -
        // entao ela vale mesmo que outro teste tenha mexido no valor.
        var parametro = Assert.Single(
            catalogo.Single(r => r.Codigo == "DescontoAcimaDoLimite").Parametros);

        Assert.Equal("percentualMaximo", parametro.Chave);
        Assert.Equal("70", parametro.Padrao);
        Assert.Equal("1", parametro.Minimo);
        Assert.Equal("100", parametro.Maximo);
    }

    // ---------------------------------------------- tres niveis de permissao

    [Fact]
    public async Task Analista_LE_e_EXECUTA_masNAOCONFIGURA()
    {
        var admin = await AdminAsync();
        var analista = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAnalistaI);

        await GarantirRubricaSalarioAsync(admin);
        var envelope = await FolhaCalculadaAsync(admin, "2031-01");

        using var leitura = await analista.GetAsync(Regras);
        using var execucao = await analista.PostAsync($"/api/folhas/{envelope.Folha.Id}/analisar", null);
        using var configuracao = await analista.PutAsJsonAsync(
            $"{Regras}/LiquidoNegativo", new { ativa = true, severidade = "Baixa" });

        Assert.Equal(HttpStatusCode.OK, leitura.StatusCode);
        Assert.Equal(HttpStatusCode.OK, execucao.StatusCode);

        // Afrouxar tolerancia e o jeito mais barato de fazer uma divergencia
        // sumir do relatorio. Quem roda a analise no dia a dia nao configura.
        Assert.Equal(HttpStatusCode.Forbidden, configuracao.StatusCode);
    }

    [Fact]
    public async Task Auditor_SOLE()
    {
        var admin = await AdminAsync();
        var auditor = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAuditorI);

        await GarantirRubricaSalarioAsync(admin);
        var envelope = await FolhaCalculadaAsync(admin, "2031-02");

        using var leitura = await auditor.GetAsync(Regras);
        using var execucao = await auditor.PostAsync($"/api/folhas/{envelope.Folha.Id}/analisar", null);
        using var configuracao = await auditor.PutAsJsonAsync(
            $"{Regras}/LiquidoNegativo", new { ativa = false, severidade = "Baixa" });

        Assert.Equal(HttpStatusCode.OK, leitura.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, execucao.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, configuracao.StatusCode);
    }

    [Fact]
    public async Task Administrador_CONFIGURA()
    {
        var admin = await AdminAsync();

        using var resposta = await admin.PutAsJsonAsync($"{Regras}/RubricaDuplicada", new
        {
            ativa = false,
            severidade = "Baixa",
        });

        resposta.EnsureSuccessStatusCode();
        var regra = (await resposta.Content.ReadFromJsonAsync<RegraItem>())!;

        Assert.False(regra.Ativa);
        Assert.Equal("Baixa", regra.Severidade);
        Assert.True(regra.Configurada);

        // Devolve para o padrao, para nao contaminar os outros testes.
        using var volta = await admin.PutAsJsonAsync($"{Regras}/RubricaDuplicada", new
        {
            ativa = true,
            severidade = "Media",
        });

        volta.EnsureSuccessStatusCode();
    }

    // ---------------------------------------------------------- parametrizacao

    [Fact]
    public async Task ParametroFORADAFAIXA_ERECUSADOComAFaixaNaMensagem()
    {
        var admin = await AdminAsync();

        using var resposta = await admin.PutAsJsonAsync($"{Regras}/DescontoAcimaDoLimite", new
        {
            ativa = true,
            severidade = "Media",
            parametros = new Dictionary<string, string> { ["percentualMaximo"] = "150" },
        });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);

        var corpo = await resposta.Content.ReadAsStringAsync();

        Assert.Contains("entre 1 e 100", corpo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ParametroQueARegraNAODECLARA_ERECUSADO()
    {
        var admin = await AdminAsync();

        using var resposta = await admin.PutAsJsonAsync($"{Regras}/DescontoAcimaDoLimite", new
        {
            ativa = true,
            severidade = "Media",
            parametros = new Dictionary<string, string> { ["toleranciaInventada"] = "10" },
        });

        // Recusado, e nao ignorado em silencio: ignorar faria a pessoa
        // configurar, ver a tela salvar, e nunca entender por que nada mudou.
        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Contains(
            "nao tem o parametro", await resposta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ParametroQueNAOENUMERO_ERECUSADO()
    {
        var admin = await AdminAsync();

        using var resposta = await admin.PutAsJsonAsync($"{Regras}/DescontoAcimaDoLimite", new
        {
            ativa = true,
            severidade = "Media",
            parametros = new Dictionary<string, string> { ["percentualMaximo"] = "select 1" },
        });

        // O parametro e um NUMERO. Nao ha caminho daqui para SQL nem para
        // expressao: o que nao converte e recusado antes de virar comportamento.
        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task RegraDESCONHECIDA_DEVOLVE404()
    {
        var admin = await AdminAsync();

        using var resposta = await admin.PutAsJsonAsync($"{Regras}/RegraQueAlguemInventou", new
        {
            ativa = true,
            severidade = "Alta",
        });

        // Vocabulario fechado: o texto da rota tem que casar com o enum.
        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task ParametroConfigurado_MUDAORESULTADODAANALISE()
    {
        var admin = await AdminAsync();
        var semente = Semente();

        await GarantirRubricaSalarioAsync(admin);
        await ContratoAsync(admin, semente, 3_000m, "2030-01-01");

        var idRubrica = await RubricaDescontoAsync(admin);
        var folha = await FolhaCalculadaAsync(admin, "2032-03");
        var holerite = folha.Funcionarios.Single(h => h.Matricula == $"A{semente:D6}");

        // Um desconto pequeno: 100 sobre 3.000 e 3,3%.
        using var lancamento = await admin.PostAsJsonAsync(
            $"/api/folhas/{folha.Folha.Id}/funcionarios/{holerite.Id}/lancamentos",
            new { idRubrica, valor = 100m, referencia = (string?)null });

        lancamento.EnsureSuccessStatusCode();

        // Com o padrao de 70%, 3,3% de desconto nao acusa nada.
        var antes = await AnalisarAsync(admin, folha.Folha.Id);

        Assert.DoesNotContain(
            antes.Resultados!, r => r.Codigo == "DescontoAcimaDoLimite");

        using var configuracao = await admin.PutAsJsonAsync($"{Regras}/DescontoAcimaDoLimite", new
        {
            ativa = true,
            severidade = "Media",
            parametros = new Dictionary<string, string> { ["percentualMaximo"] = "1" },
        });
        configuracao.EnsureSuccessStatusCode();

        try
        {
            var depois = await AnalisarAsync(admin, folha.Folha.Id);

            // Com 1%, os mesmos 3,3% passam do teto. O arquivo e o mesmo, a
            // folha e a mesma: o que mudou foi so o parametro.
            Assert.Contains(depois.Resultados!, r => r.Codigo == "DescontoAcimaDoLimite");
        }
        finally
        {
            using var volta = await admin.PutAsJsonAsync($"{Regras}/DescontoAcimaDoLimite", new
            {
                ativa = true,
                severidade = "Media",
                parametros = new Dictionary<string, string> { ["percentualMaximo"] = "70" },
            });

            volta.EnsureSuccessStatusCode();
        }
    }

    // --------------------------------------------------------------- execucao

    [Fact]
    public async Task FolhaEMRASCUNHO_NaoEhAnalisada()
    {
        var admin = await AdminAsync();

        using var abertura = await admin.PostAsJsonAsync(
            "/api/folhas", new { idEmpresa = banco.IdEmpresaI, competencia = "2033-05" });

        abertura.EnsureSuccessStatusCode();
        var folha = (await abertura.Content.ReadFromJsonAsync<FolhaResumo>())!;

        using var resposta = await admin.PostAsync($"/api/folhas/{folha.Id}/analisar", null);

        // Em rascunho nao ha holerite: analisar produziria "todo mundo
        // ausente" e nada mais - um relatorio inteiro de alarme falso.
        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Contains(
            "Calcule a folha", await resposta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DesligadoNaFolhaMensal_EACUSADOEGRAVADO()
    {
        var admin = await AdminAsync();
        var semente = Semente();

        await GarantirRubricaSalarioAsync(admin);
        var contrato = await ContratoAsync(admin, semente, 3_000m, "2030-01-01");

        // A ordem e o cenario: calcula com a pessoa ativa...
        var envelope = await FolhaCalculadaAsync(admin, "2034-07");

        // ...e so DEPOIS o desligamento e cadastrado, sem recalcular. E assim
        // que um desligado sobra numa folha mensal fechada.
        await DesligarAsync(admin, contrato, "2034-05-20");

        var execucao = await AnalisarAsync(admin, envelope.Folha.Id);

        var achado = Assert.Single(
            execucao.Resultados!,
            r => r.Codigo == "DesligadoNaFolha" && r.Matricula == $"A{semente:D6}");

        Assert.Equal("Alta", achado.Severidade);
        Assert.Equal(1, achado.VersaoRegra);
        Assert.NotNull(achado.IdFolhaFuncionario);

        // Gravado de verdade, e nao so devolvido.
        using var escopo = _fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PrismaRhDbContext>();

        var gravado = await db.ResultadosAnalise
            .IgnoreQueryFilters()
            .SingleAsync(r => r.Id == achado.Id);

        Assert.Equal(banco.IdOrganizacaoI, gravado.IdOrganizacao);
        Assert.Equal(execucao.Id, gravado.IdExecucaoAnalise);
    }

    [Fact]
    public async Task AnalisarDeNovo_CriaExecucaoNOVAEnaoSubstitui()
    {
        var admin = await AdminAsync();

        await GarantirRubricaSalarioAsync(admin);
        var envelope = await FolhaCalculadaAsync(admin, "2035-01");

        var primeira = await AnalisarAsync(admin, envelope.Folha.Id);
        var segunda = await AnalisarAsync(admin, envelope.Folha.Id);

        Assert.NotEqual(primeira.Id, segunda.Id);

        var historico = await admin.GetFromJsonAsync<PaginaExecucoes>(
            $"/api/folhas/{envelope.Folha.Id}/analises");

        // Comparar duas passadas e o que mostra se a correcao funcionou. O
        // roadmap pede historico de execucao, e este e ele.
        Assert.NotNull(historico);
        Assert.True(historico.Total >= 2);
    }

    /// <summary>
    /// Execucao reproduzivel - criterio de aceite da fase, agora pela rota.
    /// </summary>
    [Fact]
    public async Task DuasExecucoesSeguidas_ProduzemOMESMORELATORIO()
    {
        var admin = await AdminAsync();
        var semente = Semente();

        await GarantirRubricaSalarioAsync(admin);
        var contrato = await ContratoAsync(admin, semente, 3_000m, "2030-01-01");

        var envelope = await FolhaCalculadaAsync(admin, "2036-04");
        await DesligarAsync(admin, contrato, "2036-02-10");

        var primeira = await AnalisarAsync(admin, envelope.Folha.Id);
        var segunda = await AnalisarAsync(admin, envelope.Folha.Id);

        // Reproduzir um relatorio VAZIO nao prova nada: o teste precisa de um
        // achado para que a igualdade signifique alguma coisa.
        Assert.True(primeira.TotalResultados > 0);

        Assert.Equal(primeira.TotalResultados, segunda.TotalResultados);
        Assert.Equal(primeira.RegrasExecutadas, segunda.RegrasExecutadas);
        Assert.Equal(
            primeira.Resultados!.Select(r => (r.Codigo, r.Matricula, r.Descricao)),
            segunda.Resultados!.Select(r => (r.Codigo, r.Matricula, r.Descricao)));
    }

    [Fact]
    public async Task RecalcularAFolha_MarcaAAnaliseComoDESATUALIZADA()
    {
        var admin = await AdminAsync();

        await GarantirRubricaSalarioAsync(admin);
        var envelope = await FolhaCalculadaAsync(admin, "2037-06");

        var execucao = await AnalisarAsync(admin, envelope.Folha.Id);

        Assert.False(execucao.Desatualizada);

        using var recalculo = await admin.PostAsync($"/api/folhas/{envelope.Folha.Id}/calcular", null);
        recalculo.EnsureSuccessStatusCode();

        var relida = await admin.GetFromJsonAsync<ExecucaoItem>($"/api/analises/{execucao.Id}");

        // Dizer que envelheceu e melhor que apagar: apagar perderia o historico
        // que o roadmap pede.
        Assert.NotNull(relida);
        Assert.True(relida.Desatualizada);
    }

    [Fact]
    public async Task OsContadoresGravadosBatemComOsResultados()
    {
        var admin = await AdminAsync();
        var semente = Semente();

        await GarantirRubricaSalarioAsync(admin);
        var contrato = await ContratoAsync(admin, semente, 3_000m, "2030-01-01");

        var envelope = await FolhaCalculadaAsync(admin, "2038-03");
        await DesligarAsync(admin, contrato, "2038-01-15");
        var execucao = await AnalisarAsync(admin, envelope.Folha.Id);

        Assert.Equal(execucao.Resultados!.Count, execucao.TotalResultados);
        Assert.Equal(
            execucao.TotalResultados,
            execucao.ResultadosAltos + execucao.ResultadosMedios + execucao.ResultadosBaixos);
    }

    // ------------------------------------------------------------- isolamento

    [Fact]
    public async Task FolhaDaVIZINHA_DEVOLVE404NaAnalise()
    {
        var vizinha = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var admin = await AdminAsync();

        await GarantirRubricaSalarioAsync(admin);
        var envelope = await FolhaCalculadaAsync(admin, "2039-08");

        using var resposta = await vizinha.PostAsync($"/api/folhas/{envelope.Folha.Id}/analisar", null);

        // 404, e nao 403: um 403 confirmaria que aquela folha existe.
        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task ExecucaoDaVIZINHA_DEVOLVE404()
    {
        var admin = await AdminAsync();

        await GarantirRubricaSalarioAsync(admin);
        var envelope = await FolhaCalculadaAsync(admin, "2040-09");
        var execucao = await AnalisarAsync(admin, envelope.Folha.Id);

        var vizinha = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);

        using var resposta = await vizinha.GetAsync($"/api/analises/{execucao.Id}");

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    /// <summary>
    /// Uma regra da organizacao I nunca ve contrato da organizacao A.
    ///
    /// E o item 2 do Security Gate da Fase 6: a execucao roda sob o filtro
    /// global, e uma regra nao consegue enxergar fora da propria organizacao
    /// mesmo que sua configuracao pedisse.
    ///
    /// A regra de AUSENCIA e a prova mais forte disso: ela percorre os
    /// contratos da empresa procurando quem ficou de fora. Se o filtro
    /// falhasse, ela acusaria cada funcionario das outras organizacoes.
    /// </summary>
    [Fact]
    public async Task ARegraDeAUSENCIA_SoVEContratoDAPROPRIAEmpresa()
    {
        var admin = await AdminAsync();
        var semente = Semente();

        await GarantirRubricaSalarioAsync(admin);
        await ContratoAsync(admin, semente, 3_000m, "2030-01-01");

        var envelope = await FolhaCalculadaAsync(admin, "2041-10");
        var execucao = await AnalisarAsync(admin, envelope.Folha.Id);

        var ausentes = execucao.Resultados!.Where(r => r.Codigo == "AusenteDaFolha").ToList();

        // A organizacao A tem dezenas de contratos criados por outras suites.
        // Nenhum deles pode aparecer aqui.
        Assert.All(ausentes, r =>
            Assert.StartsWith("A", r.Matricula!, StringComparison.Ordinal));

        using var escopo = _fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PrismaRhDbContext>();

        var doVizinho = await db.ResultadosAnalise
            .IgnoreQueryFilters()
            .Where(r => r.IdExecucaoAnalise == execucao.Id
                        && r.IdOrganizacao != banco.IdOrganizacaoI)
            .CountAsync();

        Assert.Equal(0, doVizinho);
    }

    [Fact]
    public async Task ConfiguracaoDaVIZINHANaoAfetaEstaOrganizacao()
    {
        var admin = await AdminAsync();
        var vizinha = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);

        using var configuracao = await vizinha.PutAsJsonAsync($"{Regras}/LiquidoNegativo", new
        {
            ativa = false,
            severidade = "Baixa",
        });
        configuracao.EnsureSuccessStatusCode();

        var catalogo = await admin.GetFromJsonAsync<List<RegraItem>>(Regras);
        var minha = catalogo!.Single(r => r.Codigo == "LiquidoNegativo");

        // A vizinha desligou a dela. A minha continua ativa.
        Assert.True(minha.Ativa);
        Assert.False(minha.Configurada);
    }

    /// <summary>
    /// O corpo da requisicao nao carrega `codigo` nem `idOrganizacao` - e mandar
    /// os dois nao tem efeito.
    ///
    /// Protecao contra *overposting* (`CLAUDE.md secao 24.7`): o codigo vem da
    /// rota, a organizacao vem do usuario autenticado.
    /// </summary>
    [Fact]
    public async Task CodigoEIdOrganizacaoNoCORPO_NaoTemEfeito()
    {
        var admin = await AdminAsync();

        using var resposta = await admin.PutAsJsonAsync($"{Regras}/VariacaoSalarial", new
        {
            ativa = true,
            severidade = "Baixa",
            codigo = "LiquidoNegativo",
            idOrganizacao = Guid.NewGuid(),
            versao = 99,
        });

        resposta.EnsureSuccessStatusCode();
        var regra = (await resposta.Content.ReadFromJsonAsync<RegraItem>())!;

        // Gravou na regra da ROTA, e nao na do corpo. A versao continua a do
        // codigo, e nao a que o cliente mandou.
        Assert.Equal("VariacaoSalarial", regra.Codigo);
        Assert.Equal(1, regra.Versao);

        using var escopo = _fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PrismaRhDbContext>();

        var gravada = await db.RegrasAnalise
            .IgnoreQueryFilters()
            .SingleAsync(r => r.IdOrganizacao == banco.IdOrganizacaoI
                              && r.Codigo == PrismaRH.Dominio.Analises.CodigoRegra.VariacaoSalarial);

        Assert.Equal(banco.IdOrganizacaoI, gravada.IdOrganizacao);

        // O liquido negativo da organizacao I continua intocado.
        var catalogo = await admin.GetFromJsonAsync<List<RegraItem>>(Regras);

        Assert.True(catalogo!.Single(r => r.Codigo == "LiquidoNegativo").Ativa);

        using var volta = await admin.PutAsJsonAsync($"{Regras}/VariacaoSalarial", new
        {
            ativa = true,
            severidade = "Media",
        });
        volta.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task AListagemDeExecucoesTEMTETO()
    {
        var admin = await AdminAsync();

        await GarantirRubricaSalarioAsync(admin);
        var envelope = await FolhaCalculadaAsync(admin, "2042-11");
        await AnalisarAsync(admin, envelope.Folha.Id);

        var pagina = await admin.GetFromJsonAsync<PaginaExecucoes>(
            $"/api/folhas/{envelope.Folha.Id}/analises?tamanho=9999");

        Assert.NotNull(pagina);
        Assert.True(pagina.Itens.Count <= 100);
    }
}
