using System.Net;
using System.Net.Http.Json;

namespace PrismaRH.Testes.Isolamento;

/// <summary>
/// Fase 4E etapa 2b ponta a ponta, contra PostgreSQL real.
///
/// Os testes de dominio provam a aritmetica; estes provam o que so o sistema
/// inteiro prova: que a concessao vira holerite, que o salario usado e o da
/// DATA DA CONCESSAO (CLT art. 142), e que as quatro incidencias do eSocial
/// chegam ate as bases.
///
/// FONTE das incidencias: Manual do eSocial, tabela de rubricas e bases de
/// calculo, informada pelo responsavel pelo projeto em 28/08/2026.
///
/// Organizacao F, exclusiva: as rubricas de ferias sao da ORGANIZACAO, e
/// liga-las na A mudaria as folhas mensais dos testes das Fases 3 e 4.
/// Competencias EXCLUSIVAS: 01/2035 a 06/2035.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class FolhaDeFeriasTestes(BancoPostgresFixture banco) : IDisposable
{
    private readonly FabricaApiIsolada _fabrica = new(banco.StringConexao);

    public void Dispose() => _fabrica.Dispose();

    private sealed record Identificado(Guid Id);

    private sealed record RubricaItem(
        Guid Id, string Codigo, string Nome, string Tipo, string Estrategia,
        string BasesIncidentes, bool Ativa);

    private sealed record HoleriteResumo(
        Guid Id, Guid IdFuncionario, string Funcionario, string Matricula,
        int Avos, int Divisor, decimal SalarioReferencia,
        decimal TotalProventos, decimal TotalDescontos, decimal Liquido);

    private sealed record FolhaResumo(
        Guid Id, Guid IdEmpresa, string Empresa, string Competencia, string Tipo,
        string Situacao, int VersaoCalculo, int QuantidadeFuncionarios,
        decimal TotalProventos, decimal TotalDescontos, decimal TotalLiquido);

    private sealed record FolhaDetalhe(FolhaResumo Folha, List<HoleriteResumo> Funcionarios);

    private sealed record LinhaMemoria(int Ordem, string Descricao, string Expressao, decimal Valor);

    private sealed record Lancamento(
        Guid Id, string CodigoRubrica, string NomeRubrica, string Tipo, string Origem,
        string? Referencia, decimal Valor, int Ordem, string BasesIncidentes, List<LinhaMemoria> Memoria);

    private sealed record BaseApurada(string Base, decimal Valor, List<string> Composta);

    private sealed record Holerite(
        HoleriteResumo Resumo, string Competencia, string SituacaoFolha,
        List<Lancamento> Lancamentos, List<BaseApurada> Bases);

    private static int _sufixo;

    private static string Sufixo() => Interlocked.Increment(ref _sufixo).ToString("D4");

    private Task<HttpClient> AdminAsync() =>
        _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminF);

    /// <summary>
    /// As quatro rubricas de ferias, com as incidencias do eSocial.
    ///
    /// Repetidas aqui de proposito, e nao lidas da semeadura: se o semeador
    /// mudar, este teste precisa falhar em vez de acompanhar em silencio.
    /// </summary>
    private static readonly (string Codigo, string Estrategia, string Bases)[] Catalogo =
    [
        ("FER", "FeriasGozadas", "Inss, Fgts, Irrf"),
        ("FER13", "TercoFerias", "Inss, Fgts, Irrf"),
        ("ABONO", "AbonoPecuniario", "Irrf"),
        ("ABN13", "TercoAbono", "Irrf"),
    ];

    private static async Task RubricasDeFeriasAsync(HttpClient admin)
    {
        foreach (var (codigo, estrategia, bases) in Catalogo)
        {
            using var resposta = await admin.PostAsJsonAsync("/api/rubricas", new
            {
                codigo,
                nome = codigo,
                tipo = "Provento",
                estrategia,
                basesIncidentes = bases,
            });

            // Ja existir e normal: os testes desta classe compartilham a
            // organizacao.
            if (resposta.StatusCode is not (HttpStatusCode.Created or HttpStatusCode.Conflict))
            {
                resposta.EnsureSuccessStatusCode();
            }
        }
    }

    private async Task<Guid> ContratoAsync(HttpClient cliente, string sufixo, decimal salario)
    {
        using var respostaCargo = await cliente.PostAsJsonAsync("/api/cargos", new
        {
            codigo = $"FF{sufixo}",
            nome = $"Cargo folha ferias {sufixo}",
        });
        respostaCargo.EnsureSuccessStatusCode();
        var cargo = (await respostaCargo.Content.ReadFromJsonAsync<Identificado>())!;

        using var respostaFuncionario = await cliente.PostAsJsonAsync("/api/funcionarios", new
        {
            nome = $"Folha Ferias {sufixo}",
            cpf = BancoPostgresFixture.CpfDeTeste(40000 + int.Parse(sufixo)),
            dataNascimento = "1984-06-19",
        });
        respostaFuncionario.EnsureSuccessStatusCode();
        var funcionario = (await respostaFuncionario.Content.ReadFromJsonAsync<Identificado>())!;

        using var respostaContrato = await cliente.PostAsJsonAsync(
            $"/api/funcionarios/{funcionario.Id}/contratos",
            new
            {
                idEmpresa = banco.IdEmpresaF,
                matricula = $"FF{sufixo}",
                dataAdmissao = "2020-01-01",
                salarioInicial = salario,
                idCargo = cargo.Id,
                idEstabelecimento = banco.IdEstabelecimentoF,
                jornadaMensalHoras = 220,
            });
        respostaContrato.EnsureSuccessStatusCode();

        return (await respostaContrato.Content.ReadFromJsonAsync<Identificado>())!.Id;
    }

    private static async Task ConcederAsync(
        HttpClient cliente, Guid idContrato, string periodo, string inicio, int dias, int abono)
    {
        using var resposta = await cliente.PostAsJsonAsync(
            $"/api/contratos/{idContrato}/ferias/concessoes",
            new
            {
                inicioPeriodoAquisitivo = periodo,
                inicio,
                dias,
                diasAbonoPecuniario = abono,
            });
        resposta.EnsureSuccessStatusCode();
    }

    private async Task<FolhaDetalhe> CalcularFeriasAsync(HttpClient admin, string competencia)
    {
        using var abertura = await admin.PostAsJsonAsync("/api/folhas", new
        {
            idEmpresa = banco.IdEmpresaF,
            competencia,
            tipo = "Ferias",
        });
        abertura.EnsureSuccessStatusCode();
        var folha = (await abertura.Content.ReadFromJsonAsync<FolhaResumo>())!;

        Assert.Equal("Ferias", folha.Tipo);

        using var calculo = await admin.PostAsync($"/api/folhas/{folha.Id}/calcular", null);
        calculo.EnsureSuccessStatusCode();

        return (await calculo.Content.ReadFromJsonAsync<FolhaDetalhe>())!;
    }

    private static Task<Holerite?> HoleriteAsync(HttpClient cliente, Guid idFolha, Guid idHolerite) =>
        cliente.GetFromJsonAsync<Holerite>($"/api/folhas/{idFolha}/funcionarios/{idHolerite}");

    // ------------------------------------------------------------- catalogo

    [Fact]
    public async Task AsQuatroRubricas_TemIncidenciasDIFERENTES()
    {
        var admin = await AdminAsync();
        await RubricasDeFeriasAsync(admin);

        var rubricas = await admin.PaginaDe<RubricaItem>("/api/rubricas");

        foreach (var (codigo, estrategia, bases) in Catalogo)
        {
            var r = rubricas!.Single(x => x.Estrategia == estrategia && x.Ativa);

            Assert.Equal(codigo, r.Codigo);
            Assert.Equal("Provento", r.Tipo);
            Assert.Equal(bases, r.BasesIncidentes);
        }
    }

    [Fact]
    public async Task RubricaDeFerias_ComoDesconto_ERecusada()
    {
        var admin = await AdminAsync();

        using var resposta = await admin.PostAsJsonAsync("/api/rubricas", new
        {
            codigo = $"FX{Sufixo()}",
            nome = "Ferias errada",
            tipo = "Desconto",
            estrategia = "FeriasGozadas",
            basesIncidentes = "Nenhuma",
        });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    // -------------------------------------------------------- tipo de folha

    [Fact]
    public async Task MensalEFerias_ConvivemNaMesmaCompetencia()
    {
        var admin = await AdminAsync();

        // O indice unico passou a incluir o tipo na Fase 4E. Antes dela, a
        // segunda abertura seria recusada.
        using var mensal = await admin.PostAsJsonAsync("/api/folhas", new
        {
            idEmpresa = banco.IdEmpresaF,
            competencia = "06/2035",
            tipo = "Mensal",
        });
        mensal.EnsureSuccessStatusCode();

        using var ferias = await admin.PostAsJsonAsync("/api/folhas", new
        {
            idEmpresa = banco.IdEmpresaF,
            competencia = "06/2035",
            tipo = "Ferias",
        });
        Assert.Equal(HttpStatusCode.Created, ferias.StatusCode);

        // Mas duas do MESMO tipo continuam recusadas.
        using var repetida = await admin.PostAsJsonAsync("/api/folhas", new
        {
            idEmpresa = banco.IdEmpresaF,
            competencia = "06/2035",
            tipo = "Ferias",
        });
        Assert.Equal(HttpStatusCode.Conflict, repetida.StatusCode);
    }

    [Fact]
    public async Task TipoDesconhecido_ERecusado()
    {
        var admin = await AdminAsync();

        using var resposta = await admin.PostAsJsonAsync("/api/folhas", new
        {
            idEmpresa = banco.IdEmpresaF,
            competencia = "05/2035",
            tipo = "Decimo Terceiro Que Nao Existe",
        });

        Assert.False(resposta.IsSuccessStatusCode);
    }

    // ------------------------------------------------------------ pagamento

    [Fact]
    public async Task Holerite_TemAsQuatroParcelas_ComAsBasesCertas()
    {
        var admin = await AdminAsync();
        var sufixo = Sufixo();

        await RubricasDeFeriasAsync(admin);
        var idContrato = await ContratoAsync(admin, sufixo, 3000.00m);

        // 20 dias de gozo + 10 vendidos, do periodo 01/01/2024 a 31/12/2024.
        await ConcederAsync(admin, idContrato, "2024-01-01", "2035-01-06", 20, 10);

        var detalhe = await CalcularFeriasAsync(admin, "01/2035");
        var meu = detalhe.Funcionarios.Single(f => f.Matricula == $"FF{sufixo}");
        var holerite = (await HoleriteAsync(admin, detalhe.Folha.Id, meu.Id))!;

        decimal ValorDe(string codigo) =>
            holerite.Lancamentos.Single(l => l.CodigoRubrica == codigo).Valor;

        // Conferido a mao: 3.000 / 30 = 100 por dia.
        Assert.Equal(2000.00m, ValorDe("FER"));     // 100 x 20
        Assert.Equal(666.67m, ValorDe("FER13"));    // 2.000 / 3
        Assert.Equal(1000.00m, ValorDe("ABONO"));   // 100 x 10
        Assert.Equal(333.33m, ValorDe("ABN13"));    // 1.000 / 3

        Assert.Equal(4000.00m, holerite.Resumo.TotalProventos);

        // As BASES sao o ponto deste teste. Fonte: Manual do eSocial.
        //
        // INSS e FGTS: ferias gozadas (2.000) + terco (666,67) = 2.666,67.
        // O abono e o terco dele ficam de fora.
        Assert.Equal(2666.67m, holerite.Bases.Single(b => b.Base == "Inss").Valor);
        Assert.Equal(2666.67m, holerite.Bases.Single(b => b.Base == "Fgts").Valor);

        // IRRF: as QUATRO parcelas. 4.000,00.
        Assert.Equal(4000.00m, holerite.Bases.Single(b => b.Base == "Irrf").Valor);

        // E a memoria mostra a conta.
        var ferias = holerite.Lancamentos.Single(l => l.CodigoRubrica == "FER");
        Assert.Equal("20/30", ferias.Referencia);
        Assert.Equal("3.000,00 / 30 x 20", ferias.Memoria[1].Expressao);
    }

    [Fact]
    public async Task SemAbono_AsBasesSaoTodasIguais()
    {
        var admin = await AdminAsync();
        var sufixo = Sufixo();

        await RubricasDeFeriasAsync(admin);
        var idContrato = await ContratoAsync(admin, sufixo, 3000.00m);

        await ConcederAsync(admin, idContrato, "2024-01-01", "2035-02-05", 30, 0);

        var detalhe = await CalcularFeriasAsync(admin, "02/2035");
        var meu = detalhe.Funcionarios.Single(f => f.Matricula == $"FF{sufixo}");
        var holerite = (await HoleriteAsync(admin, detalhe.Folha.Id, meu.Id))!;

        // 3.000 + 1.000 de terco. Sem abono, as tres bases coincidem.
        Assert.Equal(4000.00m, holerite.Resumo.TotalProventos);
        Assert.All(holerite.Bases, b => Assert.Equal(4000.00m, b.Valor));
    }

    [Fact]
    public async Task SalarioUsadoEODaDATADaConcessao_NaoODeHoje()
    {
        var admin = await AdminAsync();
        var sufixo = Sufixo();

        await RubricasDeFeriasAsync(admin);
        var idContrato = await ContratoAsync(admin, sufixo, 3000.00m);

        // Aumento a partir de 01/01/2035: o gozo comeca depois dele.
        using var alteracao = await admin.PostAsJsonAsync(
            $"/api/contratos/{idContrato}/vigencias",
            new
            {
                validoDe = "2035-01-01",
                salario = 6000.00m,
                idCargo = (await admin.PaginaDe<Identificado>("/api/cargos"))![0].Id,
                idEstabelecimento = banco.IdEstabelecimentoF,
                jornadaMensalHoras = 220,
                motivo = "AlteracaoSalarial",
            });

        // Sem o aumento registrado o teste nao prova nada: ele passaria porque
        // o salario nunca mudou, e nao porque a data certa foi usada.
        Assert.True(
            alteracao.IsSuccessStatusCode,
            $"O aumento precisava ser registrado: {(int)alteracao.StatusCode} "
            + await alteracao.Content.ReadAsStringAsync());

        await ConcederAsync(admin, idContrato, "2024-01-01", "2035-03-03", 30, 0);

        var detalhe = await CalcularFeriasAsync(admin, "03/2035");
        var meu = detalhe.Funcionarios.Single(f => f.Matricula == $"FF{sufixo}");
        var holerite = (await HoleriteAsync(admin, detalhe.Folha.Id, meu.Id))!;

        // CLT art. 142: a remuneracao devida na data da CONCESSAO - o salario
        // novo, e nao o de quando o periodo aquisitivo correu.
        Assert.Equal(6000.00m, holerite.Lancamentos.Single(l => l.CodigoRubrica == "FER").Valor);
    }

    [Fact]
    public async Task QuemNaoSaiDeFerias_NaoEntraNaFolha()
    {
        var admin = await AdminAsync();
        var sufixo = Sufixo();

        await RubricasDeFeriasAsync(admin);
        await ContratoAsync(admin, sufixo, 3000.00m);

        // Contrato sem concessao na competencia: a folha de ferias sai vazia
        // para ele. Uma folha de ferias so tem quem sai de ferias.
        var detalhe = await CalcularFeriasAsync(admin, "04/2035");

        Assert.DoesNotContain(detalhe.Funcionarios, f => f.Matricula == $"FF{sufixo}");
    }

    [Fact]
    public async Task SemAsRubricasCadastradas_ORecusaExplica()
    {
        // Organizacao B nao tem rubrica de ferias alguma.
        var adminB = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminB);

        using var abertura = await adminB.PostAsJsonAsync("/api/folhas", new
        {
            idEmpresa = banco.IdEmpresaB,
            competencia = "01/2035",
            tipo = "Ferias",
        });
        abertura.EnsureSuccessStatusCode();
        var folha = (await abertura.Content.ReadFromJsonAsync<FolhaResumo>())!;

        using var calculo = await adminB.PostAsync($"/api/folhas/{folha.Id}/calcular", null);

        // 409 e nao 400: o pedido esta bem formado, o que falta e estado.
        Assert.Equal(HttpStatusCode.Conflict, calculo.StatusCode);

        var corpo = await calculo.Content.ReadAsStringAsync();
        Assert.Contains("Faltam rubricas de ferias", corpo);
    }

    // ------------------------------------------------------- multiempresa

    [Fact]
    public async Task FolhaDeFeriasDeOutraOrganizacao_NaoAparece()
    {
        var admin = await AdminAsync();
        var sufixo = Sufixo();

        await RubricasDeFeriasAsync(admin);
        var idContrato = await ContratoAsync(admin, sufixo, 3000.00m);
        await ConcederAsync(admin, idContrato, "2024-01-01", "2035-05-05", 30, 0);

        var detalhe = await CalcularFeriasAsync(admin, "05/2035");

        var adminB = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminB);

        using var resposta = await adminB.GetAsync($"/api/folhas/{detalhe.Folha.Id}");

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }
}
