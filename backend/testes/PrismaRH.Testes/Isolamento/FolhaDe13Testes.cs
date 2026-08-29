using System.Net;
using System.Net.Http.Json;

namespace PrismaRH.Testes.Isolamento;

/// <summary>
/// Fase 4F ponta a ponta, contra PostgreSQL real.
///
/// O que so o sistema inteiro prova: que o adiantamento pago em novembro chega
/// a folha anual de dezembro, e que as TRES BASES do holerite anual saem
/// diferentes umas das outras. Um teste de unidade nao alcanca isso - a soma do
/// adiantamento e uma consulta ao banco, e as bases sao apuradas pelo motor.
///
/// FONTE (CLAUDE.md secao 29): **MOS eSocial S-1.3**, consolidado ate a
/// NO S-1.3 - 10.2026, itens 10.3.4 e 10.3.4.1, texto extraido do PDF oficial
/// em 29/08/2026.
///
/// Organizacao H, exclusiva: as quatro rubricas do 13o sao da ORGANIZACAO.
/// Ano EXCLUSIVO: 2039.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class FolhaDe13Testes(BancoPostgresFixture banco) : IDisposable
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
        string? Referencia, decimal Valor, int Ordem, string BasesIncidentes,
        List<LinhaMemoria> Memoria);

    private sealed record BaseApurada(string Base, decimal Valor, List<string> Composta);

    private sealed record Holerite(
        HoleriteResumo Resumo, string Competencia, string SituacaoFolha,
        List<Lancamento> Lancamentos, List<BaseApurada> Bases);

    private static int _sufixo;

    private static string Sufixo() => Interlocked.Increment(ref _sufixo).ToString("D4");

    private Task<HttpClient> AdminAsync() =>
        _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminH);

    /// <summary>
    /// O catalogo do 13o, com as incidencias do MOS S-1.3 item 10.3.4.
    ///
    /// Repetido aqui de proposito, e nao lido do semeador: se alguem trocar uma
    /// incidencia la, este teste QUEBRA - que e exatamente o que se quer de um
    /// numero que decide quanto de INSS, IRRF e FGTS a empresa recolhe.
    /// </summary>
    private static readonly (string Codigo, string Nome, string Tipo, string Estrategia, string Bases)[] Catalogo =
    [
        ("DEC13ADT", "13o adiantamento", "Provento",
            "DecimoTerceiroAdiantamento", "Fgts"),
        ("DEC13", "13o salario", "Provento",
            "DecimoTerceiroTotal", "Inss, Irrf"),
        ("DEC13ADTD", "Adiantamento ja pago", "Desconto",
            "DecimoTerceiroAdiantamentoDescontado", "Nenhuma"),
        ("DEC13FG", "Base FGTS do 13o", "Informativo",
            "DecimoTerceiroBaseFgts", "Fgts"),

        // Os tres encargos. Sem eles a organizacao nao desconta nada, e o
        // teste do INSS na folha anual passaria por ausencia de rubrica em vez
        // de por acerto - um verde pelo motivo errado.
        ("INSS", "INSS sobre a folha", "Desconto", "InssProgressivo", "Nenhuma"),
        ("FGTS", "FGTS do mes", "Informativo", "FgtsMensal", "Nenhuma"),
        ("IRRF", "IRRF sobre a folha", "Desconto", "IrrfMensal", "Nenhuma"),
    ];

    private static async Task RubricasAsync(HttpClient admin)
    {
        foreach (var (codigo, nome, tipo, estrategia, bases) in Catalogo)
        {
            using var r = await admin.PostAsJsonAsync("/api/rubricas", new
            {
                codigo,
                nome,
                tipo,
                estrategia,
                basesIncidentes = bases,
            });

            if (r.StatusCode is not (HttpStatusCode.Created or HttpStatusCode.Conflict))
            {
                r.EnsureSuccessStatusCode();
            }
        }
    }

    /// <summary>
    /// Cria um contrato na empresa H e devolve a MATRICULA.
    ///
    /// A matricula, e nao o id do contrato, porque e por ela que cada teste
    /// acha o SEU holerite. A folha de 13o inclui todo contrato com avos no
    /// ano, e os contratos que os testes anteriores criaram continuam ativos -
    /// entao a folha tem varias pessoas, como teria numa empresa de verdade.
    /// Assert.Single() aqui seria uma premissa falsa sobre a folha.
    /// </summary>
    private async Task<string> ContratadoAsync(HttpClient cliente, string sufixo, string admissao)
    {
        using var respostaCargo = await cliente.PostAsJsonAsync("/api/cargos", new
        {
            codigo = $"F13{sufixo}",
            nome = $"Cargo folha 13 {sufixo}",
        });
        respostaCargo.EnsureSuccessStatusCode();
        var cargo = (await respostaCargo.Content.ReadFromJsonAsync<Identificado>())!;

        using var respostaFuncionario = await cliente.PostAsJsonAsync("/api/funcionarios", new
        {
            nome = $"Folha Treze {sufixo}",
            cpf = BancoPostgresFixture.CpfDeTeste(81000 + int.Parse(sufixo)),
            dataNascimento = "1986-07-21",
        });
        respostaFuncionario.EnsureSuccessStatusCode();
        var funcionario = (await respostaFuncionario.Content.ReadFromJsonAsync<Identificado>())!;

        using var respostaContrato = await cliente.PostAsJsonAsync(
            $"/api/funcionarios/{funcionario.Id}/contratos",
            new
            {
                idEmpresa = banco.IdEmpresaH,
                matricula = $"F13{sufixo}",
                dataAdmissao = admissao,
                salarioInicial = 3000m,
                idCargo = cargo.Id,
                idEstabelecimento = banco.IdEstabelecimentoH,
                jornadaMensalHoras = 220,
            });
        respostaContrato.EnsureSuccessStatusCode();

        return $"F13{sufixo}";
    }

    private async Task<FolhaDetalhe> CalcularAsync(
        HttpClient admin, string competencia, string tipo)
    {
        using var abertura = await admin.PostAsJsonAsync("/api/folhas", new
        {
            idEmpresa = banco.IdEmpresaH,
            competencia,
            tipo,
        });
        abertura.EnsureSuccessStatusCode();
        var folha = (await abertura.Content.ReadFromJsonAsync<FolhaResumo>())!;

        Assert.Equal(tipo, folha.Tipo);

        using var calculo = await admin.PostAsync($"/api/folhas/{folha.Id}/calcular", null);
        calculo.EnsureSuccessStatusCode();

        return (await calculo.Content.ReadFromJsonAsync<FolhaDetalhe>())!;
    }

    private static Task<Holerite?> HoleriteAsync(HttpClient c, Guid idFolha, Guid idHolerite) =>
        c.GetFromJsonAsync<Holerite>($"/api/folhas/{idFolha}/funcionarios/{idHolerite}");

    /// <summary>O holerite DESTE teste, achado pela matricula.</summary>
    private static HoleriteResumo Meu(FolhaDetalhe folha, string matricula) =>
        folha.Funcionarios.Single(f => f.Matricula == matricula);

    // ------------------------------------------------------------ catalogo

    [Fact]
    public async Task AsQuatroRubricas_TemTiposEIncidenciasDIFERENTES()
    {
        var admin = await AdminAsync();
        await RubricasAsync(admin);

        var rubricas = await admin.GetFromJsonAsync<List<RubricaItem>>("/api/rubricas");

        var adiantamento = rubricas!.Single(r => r.Codigo == "DEC13ADT" && r.Ativa);
        var total = rubricas.Single(r => r.Codigo == "DEC13" && r.Ativa);
        var compensacao = rubricas.Single(r => r.Codigo == "DEC13ADTD" && r.Ativa);
        var baseFgts = rubricas.Single(r => r.Codigo == "DEC13FG" && r.Ativa);

        // MOS S-1.3, 10.3.4: o adiantamento tem FGTS e SO ele.
        Assert.Equal("Fgts", adiantamento.BasesIncidentes);

        // O total tem INSS e IRRF - e NAO tem FGTS. Se tivesse, o Fundo seria
        // recolhido sobre o 13o inteiro e o adiantamento pagaria duas vezes.
        Assert.Equal("Inss, Irrf", total.BasesIncidentes);

        // Desconto nao compoe base (invariante da Fase 4A).
        Assert.Equal("Nenhuma", compensacao.BasesIncidentes);
        Assert.Equal("Desconto", compensacao.Tipo);

        // A informativa e quem carrega a base de FGTS da diferenca.
        Assert.Equal("Fgts", baseFgts.BasesIncidentes);
        Assert.Equal("Informativo", baseFgts.Tipo);
    }

    [Fact]
    public async Task RubricaDaBaseDeFgts_ComoProvento_ERecusada()
    {
        var admin = await AdminAsync();

        using var r = await admin.PostAsJsonAsync("/api/rubricas", new
        {
            codigo = $"XFG{Sufixo()}",
            nome = "Base FGTS errada",
            tipo = "Provento",
            estrategia = "DecimoTerceiroBaseFgts",
            basesIncidentes = "Fgts",
        });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    // ------------------------------------------------------- adiantamento

    [Fact]
    public async Task Adiantamento_SoTemFgts_SemInssESemIrrf()
    {
        var admin = await AdminAsync();
        await RubricasAsync(admin);
        var matricula = await ContratadoAsync(admin, Sufixo(), "2030-01-10");

        var folha = await CalcularAsync(admin, "11/2039", "DecimoTerceiroAdiantamento");

        var resumo = Meu(folha, matricula);
        var holerite = (await HoleriteAsync(admin, folha.Folha.Id, resumo.Id))!;

        // 3.000 x 12/12 = 3.000; metade = 1.500.
        var adiantamento = holerite.Lancamentos.Single(l => l.CodigoRubrica == "DEC13ADT");
        Assert.Equal(1500.00m, adiantamento.Valor);
        Assert.Equal("12/12", adiantamento.Referencia);

        // A prova do MOS 10.3.4: "um adiantamento feito em novembro tem
        // incidencia de FGTS, mas nao de CP ou IRRF".
        Assert.Equal(1500.00m, holerite.Bases.Single(b => b.Base == "Fgts").Valor);
        Assert.Equal(0m, holerite.Bases.Single(b => b.Base == "Inss").Valor);
        Assert.Equal(0m, holerite.Bases.Single(b => b.Base == "Irrf").Valor);

        // E, portanto, NADA e descontado de INSS nem de IRRF.
        //
        // O teste afirma VALOR ZERO, e nao ausencia da linha: desde a Fase 4B o
        // motor lanca o encargo mesmo quando a base e zero, em toda folha. Uma
        // linha de R$ 0,00 num holerite de adiantamento e ruido - anotado no
        // Security Gate desta fase -, mas nao e dinheiro errado, e suprimi-la
        // mudaria o comportamento das folhas das fases anteriores.
        Assert.Equal(0m, holerite.Lancamentos.Single(l => l.CodigoRubrica == "INSS").Valor);
        Assert.Equal(0m, holerite.Lancamentos.Single(l => l.CodigoRubrica == "IRRF").Valor);

        // O liquido, que e o que a pessoa recebe, e o adiantamento inteiro.
        Assert.Equal(1500.00m, resumo.Liquido);
    }

    // -------------------------------------------------------- folha anual

    [Fact]
    public async Task FolhaAnual_TemTRESBasesDIFERENTES()
    {
        var admin = await AdminAsync();
        await RubricasAsync(admin);
        var matricula = await ContratadoAsync(admin, Sufixo(), "2030-02-05");

        // Primeiro o adiantamento de novembro...
        await CalcularAsync(admin, "11/2040", "DecimoTerceiroAdiantamento");

        // ...e agora a anual, que precisa ENXERGAR aquele adiantamento.
        var anual = await CalcularAsync(admin, "12/2040", "DecimoTerceiro");

        var resumo = Meu(anual, matricula);
        var holerite = (await HoleriteAsync(admin, anual.Folha.Id, resumo.Id))!;

        var total = holerite.Lancamentos.Single(l => l.CodigoRubrica == "DEC13");
        var compensado = holerite.Lancamentos.Single(l => l.CodigoRubrica == "DEC13ADTD");
        var baseFgts = holerite.Lancamentos.Single(l => l.CodigoRubrica == "DEC13FG");

        Assert.Equal(3000.00m, total.Valor);
        Assert.Equal(1500.00m, compensado.Valor);   // veio da folha de novembro
        Assert.Equal(1500.00m, baseFgts.Valor);

        // O CORACAO DA FASE 4F: tres bases, tres numeros diferentes.
        //   INSS -> total          (MOS 10.3.4)
        //   IRRF -> total          (MOS 10.3.4)
        //   FGTS -> so a diferenca (MOS 10.3.4)
        Assert.Equal(3000.00m, holerite.Bases.Single(b => b.Base == "Inss").Valor);
        Assert.Equal(3000.00m, holerite.Bases.Single(b => b.Base == "Irrf").Valor);
        Assert.Equal(1500.00m, holerite.Bases.Single(b => b.Base == "Fgts").Valor);
    }

    [Fact]
    public async Task FolhaAnual_DescontaINSS_QueNaoExistiaNoAdiantamento()
    {
        var admin = await AdminAsync();
        await RubricasAsync(admin);
        var matricula = await ContratadoAsync(admin, Sufixo(), "2030-03-01");

        await CalcularAsync(admin, "11/2041", "DecimoTerceiroAdiantamento");
        var anual = await CalcularAsync(admin, "12/2041", "DecimoTerceiro");

        var resumo = Meu(anual, matricula);
        var holerite = (await HoleriteAsync(admin, anual.Folha.Id, resumo.Id))!;

        // MOS 10.3.4: "A apuracao da CP e do IRRF (...) e feita apenas na folha
        // de 13o (anual)". Aqui ela aparece, e sobre o TOTAL.
        var inss = holerite.Lancamentos.Single(l => l.CodigoRubrica == "INSS");
        Assert.True(inss.Valor > 0m);
        Assert.Equal("Desconto", inss.Tipo);
    }

    [Fact]
    public async Task FolhaAnual_LiquidoDescontaOAdiantamentoEOsEncargos()
    {
        var admin = await AdminAsync();
        await RubricasAsync(admin);
        var matricula = await ContratadoAsync(admin, Sufixo(), "2030-04-01");

        await CalcularAsync(admin, "11/2042", "DecimoTerceiroAdiantamento");
        var anual = await CalcularAsync(admin, "12/2042", "DecimoTerceiro");

        var resumo = Meu(anual, matricula);

        // Roteiro do MOS 10.3.4.1: vencimento = total; descontos = adiantamento
        // + contribuicao previdenciaria.
        Assert.Equal(3000.00m, resumo.TotalProventos);
        Assert.True(resumo.TotalDescontos > 1500.00m);   // adiantamento + INSS
        Assert.Equal(resumo.TotalProventos - resumo.TotalDescontos, resumo.Liquido);

        // A INFORMATIVA nao entra no liquido - so na base.
        Assert.True(resumo.Liquido < 1500.00m);
    }

    [Fact]
    public async Task SemAdiantamento_ABaseDeFgtsEO13Inteiro()
    {
        var admin = await AdminAsync();
        await RubricasAsync(admin);
        var matricula = await ContratadoAsync(admin, Sufixo(), "2030-05-02");

        // Nenhuma folha de adiantamento neste ano.
        var anual = await CalcularAsync(admin, "12/2043", "DecimoTerceiro");

        var resumo = Meu(anual, matricula);
        var holerite = (await HoleriteAsync(admin, anual.Folha.Id, resumo.Id))!;

        Assert.DoesNotContain(holerite.Lancamentos, l => l.CodigoRubrica == "DEC13ADTD");
        Assert.Equal(3000.00m, holerite.Bases.Single(b => b.Base == "Fgts").Valor);
    }

    // ------------------------------------------------------------ elegibilidade

    [Fact]
    public async Task AdmitidoEmOutubro_TemAvosPROPORCIONAIS()
    {
        var admin = await AdminAsync();
        await RubricasAsync(admin);
        var matricula = await ContratadoAsync(admin, Sufixo(), "2044-10-05");

        var anual = await CalcularAsync(admin, "12/2044", "DecimoTerceiro");

        var resumo = Meu(anual, matricula);

        // out (27 dias), nov, dez = 3 avos.
        Assert.Equal(3, resumo.Avos);
        Assert.Equal(750.00m, resumo.TotalProventos);   // 3.000 x 3/12
    }

    [Fact]
    public async Task AdmitidoEmDezembroComPoucosDias_NAOEntraNaFolha()
    {
        var admin = await AdminAsync();
        await RubricasAsync(admin);
        var matricula = await ContratadoAsync(admin, Sufixo(), "2045-12-28");

        var anual = await CalcularAsync(admin, "12/2045", "DecimoTerceiro");

        // 4 dias em dezembro: menos que os 15 da Lei 4.090. Zero avos, e quem
        // tem zero avo nao aparece - um holerite de R$ 0,00 pareceria defeito.
        Assert.DoesNotContain(anual.Funcionarios, f => f.Matricula == matricula);
    }

    // ------------------------------------------------------------- recusas

    [Fact]
    public async Task SemAsQuatroRubricas_OCalculoRECUSA()
    {
        // Organizacao G nao tem as rubricas do 13o - ela e da rescisao.
        var outra = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminG);

        using var abertura = await outra.PostAsJsonAsync("/api/folhas", new
        {
            idEmpresa = banco.IdEmpresaG,
            competencia = "12/2046",
            tipo = "DecimoTerceiro",
        });
        abertura.EnsureSuccessStatusCode();
        var folha = (await abertura.Content.ReadFromJsonAsync<FolhaResumo>())!;

        using var calculo = await outra.PostAsync($"/api/folhas/{folha.Id}/calcular", null);

        // 409, e nao um holerite incompleto: faltando a informativa DEC13FG a
        // folha fecharia certa no liquido e recolheria FGTS a MENOS.
        Assert.Equal(HttpStatusCode.Conflict, calculo.StatusCode);
        Assert.Contains("DecimoTerceiroBaseFgts", await calculo.Content.ReadAsStringAsync());
    }

    // --------------------------------------------------------- isolamento

    [Fact]
    public async Task FolhaDe13DaH_NaoApareceNaG()
    {
        var admin = await AdminAsync();
        await RubricasAsync(admin);
        var matricula = await ContratadoAsync(admin, Sufixo(), "2030-06-01");

        var anual = await CalcularAsync(admin, "12/2047", "DecimoTerceiro");

        var outra = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminG);

        using var resposta = await outra.GetAsync($"/api/folhas/{anual.Folha.Id}");

        // 404, e nao 403: um 403 confirmaria que a folha existe.
        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task Anonimo_NaoCalculaFolhaDe13()
    {
        var anonimo = _fabrica.CreateClient();

        using var r = await anonimo.PostAsync($"/api/folhas/{Guid.CreateVersion7()}/calcular", null);

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    // ------------------------------------------------------------- memoria

    [Fact]
    public async Task AMemoriaMostraDeOndeCadaNumeroVEIO()
    {
        var admin = await AdminAsync();
        await RubricasAsync(admin);
        var matricula = await ContratadoAsync(admin, Sufixo(), "2030-07-01");

        await CalcularAsync(admin, "11/2048", "DecimoTerceiroAdiantamento");
        var anual = await CalcularAsync(admin, "12/2048", "DecimoTerceiro");

        var resumo = Meu(anual, matricula);
        var holerite = (await HoleriteAsync(admin, anual.Folha.Id, resumo.Id))!;

        var baseFgts = holerite.Lancamentos.Single(l => l.CodigoRubrica == "DEC13FG");

        // A conta da diferenca precisa estar visivel: e o numero mais dificil
        // de conferir a mao de toda a folha anual.
        Assert.Contains(baseFgts.Memoria, m => m.Descricao.Contains("nao tributada"));
        Assert.Equal(1500.00m, baseFgts.Memoria.Last().Valor);
    }
}
