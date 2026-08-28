using System.Net;
using System.Net.Http.Json;

namespace PrismaRH.Testes.Isolamento;

/// <summary>
/// Fase 4F etapa 1 ponta a ponta, contra PostgreSQL real.
///
/// Nenhuma folha e calculada: esta etapa nao mexe em dinheiro. Como as ferias
/// da 4E etapa 1, ela le contrato e calendario - e por isso nao precisa de
/// organizacao propria.
///
/// PREFIXO "DEC" em cargo e matricula: os contadores das classes comecam todos
/// em 0001, e prefixo repetido colide no indice unico da organizacao A.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class DecimoTerceiroIntegracaoTestes(BancoPostgresFixture banco) : IDisposable
{
    private readonly FabricaApiIsolada _fabrica = new(banco.StringConexao);

    public void Dispose() => _fabrica.Dispose();

    private sealed record Identificado(Guid Id);

    private sealed record MesItem(int Mes, int DiasTrabalhados, bool Conta, string Motivo);

    private sealed record AvosResposta(
        Guid IdContrato, string Matricula, DateOnly DataAdmissao, DateOnly? DataDesligamento,
        int Ano, int Avos, string Fracao, bool AnoCompleto, List<MesItem> Meses);

    private static int _sufixo;

    private static string Sufixo() => Interlocked.Increment(ref _sufixo).ToString("D4");

    private async Task<Guid> ContratoAsync(HttpClient cliente, string sufixo, string admissao)
    {
        using var respostaCargo = await cliente.PostAsJsonAsync("/api/cargos", new
        {
            codigo = $"DEC{sufixo}",
            nome = $"Cargo 13o {sufixo}",
        });
        respostaCargo.EnsureSuccessStatusCode();
        var cargo = (await respostaCargo.Content.ReadFromJsonAsync<Identificado>())!;

        using var respostaFuncionario = await cliente.PostAsJsonAsync("/api/funcionarios", new
        {
            nome = $"Decimo Pessoa {sufixo}",
            cpf = BancoPostgresFixture.CpfDeTeste(50000 + int.Parse(sufixo)),
            dataNascimento = "1983-05-08",
        });
        respostaFuncionario.EnsureSuccessStatusCode();
        var funcionario = (await respostaFuncionario.Content.ReadFromJsonAsync<Identificado>())!;

        using var respostaContrato = await cliente.PostAsJsonAsync(
            $"/api/funcionarios/{funcionario.Id}/contratos",
            new
            {
                idEmpresa = banco.IdEmpresaA,
                matricula = $"DEC{sufixo}",
                dataAdmissao = admissao,
                salarioInicial = 3000m,
                idCargo = cargo.Id,
                idEstabelecimento = banco.IdEstabelecimentoA,
                jornadaMensalHoras = 220,
            });
        respostaContrato.EnsureSuccessStatusCode();

        return (await respostaContrato.Content.ReadFromJsonAsync<Identificado>())!.Id;
    }

    private static Task<AvosResposta?> AvosAsync(HttpClient cliente, Guid idContrato, int ano) =>
        cliente.GetFromJsonAsync<AvosResposta>(
            $"/api/contratos/{idContrato}/decimo-terceiro/avos?ano={ano}");

    // -------------------------------------------------------------- leitura

    [Fact]
    public async Task AnoInteiro_DaDozeAvos()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var idContrato = await ContratoAsync(cliente, sufixo, "2020-01-01");

        var avos = (await AvosAsync(cliente, idContrato, 2026))!;

        Assert.Equal($"DEC{sufixo}", avos.Matricula);
        Assert.Equal(12, avos.Avos);
        Assert.Equal("12/12", avos.Fracao);
        Assert.True(avos.AnoCompleto);
        Assert.Equal(12, avos.Meses.Count);
    }

    [Fact]
    public async Task AdmitidoNoDia17DeMarco_ContaMarco()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();

        // 17 a 31 de marco sao 15 dias EXATOS. A lei diz "igual ou superior".
        var idContrato = await ContratoAsync(cliente, sufixo, "2026-03-17");

        var avos = (await AvosAsync(cliente, idContrato, 2026))!;

        Assert.Equal(10, avos.Avos);

        var marco = avos.Meses.Single(m => m.Mes == 3);
        Assert.Equal(15, marco.DiasTrabalhados);
        Assert.True(marco.Conta);
        Assert.Equal("15 dias trabalhados", marco.Motivo);
    }

    [Fact]
    public async Task AdmitidoNoDia18DeMarco_NaoContaMarco()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var idContrato = await ContratoAsync(cliente, sufixo, "2026-03-18");

        var avos = (await AvosAsync(cliente, idContrato, 2026))!;

        Assert.Equal(9, avos.Avos);

        var marco = avos.Meses.Single(m => m.Mes == 3);
        Assert.Equal(14, marco.DiasTrabalhados);
        Assert.False(marco.Conta);
        Assert.Equal("so 14 dias, menos que 15", marco.Motivo);
    }

    [Fact]
    public async Task TodosOsDozeMesesVoltam_MesmoOsQueNaoContam()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var idContrato = await ContratoAsync(cliente, sufixo, "2026-11-20");

        var avos = (await AvosAsync(cliente, idContrato, 2026))!;

        // A tela precisa mostrar POR QUE cada mes ficou de fora.
        Assert.Equal(12, avos.Meses.Count);
        Assert.Equal(1, avos.Avos);
        Assert.All(avos.Meses.Take(10), m => Assert.Equal("sem vinculo no mes", m.Motivo));
    }

    [Fact]
    public async Task AnoAnterior_ETConsultavel()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var idContrato = await ContratoAsync(cliente, sufixo, "2024-07-10");

        // 2024: julho tem 22 dias, conta. Julho a dezembro = 6.
        var em2024 = (await AvosAsync(cliente, idContrato, 2024))!;
        Assert.Equal(6, em2024.Avos);

        // 2025: ano inteiro.
        var em2025 = (await AvosAsync(cliente, idContrato, 2025))!;
        Assert.Equal(12, em2025.Avos);
    }

    [Fact]
    public async Task SemAno_UsaOCorrente()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var idContrato = await ContratoAsync(cliente, sufixo, "2020-01-01");

        var avos = await cliente.GetFromJsonAsync<AvosResposta>(
            $"/api/contratos/{idContrato}/decimo-terceiro/avos");

        Assert.Equal(DateTime.UtcNow.Year, avos!.Ano);
    }

    [Fact]
    public async Task AnoForaDoIntervalo_ERecusado()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var idContrato = await ContratoAsync(cliente, sufixo, "2020-01-01");

        using var resposta = await cliente.GetAsync(
            $"/api/contratos/{idContrato}/decimo-terceiro/avos?ano=1900");

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task ContratoInexistente_Devolve404()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);

        using var resposta = await cliente.GetAsync(
            $"/api/contratos/{Guid.CreateVersion7()}/decimo-terceiro/avos");

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    // --------------------------------------------------------- autorizacao

    [Fact]
    public async Task Auditor_PodeLer()
    {
        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var idContrato = await ContratoAsync(admin, sufixo, "2022-02-01");

        var auditor = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAuditorA);

        var avos = await auditor.GetFromJsonAsync<AvosResposta>(
            $"/api/contratos/{idContrato}/decimo-terceiro/avos?ano=2026");

        Assert.Equal(12, avos!.Avos);
    }

    [Fact]
    public async Task SemAutenticacao_NaoLe()
    {
        var anonimo = _fabrica.CreateClient();

        using var resposta = await anonimo.GetAsync(
            $"/api/contratos/{Guid.CreateVersion7()}/decimo-terceiro/avos");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    // ---------------------------------------------------------- isolamento

    [Fact]
    public async Task ContratoDeOutraOrganizacao_Devolve404()
    {
        var clienteA = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var idContrato = await ContratoAsync(clienteA, sufixo, "2021-08-01");

        var clienteB = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminB);

        using var resposta = await clienteB.GetAsync(
            $"/api/contratos/{idContrato}/decimo-terceiro/avos?ano=2026");

        // 404 e nao 403: um 403 confirmaria que o contrato existe.
        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }
}
