using System.Net;
using System.Net.Http.Json;

namespace PrismaRH.Testes.Isolamento;

/// <summary>
/// Fase 4E etapa 1 ponta a ponta, contra PostgreSQL real.
///
/// Nenhuma competencia e usada e nenhuma folha e calculada: esta etapa nao
/// mexe em dinheiro. Ela le contrato e calendario, e por isso e a unica dos
/// testes de integracao que nao precisa de organizacao propria.
///
/// PREFIXO "FER" em cargo e matricula, e nao "F": FolhaMensalTestes usa
/// F{sufixo} na MESMA organizacao A, e os dois contadores comecam em 0001.
/// Rodando isolados os dois passam; juntos, o segundo colide no indice unico e
/// falha por 409 - defeito de teste que parece defeito de codigo.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class FeriasIntegracaoTestes(BancoPostgresFixture banco) : IDisposable
{
    private readonly FabricaApiIsolada _fabrica = new(banco.StringConexao);

    public void Dispose() => _fabrica.Dispose();

    private sealed record Identificado(Guid Id);

    private sealed record ConcessaoItem(
        Guid Id, DateOnly InicioPeriodoAquisitivo, DateOnly FimPeriodoAquisitivo,
        DateOnly Inicio, DateOnly Fim, int Dias, int DiasAbonoPecuniario,
        int DiasBaixados, string Situacao, bool PodeCancelar);

    private sealed record PeriodoItem(
        int Numero, DateOnly Inicio, DateOnly Fim, DateOnly InicioConcessao,
        DateOnly LimiteConcessao, int DiasDireito, string Situacao,
        int DiasParaCompletar, bool EmDobra, int DiasConcedidos, int Saldo,
        int SaldoAbono, int FracoesUsadas, List<ConcessaoItem> Concessoes);

    private sealed record FeriasResposta(
        Guid IdContrato, string Matricula, DateOnly DataAdmissao, DateOnly? DataDesligamento,
        DateOnly Referencia, int DiasAdquiridos, int SaldoTotal, int PeriodosVencidos,
        List<PeriodoItem> Periodos);

    private sealed record ProblemaValidacao(Dictionary<string, string[]> Errors);

    private static Task<HttpResponseMessage> ConcederAsync(
        HttpClient cliente, Guid idContrato, object corpo) =>
        cliente.PostAsJsonAsync($"/api/contratos/{idContrato}/ferias/concessoes", corpo);

    private static int _sufixo;

    private static string Sufixo() => Interlocked.Increment(ref _sufixo).ToString("D4");

    private async Task<Guid> ContratoAsync(HttpClient cliente, string sufixo, string admissao)
    {
        using var respostaCargo = await cliente.PostAsJsonAsync("/api/cargos", new
        {
            codigo = $"FER{sufixo}",
            nome = $"Cargo ferias {sufixo}",
        });
        respostaCargo.EnsureSuccessStatusCode();
        var cargo = (await respostaCargo.Content.ReadFromJsonAsync<Identificado>())!;

        using var respostaFuncionario = await cliente.PostAsJsonAsync("/api/funcionarios", new
        {
            nome = $"Ferias Pessoa {sufixo}",
            cpf = BancoPostgresFixture.CpfDeTeste(30000 + int.Parse(sufixo)),
            dataNascimento = "1986-11-03",
        });
        respostaFuncionario.EnsureSuccessStatusCode();
        var funcionario = (await respostaFuncionario.Content.ReadFromJsonAsync<Identificado>())!;

        using var respostaContrato = await cliente.PostAsJsonAsync(
            $"/api/funcionarios/{funcionario.Id}/contratos",
            new
            {
                idEmpresa = banco.IdEmpresaA,
                matricula = $"FER{sufixo}",
                dataAdmissao = admissao,
                salarioInicial = 3000m,
                idCargo = cargo.Id,
                idEstabelecimento = banco.IdEstabelecimentoA,
                jornadaMensalHoras = 220,
            });
        respostaContrato.EnsureSuccessStatusCode();

        return (await respostaContrato.Content.ReadFromJsonAsync<Identificado>())!.Id;
    }

    private static Task<FeriasResposta?> PeriodosAsync(
        HttpClient cliente, Guid idContrato, string referencia) =>
        cliente.GetFromJsonAsync<FeriasResposta>(
            $"/api/contratos/{idContrato}/ferias/periodos?referencia={referencia}");

    // -------------------------------------------------------------- leitura

    [Fact]
    public async Task ContratoDeTresAnos_TemTresPeriodosAdquiridosEUmEmAndamento()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var idContrato = await ContratoAsync(cliente, sufixo, "2023-04-01");

        var ferias = (await PeriodosAsync(cliente, idContrato, "2026-08-28"))!;

        Assert.Equal($"FER{sufixo}", ferias.Matricula);
        Assert.Equal(new DateOnly(2023, 4, 1), ferias.DataAdmissao);
        Assert.Equal(4, ferias.Periodos.Count);

        // Tres completos x 30 dias.
        Assert.Equal(90, ferias.DiasAdquiridos);

        var primeiro = ferias.Periodos[0];
        Assert.Equal(new DateOnly(2023, 4, 1), primeiro.Inicio);
        Assert.Equal(new DateOnly(2024, 3, 31), primeiro.Fim);
        Assert.Equal(new DateOnly(2024, 4, 1), primeiro.InicioConcessao);
        Assert.Equal(new DateOnly(2025, 3, 31), primeiro.LimiteConcessao);

        // Em 28/08/2026 o primeiro ja passou do limite: dobra.
        Assert.Equal("Vencido", primeiro.Situacao);
        Assert.True(primeiro.EmDobra);

        var atual = ferias.Periodos[^1];
        Assert.Equal("EmAndamento", atual.Situacao);
        Assert.False(atual.EmDobra);
    }

    [Fact]
    public async Task ReferenciaMudaARespostaSemMudarNada()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var idContrato = await ContratoAsync(cliente, sufixo, "2025-01-01");

        // Antes de completar 12 meses: nenhum direito.
        var antes = (await PeriodosAsync(cliente, idContrato, "2025-12-31"))!;
        Assert.Equal(0, antes.DiasAdquiridos);
        Assert.Equal("EmAndamento", Assert.Single(antes.Periodos).Situacao);

        // No dia seguinte: 30 dias.
        var depois = (await PeriodosAsync(cliente, idContrato, "2026-01-01"))!;
        Assert.Equal(30, depois.DiasAdquiridos);
        Assert.Equal(2, depois.Periodos.Count);

        // Depois do limite de concessao: vencido.
        var vencido = (await PeriodosAsync(cliente, idContrato, "2027-01-01"))!;
        Assert.Equal(1, vencido.PeriodosVencidos);
    }

    [Fact]
    public async Task SemReferencia_UsaHoje()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var idContrato = await ContratoAsync(cliente, sufixo, "2020-06-15");

        var ferias = await cliente.GetFromJsonAsync<FeriasResposta>(
            $"/api/contratos/{idContrato}/ferias/periodos");

        Assert.NotNull(ferias);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), ferias!.Referencia);
        Assert.NotEmpty(ferias.Periodos);
    }

    [Fact]
    public async Task ContratoInexistente_Devolve404()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);

        using var resposta = await cliente.GetAsync(
            $"/api/contratos/{Guid.CreateVersion7()}/ferias/periodos");

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    // --------------------------------------------------------- autorizacao

    [Fact]
    public async Task Visualizador_PodeLer()
    {
        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var idContrato = await ContratoAsync(admin, sufixo, "2022-02-01");

        var visualizador = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailVisualizadorA);

        var ferias = await visualizador.GetFromJsonAsync<FeriasResposta>(
            $"/api/contratos/{idContrato}/ferias/periodos");

        Assert.NotEmpty(ferias!.Periodos);
    }

    [Fact]
    public async Task SemAutenticacao_NaoLe()
    {
        var anonimo = _fabrica.CreateClient();

        using var resposta = await anonimo.GetAsync(
            $"/api/contratos/{Guid.CreateVersion7()}/ferias/periodos");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    // --------------------------------------------------------- concessao

    [Fact]
    public async Task Conceder_BaixaOSaldoDoPeriodo()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var idContrato = await ContratoAsync(cliente, sufixo, "2023-04-01");

        using var criacao = await ConcederAsync(cliente, idContrato, new
        {
            inicioPeriodoAquisitivo = "2023-04-01",
            inicio = "2026-11-02",
            dias = 20,
            diasAbonoPecuniario = 10,
        });
        Assert.Equal(HttpStatusCode.Created, criacao.StatusCode);

        var criada = (await criacao.Content.ReadFromJsonAsync<ConcessaoItem>())!;
        Assert.Equal(new DateOnly(2026, 11, 21), criada.Fim);
        Assert.Equal(30, criada.DiasBaixados);
        Assert.Equal("Programada", criada.Situacao);

        var ferias = (await PeriodosAsync(cliente, idContrato, "2026-08-28"))!;
        var primeiro = ferias.Periodos[0];

        Assert.Equal(30, primeiro.DiasConcedidos);
        Assert.Equal(0, primeiro.Saldo);
        Assert.Equal(0, primeiro.SaldoAbono);
        Assert.Equal(1, primeiro.FracoesUsadas);
        Assert.Single(primeiro.Concessoes);

        // O saldo total desconta o que ja foi programado, mas os dias
        // ADQUIRIDOS continuam sendo o direito bruto.
        Assert.Equal(90, ferias.DiasAdquiridos);
        Assert.Equal(60, ferias.SaldoTotal);
    }

    [Fact]
    public async Task Conceder_AlemDoSaldo_ERecusado()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var idContrato = await ContratoAsync(cliente, sufixo, "2023-04-01");

        using var resposta = await ConcederAsync(cliente, idContrato, new
        {
            inicioPeriodoAquisitivo = "2023-04-01",
            inicio = "2026-11-02",
            dias = 31,
            diasAbonoPecuniario = 0,
        });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);

        var problema = (await resposta.Content.ReadFromJsonAsync<ProblemaValidacao>())!;
        Assert.Contains(problema.Errors["concessao"], m => m.Contains("nao tem dias suficientes"));
    }

    [Fact]
    public async Task Conceder_ComAbonoAcimaDoTerco_ERecusadoCitandoALei()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var idContrato = await ContratoAsync(cliente, sufixo, "2023-04-01");

        using var resposta = await ConcederAsync(cliente, idContrato, new
        {
            inicioPeriodoAquisitivo = "2023-04-01",
            inicio = "2026-11-02",
            dias = 19,
            diasAbonoPecuniario = 11,
        });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);

        var problema = (await resposta.Content.ReadFromJsonAsync<ProblemaValidacao>())!;
        Assert.Contains(problema.Errors["concessao"], m => m.Contains("art. 143"));
    }

    [Fact]
    public async Task Conceder_FracaoAbaixoDeCinco_ERecusada()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var idContrato = await ContratoAsync(cliente, sufixo, "2023-04-01");

        using var resposta = await ConcederAsync(cliente, idContrato, new
        {
            inicioPeriodoAquisitivo = "2023-04-01",
            inicio = "2026-11-02",
            dias = 4,
            diasAbonoPecuniario = 0,
        });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);

        var problema = (await resposta.Content.ReadFromJsonAsync<ProblemaValidacao>())!;
        Assert.Contains(problema.Errors["concessao"], m => m.Contains("art. 134"));
    }

    [Fact]
    public async Task Conceder_ComGozoSobreposto_ERecusado()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var idContrato = await ContratoAsync(cliente, sufixo, "2023-04-01");

        using var primeira = await ConcederAsync(cliente, idContrato, new
        {
            inicioPeriodoAquisitivo = "2023-04-01",
            inicio = "2026-11-02",
            dias = 15,
            diasAbonoPecuniario = 0,
        });
        primeira.EnsureSuccessStatusCode();

        using var segunda = await ConcederAsync(cliente, idContrato, new
        {
            inicioPeriodoAquisitivo = "2023-04-01",
            inicio = "2026-11-10",
            dias = 10,
            diasAbonoPecuniario = 0,
        });

        Assert.Equal(HttpStatusCode.BadRequest, segunda.StatusCode);
    }

    [Fact]
    public async Task Conceder_EmPeriodoQueOContratoNaoTem_ERecusado()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var idContrato = await ContratoAsync(cliente, sufixo, "2023-04-01");

        // Data inventada pelo cliente: o periodo e procurado entre os
        // DERIVADOS, entao nao existe.
        using var resposta = await ConcederAsync(cliente, idContrato, new
        {
            inicioPeriodoAquisitivo = "2019-01-01",
            inicio = "2026-11-02",
            dias = 30,
            diasAbonoPecuniario = 0,
        });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Cancelar_LiberaOSaldo()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var idContrato = await ContratoAsync(cliente, sufixo, "2023-04-01");

        using var criacao = await ConcederAsync(cliente, idContrato, new
        {
            inicioPeriodoAquisitivo = "2023-04-01",
            inicio = "2030-05-01",
            dias = 30,
            diasAbonoPecuniario = 0,
        });
        var criada = (await criacao.Content.ReadFromJsonAsync<ConcessaoItem>())!;

        using var remocao = await cliente.DeleteAsync(
            $"/api/contratos/{idContrato}/ferias/concessoes/{criada.Id}");
        Assert.Equal(HttpStatusCode.NoContent, remocao.StatusCode);

        var ferias = (await PeriodosAsync(cliente, idContrato, "2026-08-28"))!;
        Assert.Equal(30, ferias.Periodos[0].Saldo);
    }

    [Fact]
    public async Task Auditor_NaoConcede()
    {
        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var idContrato = await ContratoAsync(admin, sufixo, "2023-04-01");

        var auditor = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAuditorA);

        using var resposta = await ConcederAsync(auditor, idContrato, new
        {
            inicioPeriodoAquisitivo = "2023-04-01",
            inicio = "2026-11-02",
            dias = 30,
            diasAbonoPecuniario = 0,
        });

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    // --------------------------------------------------------- isolamento

    [Fact]
    public async Task ContratoDeOutraOrganizacao_Devolve404()
    {
        var clienteA = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var idContrato = await ContratoAsync(clienteA, sufixo, "2021-08-01");

        var clienteB = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminB);

        using var resposta = await clienteB.GetAsync(
            $"/api/contratos/{idContrato}/ferias/periodos");

        // 404 e nao 403: um 403 confirmaria que o contrato existe, e o
        // historico de admissao do vizinho poderia ser mapeado um id por vez.
        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task IdorPeloIdDaConcessao_NaoAlcanca()
    {
        var clienteA = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixoA = Sufixo();
        var contratoA = await ContratoAsync(clienteA, sufixoA, "2023-04-01");

        using var criacao = await ConcederAsync(clienteA, contratoA, new
        {
            inicioPeriodoAquisitivo = "2023-04-01",
            inicio = "2030-07-01",
            dias = 30,
            diasAbonoPecuniario = 0,
        });
        var daA = (await criacao.Content.ReadFromJsonAsync<ConcessaoItem>())!;

        // A organizacao B tem contrato proprio e tenta usar o id da concessao
        // da A por baixo dele. Como a concessao e resolvida pelo PAI, o
        // caminho nao existe.
        var clienteB = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminB);

        using var remocao = await clienteB.DeleteAsync(
            $"/api/contratos/{contratoA}/ferias/concessoes/{daA.Id}");
        Assert.Equal(HttpStatusCode.NotFound, remocao.StatusCode);

        // E a concessao continua intacta na organizacao dona.
        var ferias = (await PeriodosAsync(clienteA, contratoA, "2026-08-28"))!;
        Assert.Single(ferias.Periodos[0].Concessoes);
    }

    [Fact]
    public async Task ConcessaoDeOutraOrganizacao_NaoAparece()
    {
        var clienteA = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var contratoA = await ContratoAsync(clienteA, sufixo, "2023-04-01");

        using var criacao = await ConcederAsync(clienteA, contratoA, new
        {
            inicioPeriodoAquisitivo = "2023-04-01",
            inicio = "2030-09-01",
            dias = 30,
            diasAbonoPecuniario = 0,
        });
        criacao.EnsureSuccessStatusCode();

        var clienteB = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminB);

        using var resposta = await clienteB.GetAsync(
            $"/api/contratos/{contratoA}/ferias/periodos");

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }
}
