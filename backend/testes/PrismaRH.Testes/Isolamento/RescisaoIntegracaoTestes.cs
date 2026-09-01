using System.Net;
using System.Net.Http.Json;

namespace PrismaRH.Testes.Isolamento;

/// <summary>
/// Fase 4G etapa 2 ponta a ponta, contra PostgreSQL real.
///
/// Os testes de dominio provam a aritmetica e a matriz; estes provam o que so
/// o sistema inteiro prova: que o salario usado e o da DATA DO DESLIGAMENTO,
/// que as ferias vencidas saem do saldo real dos periodos, e que o valor base
/// do FGTS e o INFORMADO - nunca o que o sistema conhece.
///
/// PREFIXO "RES" em cargo e matricula, pelo motivo ja documentado em
/// FeriasIntegracaoTestes: prefixo repetido colide no indice unico.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class RescisaoIntegracaoTestes(BancoPostgresFixture banco) : IDisposable
{
    private readonly FabricaApiIsolada _fabrica = new(banco.StringConexao);

    public void Dispose() => _fabrica.Dispose();

    private sealed record Identificado(Guid Id);

    private sealed record LinhaMemoria(int Ordem, string Descricao, string Expressao, decimal Valor);

    private sealed record VerbaItem(
        string Codigo, string Nome, decimal Valor, string Referencia, List<LinhaMemoria> Memoria);

    private sealed record AvisoItem(
        string Devedor, int AnosCompletos, int DiasBase, int DiasAcrescidos, int Dias, bool Reduzido);

    private sealed record MesProporcionalItem(
        DateOnly Inicio, DateOnly Fim, int Dias, bool Conta, string Motivo);

    private sealed record FeriasProporcionaisItem(
        DateOnly InicioPeriodo, DateOnly FimPeriodo, int Avos, string Fracao,
        List<MesProporcionalItem> Meses);

    private sealed record ValorBaseItem(
        decimal Informado, decimal ConhecidoPeloSistema, bool AbaixoDoConhecido);

    private sealed record RescisaoItem(
        Guid IdContrato, string Matricula, string Motivo, DateOnly DataDesligamento,
        decimal SalarioReferencia, bool Suportado, string? MotivoDoBloqueio, string Fonte,
        AvisoItem? Aviso, FeriasProporcionaisItem? FeriasProporcionais, int DiasFeriasVencidas,
        int Avos13, string? Fracao13, DateOnly DataProjetada, int AvosDoAviso,
        ValorBaseItem? ValorBaseFgts, decimal FgtsConhecidoPeloSistema,
        decimal Total, List<VerbaItem> Verbas);

    private sealed record MatrizItem(
        string Motivo, bool Suportado, string DevedorDoAviso, bool AvisoPelaMetade,
        bool FeriasProporcionais, decimal PercentualMultaFgts, string Fonte, string? MotivoDoBloqueio);

    private static int _sufixo;

    private static string Sufixo() => Interlocked.Increment(ref _sufixo).ToString("D4");

    private async Task<Guid> ContratoDesligadoAsync(
        HttpClient cliente, string sufixo, string admissao, string desligamento, string motivo)
    {
        using var respostaCargo = await cliente.PostAsJsonAsync("/api/cargos", new
        {
            codigo = $"RES{sufixo}",
            nome = $"Cargo rescisao {sufixo}",
        });
        respostaCargo.EnsureSuccessStatusCode();
        var cargo = (await respostaCargo.Content.ReadFromJsonAsync<Identificado>())!;

        using var respostaFuncionario = await cliente.PostAsJsonAsync("/api/funcionarios", new
        {
            nome = $"Rescisao Pessoa {sufixo}",
            cpf = BancoPostgresFixture.CpfDeTeste(60000 + int.Parse(sufixo)),
            dataNascimento = "1982-04-17",
        });
        respostaFuncionario.EnsureSuccessStatusCode();
        var funcionario = (await respostaFuncionario.Content.ReadFromJsonAsync<Identificado>())!;

        using var respostaContrato = await cliente.PostAsJsonAsync(
            $"/api/funcionarios/{funcionario.Id}/contratos",
            new
            {
                idEmpresa = banco.IdEmpresaA,
                matricula = $"RES{sufixo}",
                dataAdmissao = admissao,
                salarioInicial = 3000m,
                idCargo = cargo.Id,
                idEstabelecimento = banco.IdEstabelecimentoA,
                jornadaMensalHoras = 220,
            });
        respostaContrato.EnsureSuccessStatusCode();
        var contrato = (await respostaContrato.Content.ReadFromJsonAsync<Identificado>())!;

        using var baixa = await cliente.PostAsJsonAsync(
            $"/api/contratos/{contrato.Id}/desligamento",
            new { dataDesligamento = desligamento, motivo });
        baixa.EnsureSuccessStatusCode();

        return contrato.Id;
    }

    /// <summary>
    /// Apura a rescisao, GRAVANDO antes o valor base do FGTS quando informado.
    ///
    /// O valor deixou de viajar na query string na etapa 3: ele e um dado que o
    /// analista informa e que precisa ficar registrado com autor e data, e nao
    /// um parametro de leitura. Por isso vai por PUT, no corpo.
    /// </summary>
    private static async Task<RescisaoItem?> ApurarAsync(
        HttpClient cliente, Guid idContrato, decimal? valorBaseFgts = null)
    {
        if (valorBaseFgts is { } v)
        {
            using var gravacao = await cliente.PutAsJsonAsync(
                $"/api/contratos/{idContrato}/rescisao/valor-base-fgts",
                new { valor = v, observacao = "Extrato do FGTS Digital" });

            gravacao.EnsureSuccessStatusCode();
        }

        return await cliente.GetFromJsonAsync<RescisaoItem>(
            $"/api/contratos/{idContrato}/rescisao");
    }

    // --------------------------------------------------------------- matriz

    [Fact]
    public async Task Matriz_TemOsOitoMotivos_ComCincoSuportados()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);

        // ⚠️ Rota movida na Fase 12: era
        // `/api/contratos/{id}/rescisao/matriz` e devolvia 200 para contrato de
        // ninguem, porque o handler nunca olhou o contrato. E tabela de
        // referencia do sistema, e nao sub-recurso de tenant.
        var matriz = await cliente.GetFromJsonAsync<List<MatrizItem>>("/api/rescisao/matriz");

        Assert.Equal(8, matriz!.Count);
        Assert.Equal(5, matriz.Count(m => m.Suportado));
        Assert.Equal(3, matriz.Count(m => !m.Suportado));

        // Toda linha cita a fonte, e as bloqueadas dizem por que.
        Assert.All(matriz, m => Assert.NotEmpty(m.Fonte));
        Assert.All(matriz.Where(m => !m.Suportado), m => Assert.NotNull(m.MotivoDoBloqueio));

        var semJustaCausa = matriz.Single(m => m.Motivo == "DispensaSemJustaCausa");
        Assert.Equal(40m, semJustaCausa.PercentualMultaFgts);

        var acordo = matriz.Single(m => m.Motivo == "AcordoEntreAsPartes");
        Assert.Equal(20m, acordo.PercentualMultaFgts);
        Assert.True(acordo.AvisoPelaMetade);
    }

    // ------------------------------------------------------------- apuracao

    [Fact]
    public async Task DispensaSemJustaCausa_GeraAsVerbas()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();

        var idContrato = await ContratoDesligadoAsync(
            cliente, sufixo, "2024-01-10", "2026-05-20", "DispensaSemJustaCausa");

        var r = (await ApurarAsync(cliente, idContrato, 10000m))!;

        Assert.True(r.Suportado);
        Assert.Equal(3000.00m, r.SalarioReferencia);

        decimal Valor(string c) => r.Verbas.Single(v => v.Codigo == c).Valor;

        // Conferido a mao: diario 100.
        Assert.Equal(2000.00m, Valor("SALDO"));      // 01/05 a 20/05 = 20 dias
        Assert.Equal(36, r.Aviso!.Dias);             // 30 + 2 anos x 3
        Assert.Equal(3600.00m, Valor("AVISO"));
        // O aviso PROJETA a saida (CLT art. 487 par. 1o, OJ 82 SDI-1): a data
        // de saida na CTPS e o fim do aviso, e os avos vao ate la.
        Assert.Equal(new DateOnly(2026, 6, 25), r.DataProjetada);  // 20/05 + 36

        Assert.Equal(6, r.FeriasProporcionais!.Avos);
        Assert.Equal(1500.00m, Valor("FERPROP"));    // 3.000 x 6/12
        Assert.Equal(500.00m, Valor("FERPROP13"));
        Assert.Equal(4000.00m, Valor("MULTAFGTS"));  // 40% de 10.000

        // 13o: cinco avos ate a saida, mais um pela projecao do aviso - em
        // verbas SEPARADAS, porque o 13o sobre o aviso nao integra IRRF.
        Assert.Equal(5, r.Avos13);
        Assert.Equal(1250.00m, Valor("DEC13PROP"));
        Assert.Equal(1, r.AvosDoAviso);
        Assert.Equal(250.00m, Valor("DEC13AV"));

        // As ferias VENCIDAS saem do saldo real: dois periodos completos
        // (2024 e 2025), nenhum gozado, 60 dias.
        Assert.Equal(60, r.DiasFeriasVencidas);
        Assert.Equal(6000.00m, Valor("FERVEN"));
        Assert.Equal(2000.00m, Valor("FERVEN13"));
    }

    [Fact]
    public async Task ValorBaseDoFgts_EOINFORMADO_NaoOConhecido()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();

        var idContrato = await ContratoDesligadoAsync(
            cliente, sufixo, "2024-01-10", "2026-05-20", "DispensaSemJustaCausa");

        var r = (await ApurarAsync(cliente, idContrato, 25000m))!;

        Assert.Equal(25000m, r.ValorBaseFgts!.Informado);

        // Este contrato nunca entrou em folha: o sistema conhece zero. E
        // exatamente por isso o valor informado nao pode ser substituido.
        Assert.Equal(0m, r.ValorBaseFgts.ConhecidoPeloSistema);
        Assert.Equal(0m, r.FgtsConhecidoPeloSistema);
        Assert.False(r.ValorBaseFgts.AbaixoDoConhecido);

        Assert.Equal(10000.00m, r.Verbas.Single(v => v.Codigo == "MULTAFGTS").Valor);
    }

    [Fact]
    public async Task SemValorBase_NaoHaLinhaDeMulta()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();

        var idContrato = await ContratoDesligadoAsync(
            cliente, sufixo, "2024-01-10", "2026-05-20", "DispensaSemJustaCausa");

        var r = (await ApurarAsync(cliente, idContrato))!;

        // Melhor nenhuma linha do que uma calculada sobre um numero que o
        // produto nao tem.
        Assert.DoesNotContain(r.Verbas, v => v.Codigo == "MULTAFGTS");

        // NULO, e nao um objeto zerado: "zero informado" e coisa diferente de
        // "nao informado", e a tela precisa distinguir os dois.
        Assert.Null(r.ValorBaseFgts);

        Assert.Contains(r.Verbas, v => v.Codigo == "SALDO");
    }

    [Fact]
    public async Task JustaCausa_PerdeProporcionais()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();

        var idContrato = await ContratoDesligadoAsync(
            cliente, sufixo, "2024-01-10", "2026-05-20", "DispensaPorJustaCausa");

        var r = (await ApurarAsync(cliente, idContrato, 10000m))!;

        Assert.True(r.Suportado);
        Assert.DoesNotContain(r.Verbas, v => v.Codigo == "FERPROP");
        Assert.DoesNotContain(r.Verbas, v => v.Codigo == "AVISO");
        Assert.DoesNotContain(r.Verbas, v => v.Codigo == "MULTAFGTS");

        // Mas as VENCIDAS continuam: ja eram direito adquirido.
        Assert.Contains(r.Verbas, v => v.Codigo == "FERVEN");
    }

    [Fact]
    public async Task Acordo_MetadeDoAvisoEVintePorCento()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();

        var idContrato = await ContratoDesligadoAsync(
            cliente, sufixo, "2024-01-10", "2026-05-20", "AcordoEntreAsPartes");

        var r = (await ApurarAsync(cliente, idContrato, 10000m))!;

        Assert.Equal(18, r.Aviso!.Dias);
        Assert.True(r.Aviso.Reduzido);
        Assert.Equal(2000.00m, r.Verbas.Single(v => v.Codigo == "MULTAFGTS").Valor);
    }

    [Theory]
    [InlineData("TerminoDeContratoPorPrazoDeterminado")]
    [InlineData("FalecimentoDoEmpregado")]
    [InlineData("Aposentadoria")]
    public async Task MotivoBloqueado_ExplicaSemCalcular(string motivo)
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();

        var idContrato = await ContratoDesligadoAsync(
            cliente, sufixo, "2024-01-10", "2026-05-20", motivo);

        var r = (await ApurarAsync(cliente, idContrato, 10000m))!;

        Assert.False(r.Suportado);
        Assert.Empty(r.Verbas);
        Assert.Equal(0m, r.Total);
        Assert.NotNull(r.MotivoDoBloqueio);

        // O CONTEXTO vem mesmo assim - quem le precisa entender o que falta.
        Assert.NotNull(r.FeriasProporcionais);
        Assert.True(r.Avos13 > 0);
        Assert.Equal(60, r.DiasFeriasVencidas);
    }

    [Fact]
    public async Task SalarioUsadoEODaDATADoDesligamento()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();

        using var respostaCargo = await cliente.PostAsJsonAsync("/api/cargos", new
        {
            codigo = $"RES{sufixo}",
            nome = $"Cargo rescisao {sufixo}",
        });
        respostaCargo.EnsureSuccessStatusCode();
        var cargo = (await respostaCargo.Content.ReadFromJsonAsync<Identificado>())!;

        using var respostaFuncionario = await cliente.PostAsJsonAsync("/api/funcionarios", new
        {
            nome = $"Rescisao Aumento {sufixo}",
            cpf = BancoPostgresFixture.CpfDeTeste(61000 + int.Parse(sufixo)),
            dataNascimento = "1982-04-17",
        });
        respostaFuncionario.EnsureSuccessStatusCode();
        var funcionario = (await respostaFuncionario.Content.ReadFromJsonAsync<Identificado>())!;

        using var respostaContrato = await cliente.PostAsJsonAsync(
            $"/api/funcionarios/{funcionario.Id}/contratos",
            new
            {
                idEmpresa = banco.IdEmpresaA,
                matricula = $"RES{sufixo}",
                dataAdmissao = "2024-01-10",
                salarioInicial = 3000m,
                idCargo = cargo.Id,
                idEstabelecimento = banco.IdEstabelecimentoA,
                jornadaMensalHoras = 220,
            });
        respostaContrato.EnsureSuccessStatusCode();
        var contrato = (await respostaContrato.Content.ReadFromJsonAsync<Identificado>())!;

        using var alteracao = await cliente.PostAsJsonAsync(
            $"/api/contratos/{contrato.Id}/vigencias",
            new
            {
                validoDe = "2026-01-01",
                salario = 6000m,
                idCargo = cargo.Id,
                idEstabelecimento = banco.IdEstabelecimentoA,
                jornadaMensalHoras = 220,
                motivo = "AlteracaoSalarial",
            });
        Assert.True(
            alteracao.IsSuccessStatusCode,
            $"O aumento precisava ser registrado: {(int)alteracao.StatusCode}");

        using var baixa = await cliente.PostAsJsonAsync(
            $"/api/contratos/{contrato.Id}/desligamento",
            new { dataDesligamento = "2026-05-20", motivo = "DispensaSemJustaCausa" });
        baixa.EnsureSuccessStatusCode();

        var r = (await ApurarAsync(cliente, contrato.Id))!;

        // O salario NOVO: a rescisao paga pela remuneracao da data da saida.
        Assert.Equal(6000.00m, r.SalarioReferencia);
        Assert.Equal(4000.00m, r.Verbas.Single(v => v.Codigo == "SALDO").Valor);
    }

    // ------------------------------------------------------------- recusas

    [Fact]
    public async Task ContratoAtivo_Devolve409()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();

        using var respostaCargo = await cliente.PostAsJsonAsync("/api/cargos", new
        {
            codigo = $"RES{sufixo}",
            nome = $"Cargo rescisao {sufixo}",
        });
        respostaCargo.EnsureSuccessStatusCode();
        var cargo = (await respostaCargo.Content.ReadFromJsonAsync<Identificado>())!;

        using var respostaFuncionario = await cliente.PostAsJsonAsync("/api/funcionarios", new
        {
            nome = $"Rescisao Ativo {sufixo}",
            cpf = BancoPostgresFixture.CpfDeTeste(62000 + int.Parse(sufixo)),
            dataNascimento = "1982-04-17",
        });
        respostaFuncionario.EnsureSuccessStatusCode();
        var funcionario = (await respostaFuncionario.Content.ReadFromJsonAsync<Identificado>())!;

        using var respostaContrato = await cliente.PostAsJsonAsync(
            $"/api/funcionarios/{funcionario.Id}/contratos",
            new
            {
                idEmpresa = banco.IdEmpresaA,
                matricula = $"RES{sufixo}",
                dataAdmissao = "2024-01-10",
                salarioInicial = 3000m,
                idCargo = cargo.Id,
                idEstabelecimento = banco.IdEstabelecimentoA,
                jornadaMensalHoras = 220,
            });
        respostaContrato.EnsureSuccessStatusCode();
        var contrato = (await respostaContrato.Content.ReadFromJsonAsync<Identificado>())!;

        using var resposta = await cliente.GetAsync($"/api/contratos/{contrato.Id}/rescisao");

        // 409 e nao 400: o pedido esta bem formado, o que falta e estado.
        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    }

    [Fact]
    public async Task ContratoInexistente_Devolve404()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);

        using var resposta = await cliente.GetAsync(
            $"/api/contratos/{Guid.CreateVersion7()}/rescisao");

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task SemAutenticacao_NaoLe()
    {
        var anonimo = _fabrica.CreateClient();

        using var resposta = await anonimo.GetAsync(
            $"/api/contratos/{Guid.CreateVersion7()}/rescisao");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    // ---------------------------------------------------------- isolamento

    [Fact]
    public async Task ContratoDeOutraOrganizacao_Devolve404()
    {
        var clienteA = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();

        var idContrato = await ContratoDesligadoAsync(
            clienteA, sufixo, "2024-01-10", "2026-05-20", "DispensaSemJustaCausa");

        var clienteB = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminB);

        using var resposta = await clienteB.GetAsync(
            $"/api/contratos/{idContrato}/rescisao?valorBaseFgts=10000");

        // 404 e nao 403: um 403 confirmaria que o contrato existe, e um valor
        // de rescisao e das informacoes mais sensiveis do produto.
        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }
}
