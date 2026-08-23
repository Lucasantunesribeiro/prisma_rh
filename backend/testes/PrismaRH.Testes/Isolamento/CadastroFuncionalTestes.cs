using System.Net;
using System.Net.Http.Json;

namespace PrismaRH.Testes.Isolamento;

/// <summary>
/// Isolamento e autorizacao do cadastro funcional, contra PostgreSQL real.
///
/// Estes testes valem mais do que os de empresa: funcionario carrega CPF, data
/// de nascimento e salario. Um furo aqui nao e um vazamento de razao social -
/// e um vazamento de dado pessoal e de remuneracao.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class CadastroFuncionalTestes(BancoPostgresFixture banco) : IDisposable
{
    private readonly FabricaApiIsolada _fabrica = new(banco.StringConexao);

    public void Dispose() => _fabrica.Dispose();

    private sealed record Identificado(Guid Id);

    private sealed record PaginaFuncionarios(int Total, List<FuncionarioItem> Itens);

    private sealed record FuncionarioItem(Guid Id, string Nome, string Cpf, bool Ativo);

    private sealed record Vigencia(
        Guid Id,
        DateOnly ValidoDe,
        DateOnly? ValidoAte,
        decimal Salario,
        string Motivo);

    private sealed record Contrato(Guid Id, string Matricula, string Situacao, Vigencia? VigenciaAtual);

    /// <summary>Cria cargo, funcionario e contrato para a organizacao do cliente informado.</summary>
    private static async Task<(Guid IdFuncionario, Guid IdContrato, Guid IdCargo)> MontarCenarioAsync(
        HttpClient cliente,
        Guid idEmpresa,
        Guid idEstabelecimento,
        string cpf,
        string sufixo)
    {
        using var respostaCargo = await cliente.PostAsJsonAsync("/api/cargos", new
        {
            codigo = $"C{sufixo}",
            nome = $"Cargo {sufixo}",
        });
        respostaCargo.EnsureSuccessStatusCode();
        var cargo = (await respostaCargo.Content.ReadFromJsonAsync<Identificado>())!;

        using var respostaFuncionario = await cliente.PostAsJsonAsync("/api/funcionarios", new
        {
            nome = $"Pessoa {sufixo}",
            cpf,
            dataNascimento = "1990-05-20",
        });
        respostaFuncionario.EnsureSuccessStatusCode();
        var funcionario = (await respostaFuncionario.Content.ReadFromJsonAsync<Identificado>())!;

        using var respostaContrato = await cliente.PostAsJsonAsync(
            $"/api/funcionarios/{funcionario.Id}/contratos",
            new
            {
                idEmpresa,
                matricula = $"M{sufixo}",
                dataAdmissao = "2026-01-15",
                salarioInicial = 3000m,
                idCargo = cargo.Id,
                idEstabelecimento,
                jornadaMensalHoras = 220,
            });
        respostaContrato.EnsureSuccessStatusCode();
        var contrato = (await respostaContrato.Content.ReadFromJsonAsync<Identificado>())!;

        return (funcionario.Id, contrato.Id, cargo.Id);
    }

    // ---------------------------------------------------------------- isolamento

    [Fact]
    public async Task Funcionario_DeOutraOrganizacao_NaoAparece_E_Devolve404()
    {
        var clienteA = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var clienteB = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminB);

        var (idDeA, _, _) = await MontarCenarioAsync(
            clienteA, banco.IdEmpresaA, banco.IdEstabelecimentoA, BancoPostgresFixture.CpfDeTeste(1), "A1");

        // PRESENCA: a propria organizacao enxerga.
        var listaA = await clienteA.GetFromJsonAsync<PaginaFuncionarios>("/api/funcionarios");
        Assert.Contains(listaA!.Itens, f => f.Id == idDeA);

        // AUSENCIA: a vizinha nao.
        var listaB = await clienteB.GetFromJsonAsync<PaginaFuncionarios>("/api/funcionarios");
        Assert.DoesNotContain(listaB!.Itens, f => f.Id == idDeA);

        using var direto = await clienteB.GetAsync($"/api/funcionarios/{idDeA}");
        Assert.Equal(HttpStatusCode.NotFound, direto.StatusCode);
    }

    [Fact]
    public async Task Historico_DeContratoAlheio_NaoEAcessivel()
    {
        var clienteA = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var clienteB = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminB);

        var (_, idContrato, _) = await MontarCenarioAsync(
            clienteA, banco.IdEmpresaA, banco.IdEstabelecimentoA, BancoPostgresFixture.CpfDeTeste(2), "A2");

        using var proprio = await clienteA.GetAsync($"/api/contratos/{idContrato}/vigencias");
        Assert.Equal(HttpStatusCode.OK, proprio.StatusCode);

        // Salario e o dado mais sensivel do sistema. Nem 403 - 404.
        using var alheio = await clienteB.GetAsync($"/api/contratos/{idContrato}/vigencias");
        Assert.Equal(HttpStatusCode.NotFound, alheio.StatusCode);
    }

    [Fact]
    public async Task Cpf_PodeSeRepetirEntreOrganizacoes_MasNaoDentroDeUma()
    {
        var clienteA = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var clienteB = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminB);

        var cpf = BancoPostgresFixture.CpfDeTeste(6);
        var corpo = new { nome = "Mesma pessoa", cpf, dataNascimento = "1985-03-10" };

        using var naA = await clienteA.PostAsJsonAsync("/api/funcionarios", corpo);
        Assert.Equal(HttpStatusCode.Created, naA.StatusCode);

        // A mesma pessoa pode trabalhar para empresas de organizacoes diferentes.
        using var naB = await clienteB.PostAsJsonAsync("/api/funcionarios", corpo);
        Assert.Equal(HttpStatusCode.Created, naB.StatusCode);

        // Mas nao duas vezes na mesma organizacao.
        using var duplicado = await clienteA.PostAsJsonAsync("/api/funcionarios", corpo);
        Assert.Equal(HttpStatusCode.Conflict, duplicado.StatusCode);
    }

    // ---------------------------------------------------------------- historico

    [Fact]
    public async Task Aumento_FechaAVigenciaAnterior_E_OSalarioAntigoContinuaConsultavel()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var (_, idContrato, idCargo) = await MontarCenarioAsync(
            cliente, banco.IdEmpresaA, banco.IdEstabelecimentoA, BancoPostgresFixture.CpfDeTeste(3), "A3");

        using var alteracao = await cliente.PostAsJsonAsync($"/api/contratos/{idContrato}/vigencias", new
        {
            validoDe = "2026-06-01",
            salario = 4200m,
            idCargo,
            idEstabelecimento = banco.IdEstabelecimentoA,
            jornadaMensalHoras = 220,
            motivo = "AlteracaoSalarial",
        });
        Assert.True(
            alteracao.StatusCode == HttpStatusCode.Created,
            $"esperado Created, veio {alteracao.StatusCode}: {await alteracao.Content.ReadAsStringAsync()}");

        var historico = await cliente.GetFromJsonAsync<List<Vigencia>>($"/api/contratos/{idContrato}/vigencias");
        Assert.Equal(2, historico!.Count);

        var antiga = historico.Single(v => v.Motivo == "Admissao");
        Assert.Equal(new DateOnly(2026, 5, 31), antiga.ValidoAte);
        Assert.Equal(3000m, antiga.Salario);

        // A pergunta que o motor de calculo da Fase 3 vai fazer.
        var antes = await cliente.GetFromJsonAsync<Vigencia>(
            $"/api/contratos/{idContrato}/vigencia?data=2026-05-31");
        Assert.Equal(3000m, antes!.Salario);

        var depois = await cliente.GetFromJsonAsync<Vigencia>(
            $"/api/contratos/{idContrato}/vigencia?data=2026-06-01");
        Assert.Equal(4200m, depois!.Salario);
    }

    [Fact]
    public async Task Alteracao_ComDataQueSobrepoe_ERecusada()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var (_, idContrato, idCargo) = await MontarCenarioAsync(
            cliente, banco.IdEmpresaA, banco.IdEstabelecimentoA, BancoPostgresFixture.CpfDeTeste(4), "A4");

        // Mesma data de inicio da vigencia atual: criaria dois periodos validos
        // no mesmo dia, e a folha escolheria um deles em silencio.
        using var resposta = await cliente.PostAsJsonAsync($"/api/contratos/{idContrato}/vigencias", new
        {
            validoDe = "2026-01-15",
            salario = 9999m,
            idCargo,
            idEstabelecimento = banco.IdEstabelecimentoA,
            jornadaMensalHoras = 220,
            motivo = "AlteracaoSalarial",
        });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Desligamento_FechaOHistorico_E_ImpedeNovaAlteracao()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var (_, idContrato, idCargo) = await MontarCenarioAsync(
            cliente, banco.IdEmpresaA, banco.IdEstabelecimentoA, BancoPostgresFixture.CpfDeTeste(5), "A5");

        using var desligamento = await cliente.PostAsJsonAsync(
            $"/api/contratos/{idContrato}/desligamento",
            new { dataDesligamento = "2026-07-31" });

        Assert.Equal(HttpStatusCode.OK, desligamento.StatusCode);
        var contrato = (await desligamento.Content.ReadFromJsonAsync<Contrato>())!;
        Assert.Equal("Desligado", contrato.Situacao);
        Assert.Null(contrato.VigenciaAtual);

        // O passado continua consultavel depois do desligamento.
        var durante = await cliente.GetFromJsonAsync<Vigencia>(
            $"/api/contratos/{idContrato}/vigencia?data=2026-03-01");
        Assert.Equal(3000m, durante!.Salario);

        // Depois da saida nao havia contrato valendo: 204, nao erro.
        using var depois = await cliente.GetAsync($"/api/contratos/{idContrato}/vigencia?data=2026-08-01");
        Assert.Equal(HttpStatusCode.NoContent, depois.StatusCode);

        using var tentativa = await cliente.PostAsJsonAsync($"/api/contratos/{idContrato}/vigencias", new
        {
            validoDe = "2026-09-01",
            salario = 5000m,
            idCargo,
            idEstabelecimento = banco.IdEstabelecimentoA,
            jornadaMensalHoras = 220,
            motivo = "AlteracaoSalarial",
        });
        Assert.Equal(HttpStatusCode.Conflict, tentativa.StatusCode);
    }

    // ---------------------------------------------------------------- autorizacao

    [Fact]
    public async Task AnalistaDeRh_MantemCadastroDePessoas_MasNaoAdministraEmpresas()
    {
        var analista = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAnalistaA);

        // Manter cadastros e atribuicao do Analista (CLAUDE.md secao 6).
        using var criaFuncionario = await analista.PostAsJsonAsync("/api/funcionarios", new
        {
            nome = "Criado pelo analista",
            cpf = BancoPostgresFixture.CpfDeTeste(7),
            dataNascimento = "1992-11-02",
        });
        Assert.Equal(HttpStatusCode.Created, criaFuncionario.StatusCode);

        // Administrar empresas, nao.
        using var criaEmpresa = await analista.PostAsJsonAsync("/api/empresas", new
        {
            razaoSocial = "Nao deveria entrar",
            cnpj = "11222333000181",
        });
        Assert.Equal(HttpStatusCode.Forbidden, criaEmpresa.StatusCode);
    }

    [Theory]
    [InlineData(BancoPostgresFixture.EmailAuditorA)]
    [InlineData(BancoPostgresFixture.EmailVisualizadorA)]
    public async Task AuditorEVisualizador_Leem_MasNaoCriamPessoas(string email)
    {
        var cliente = await _fabrica.ClienteComoAsync(email);

        using var leitura = await cliente.GetAsync("/api/funcionarios");
        Assert.Equal(HttpStatusCode.OK, leitura.StatusCode);

        using var escrita = await cliente.PostAsJsonAsync("/api/funcionarios", new
        {
            nome = "Nao deveria entrar",
            cpf = BancoPostgresFixture.CpfDeTeste(8),
            dataNascimento = "1990-01-01",
        });
        Assert.Equal(HttpStatusCode.Forbidden, escrita.StatusCode);

        using var cargo = await cliente.PostAsJsonAsync("/api/cargos", new
        {
            codigo = "XX",
            nome = "Nao deveria entrar",
        });
        Assert.Equal(HttpStatusCode.Forbidden, cargo.StatusCode);
    }

    [Fact]
    public async Task Listagem_MascaraOCpf_E_ODetalheMostraCompleto()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var cpf = BancoPostgresFixture.CpfDeTeste(9);

        using var criacao = await cliente.PostAsJsonAsync("/api/funcionarios", new
        {
            nome = "Pessoa mascarada",
            cpf,
            dataNascimento = "1988-07-07",
        });
        criacao.EnsureSuccessStatusCode();
        var criado = (await criacao.Content.ReadFromJsonAsync<Identificado>())!;

        var lista = await cliente.GetFromJsonAsync<PaginaFuncionarios>("/api/funcionarios?nome=mascarada");
        var naLista = Assert.Single(lista!.Itens, f => f.Id == criado.Id);

        // CPF e dado pessoal: a lista identifica sem expor o documento inteiro.
        Assert.Contains("*", naLista.Cpf);
        Assert.DoesNotContain(cpf.Substring(3, 5), naLista.Cpf);

        var detalhe = await cliente.GetFromJsonAsync<FuncionarioItem>($"/api/funcionarios/{criado.Id}");
        Assert.Equal(cpf, detalhe!.Cpf);
    }
}
