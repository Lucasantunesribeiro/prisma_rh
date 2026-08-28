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

    private sealed record PeriodoItem(
        int Numero, DateOnly Inicio, DateOnly Fim, DateOnly InicioConcessao,
        DateOnly LimiteConcessao, int DiasDireito, string Situacao,
        int DiasParaCompletar, bool EmDobra);

    private sealed record FeriasResposta(
        Guid IdContrato, string Matricula, DateOnly DataAdmissao, DateOnly? DataDesligamento,
        DateOnly Referencia, int DiasAdquiridos, int PeriodosVencidos, List<PeriodoItem> Periodos);

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
}
