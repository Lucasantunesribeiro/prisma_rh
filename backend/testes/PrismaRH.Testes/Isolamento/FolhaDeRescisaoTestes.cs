using System.Net;
using System.Net.Http.Json;

namespace PrismaRH.Testes.Isolamento;

/// <summary>
/// Fase 4G etapa 3 ponta a ponta, contra PostgreSQL real.
///
/// O que so o sistema inteiro prova: que as verbas viram holerite, e que as
/// INCIDENCIAS de cada uma chegam ate as bases - que e onde um erro custa
/// dinheiro.
///
/// FONTE das incidencias: tabela de rubricas do eSocial vigente em 2026,
/// informada pelo responsavel pelo projeto em 29/08/2026.
///
/// Organizacao G, exclusiva: as nove rubricas de rescisao sao da ORGANIZACAO.
/// Competencias EXCLUSIVAS: 01/2037 a 06/2037.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class FolhaDeRescisaoTestes(BancoPostgresFixture banco) : IDisposable
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

    private sealed record FolhaDetalhe(
        FolhaResumo Folha, List<HoleriteResumo> Funcionarios, List<Guid>? ContratosIgnorados);

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
        _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminG);

    /// <summary>
    /// As nove rubricas com as incidencias do eSocial.
    ///
    /// Repetidas aqui de proposito, e nao lidas do semeador: se ele mudar,
    /// este teste precisa FALHAR em vez de acompanhar em silencio.
    /// </summary>
    private static readonly (string Codigo, string Bases)[] Catalogo =
    [
        ("SALDO", "Inss, Fgts, Irrf"),
        ("AVISO", "Fgts"),
        ("FERVEN", "Nenhuma"),
        ("FERVEN13", "Nenhuma"),
        ("FERPROP", "Nenhuma"),
        ("FERPROP13", "Nenhuma"),
        ("DEC13PROP", "Inss, Fgts, Irrf"),
        ("DEC13AV", "Inss, Fgts"),
        ("MULTAFGTS", "Nenhuma"),
    ];

    private static async Task RubricasAsync(HttpClient admin)
    {
        foreach (var (codigo, bases) in Catalogo)
        {
            using var r = await admin.PostAsJsonAsync("/api/rubricas", new
            {
                codigo,
                nome = codigo,
                tipo = "Provento",
                estrategia = "VerbaRescisoria",
                basesIncidentes = bases,
            });

            if (r.StatusCode is not (HttpStatusCode.Created or HttpStatusCode.Conflict))
            {
                r.EnsureSuccessStatusCode();
            }
        }
    }

    private async Task<Guid> DesligadoAsync(
        HttpClient cliente, string sufixo, string desligamento, string motivo, decimal? baseFgts)
    {
        using var respostaCargo = await cliente.PostAsJsonAsync("/api/cargos", new
        {
            codigo = $"FR{sufixo}",
            nome = $"Cargo folha rescisao {sufixo}",
        });
        respostaCargo.EnsureSuccessStatusCode();
        var cargo = (await respostaCargo.Content.ReadFromJsonAsync<Identificado>())!;

        using var respostaFuncionario = await cliente.PostAsJsonAsync("/api/funcionarios", new
        {
            nome = $"Folha Rescisao {sufixo}",
            cpf = BancoPostgresFixture.CpfDeTeste(70000 + int.Parse(sufixo)),
            dataNascimento = "1981-03-09",
        });
        respostaFuncionario.EnsureSuccessStatusCode();
        var funcionario = (await respostaFuncionario.Content.ReadFromJsonAsync<Identificado>())!;

        using var respostaContrato = await cliente.PostAsJsonAsync(
            $"/api/funcionarios/{funcionario.Id}/contratos",
            new
            {
                idEmpresa = banco.IdEmpresaG,
                matricula = $"FR{sufixo}",
                dataAdmissao = "2024-01-10",
                salarioInicial = 3000m,
                idCargo = cargo.Id,
                idEstabelecimento = banco.IdEstabelecimentoG,
                jornadaMensalHoras = 220,
            });
        respostaContrato.EnsureSuccessStatusCode();
        var contrato = (await respostaContrato.Content.ReadFromJsonAsync<Identificado>())!;

        using var baixa = await cliente.PostAsJsonAsync(
            $"/api/contratos/{contrato.Id}/desligamento",
            new { dataDesligamento = desligamento, motivo });
        baixa.EnsureSuccessStatusCode();

        if (baseFgts is { } valor)
        {
            using var informar = await cliente.PutAsJsonAsync(
                $"/api/contratos/{contrato.Id}/rescisao/valor-base-fgts",
                new { valor, observacao = "Extrato da Caixa" });
            informar.EnsureSuccessStatusCode();
        }

        return contrato.Id;
    }

    private async Task<FolhaDetalhe> CalcularAsync(HttpClient admin, string competencia)
    {
        using var abertura = await admin.PostAsJsonAsync("/api/folhas", new
        {
            idEmpresa = banco.IdEmpresaG,
            competencia,
            tipo = "Rescisao",
        });
        abertura.EnsureSuccessStatusCode();
        var folha = (await abertura.Content.ReadFromJsonAsync<FolhaResumo>())!;

        Assert.Equal("Rescisao", folha.Tipo);

        using var calculo = await admin.PostAsync($"/api/folhas/{folha.Id}/calcular", null);
        calculo.EnsureSuccessStatusCode();

        return (await calculo.Content.ReadFromJsonAsync<FolhaDetalhe>())!;
    }

    private static Task<Holerite?> HoleriteAsync(HttpClient c, Guid idFolha, Guid idHolerite) =>
        c.GetFromJsonAsync<Holerite>($"/api/folhas/{idFolha}/funcionarios/{idHolerite}");

    // ------------------------------------------------------------ valor base

    [Fact]
    public async Task ValorBase_VaiNoCORPO_DeUmPut()
    {
        var admin = await AdminAsync();
        var sufixo = Sufixo();
        var idContrato = await DesligadoAsync(admin, sufixo, "2037-01-20", "DispensaSemJustaCausa", null);

        using var primeira = await admin.PutAsJsonAsync(
            $"/api/contratos/{idContrato}/rescisao/valor-base-fgts",
            new { valor = 10000m, observacao = "Extrato da Caixa de 01/2037" });

        Assert.Equal(HttpStatusCode.NoContent, primeira.StatusCode);

        // Idempotente: chamar de novo com o mesmo numero deixa no mesmo estado.
        using var segunda = await admin.PutAsJsonAsync(
            $"/api/contratos/{idContrato}/rescisao/valor-base-fgts",
            new { valor = 10000m, observacao = (string?)null });

        Assert.Equal(HttpStatusCode.NoContent, segunda.StatusCode);
    }

    [Fact]
    public async Task ValorBaseNegativo_ERecusado()
    {
        var admin = await AdminAsync();
        var sufixo = Sufixo();
        var idContrato = await DesligadoAsync(admin, sufixo, "2037-01-21", "DispensaSemJustaCausa", null);

        using var resposta = await admin.PutAsJsonAsync(
            $"/api/contratos/{idContrato}/rescisao/valor-base-fgts",
            new { valor = -1m, observacao = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task ValorBase_EmContratoATIVO_ERecusado()
    {
        var admin = await AdminAsync();
        var sufixo = Sufixo();

        using var respostaCargo = await admin.PostAsJsonAsync("/api/cargos", new
        {
            codigo = $"FR{sufixo}",
            nome = $"Cargo {sufixo}",
        });
        respostaCargo.EnsureSuccessStatusCode();
        var cargo = (await respostaCargo.Content.ReadFromJsonAsync<Identificado>())!;

        using var respostaFuncionario = await admin.PostAsJsonAsync("/api/funcionarios", new
        {
            nome = $"Ativo {sufixo}",
            cpf = BancoPostgresFixture.CpfDeTeste(71000 + int.Parse(sufixo)),
            dataNascimento = "1981-03-09",
        });
        respostaFuncionario.EnsureSuccessStatusCode();
        var funcionario = (await respostaFuncionario.Content.ReadFromJsonAsync<Identificado>())!;

        using var respostaContrato = await admin.PostAsJsonAsync(
            $"/api/funcionarios/{funcionario.Id}/contratos",
            new
            {
                idEmpresa = banco.IdEmpresaG,
                matricula = $"FR{sufixo}",
                dataAdmissao = "2024-01-10",
                salarioInicial = 3000m,
                idCargo = cargo.Id,
                idEstabelecimento = banco.IdEstabelecimentoG,
                jornadaMensalHoras = 220,
            });
        respostaContrato.EnsureSuccessStatusCode();
        var contrato = (await respostaContrato.Content.ReadFromJsonAsync<Identificado>())!;

        using var resposta = await admin.PutAsJsonAsync(
            $"/api/contratos/{contrato.Id}/rescisao/valor-base-fgts",
            new { valor = 10000m, observacao = (string?)null });

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    }

    [Fact]
    public async Task Auditor_NaoInformaValorBase()
    {
        var admin = await AdminAsync();
        var sufixo = Sufixo();
        var idContrato = await DesligadoAsync(admin, sufixo, "2037-01-22", "DispensaSemJustaCausa", null);

        var auditor = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAuditorA);

        using var resposta = await auditor.PutAsJsonAsync(
            $"/api/contratos/{idContrato}/rescisao/valor-base-fgts",
            new { valor = 99999m, observacao = (string?)null });

        // 403, e nao 404: a politica AdministrarPessoas e avaliada ANTES do
        // handler, entao o auditor nem chega ao filtro global.
        //
        // Isso NAO vaza nada sobre o contrato: o 403 responde "voce nao pode
        // informar valor base", e nao "este contrato existe". Quem passa pela
        // politica e ai sim cai no filtro global recebe 404, como os outros
        // testes de isolamento provam.
        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    // ---------------------------------------------------------- as rubricas

    [Fact]
    public async Task AsNoveRubricas_TemAsIncidenciasDoESocial()
    {
        var admin = await AdminAsync();
        await RubricasAsync(admin);

        var rubricas = await admin.GetFromJsonAsync<List<RubricaItem>>("/api/rubricas");

        foreach (var (codigo, bases) in Catalogo)
        {
            var r = rubricas!.Single(x => x.Codigo == codigo && x.Ativa);

            Assert.Equal("Provento", r.Tipo);
            Assert.Equal("VerbaRescisoria", r.Estrategia);
            Assert.Equal(bases, r.BasesIncidentes);
        }
    }

    [Fact]
    public async Task FeriasNaRescisao_NaoIntegramNADA_AoContrarioDasGozadas()
    {
        var admin = await AdminAsync();
        await RubricasAsync(admin);

        var rubricas = await admin.GetFromJsonAsync<List<RubricaItem>>("/api/rubricas");

        // Ferias GOZADAS (Fase 4E) integram as tres bases; ferias na RESCISAO
        // nao integram nenhuma. Copiar a incidencia de uma para a outra
        // descontaria INSS sobre verba que a lei nao alcanca.
        Assert.Equal("Nenhuma", rubricas!.Single(r => r.Codigo == "FERVEN" && r.Ativa).BasesIncidentes);
        Assert.Equal("Nenhuma", rubricas.Single(r => r.Codigo == "FERPROP" && r.Ativa).BasesIncidentes);
    }

    [Fact]
    public async Task AsDuasRubricasDe13_TemIncidenciasDIFERENTES()
    {
        var admin = await AdminAsync();
        await RubricasAsync(admin);

        var rubricas = await admin.GetFromJsonAsync<List<RubricaItem>>("/api/rubricas");

        var proporcional = rubricas!.Single(r => r.Codigo == "DEC13PROP" && r.Ativa);
        var sobreAviso = rubricas.Single(r => r.Codigo == "DEC13AV" && r.Ativa);

        // A razao de serem duas rubricas: o 13o proporcional integra IRRF, o
        // 13o sobre o aviso nao. Uma rubrica so obrigaria a errar uma das duas.
        Assert.Equal("Inss, Fgts, Irrf", proporcional.BasesIncidentes);
        Assert.Equal("Inss, Fgts", sobreAviso.BasesIncidentes);
        Assert.NotEqual(proporcional.BasesIncidentes, sobreAviso.BasesIncidentes);
    }

    [Fact]
    public async Task RubricaDeRescisao_ComoDesconto_ERecusada()
    {
        var admin = await AdminAsync();

        using var resposta = await admin.PostAsJsonAsync("/api/rubricas", new
        {
            codigo = $"RX{Sufixo()}",
            nome = "Rescisao errada",
            tipo = "Desconto",
            estrategia = "VerbaRescisoria",
            basesIncidentes = "Nenhuma",
        });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    // ------------------------------------------------------------- a folha

    [Fact]
    public async Task Holerite_TemAsVerbas_ComAsBasesCertas()
    {
        var admin = await AdminAsync();
        var sufixo = Sufixo();

        await RubricasAsync(admin);
        await DesligadoAsync(admin, sufixo, "2037-02-20", "DispensaSemJustaCausa", 10000m);

        var detalhe = await CalcularAsync(admin, "02/2037");
        var meu = detalhe.Funcionarios.Single(f => f.Matricula == $"FR{sufixo}");
        var holerite = (await HoleriteAsync(admin, detalhe.Folha.Id, meu.Id))!;

        decimal Valor(string c) => holerite.Lancamentos.Single(l => l.CodigoRubrica == c).Valor;

        // Salario 3.000, diario 100. Admitido 10/01/2024, saida 20/02/2037.
        Assert.Equal(2000.00m, Valor("SALDO"));       // 01/02 a 20/02 = 20 dias
        Assert.Equal(4000.00m, Valor("MULTAFGTS"));   // 40% de 10.000

        // As BASES sao o ponto do teste. Fonte: eSocial.
        //
        // INSS: saldo + 13o proporcional + 13o sobre o aviso. Nada de ferias,
        // nada de aviso previo, nada de multa.
        var baseInss = holerite.Bases.Single(b => b.Base == "Inss");
        Assert.Contains("SALDO", baseInss.Composta);
        Assert.Contains("DEC13PROP", baseInss.Composta);
        Assert.DoesNotContain("AVISO", baseInss.Composta);
        Assert.DoesNotContain("FERVEN", baseInss.Composta);
        Assert.DoesNotContain("MULTAFGTS", baseInss.Composta);

        // FGTS: tudo do INSS mais o AVISO.
        var baseFgts = holerite.Bases.Single(b => b.Base == "Fgts");
        Assert.Contains("AVISO", baseFgts.Composta);
        Assert.Contains("SALDO", baseFgts.Composta);
        Assert.DoesNotContain("FERVEN", baseFgts.Composta);

        // IRRF: saldo e 13o proporcional. NAO o 13o sobre o aviso.
        var baseIrrf = holerite.Bases.Single(b => b.Base == "Irrf");
        Assert.Contains("SALDO", baseIrrf.Composta);
        Assert.Contains("DEC13PROP", baseIrrf.Composta);
        Assert.DoesNotContain("DEC13AV", baseIrrf.Composta);
        Assert.DoesNotContain("AVISO", baseIrrf.Composta);
    }

    [Fact]
    public async Task OAvisoPROJETA_EGeraO13SobreEle()
    {
        var admin = await AdminAsync();
        var sufixo = Sufixo();

        await RubricasAsync(admin);
        await DesligadoAsync(admin, sufixo, "2037-03-20", "DispensaSemJustaCausa", 10000m);

        var detalhe = await CalcularAsync(admin, "03/2037");
        var meu = detalhe.Funcionarios.Single(f => f.Matricula == $"FR{sufixo}");
        var holerite = (await HoleriteAsync(admin, detalhe.Folha.Id, meu.Id))!;

        // CLT art. 487 par. 1o: o aviso indenizado integra o tempo de servico.
        //
        // Admitido 10/01/2024, saida 20/03/2037: 13 anos completos.
        // 30 + 13 x 3 = 69 dias - o teto de 60 ACRESCIDOS nao e alcancado,
        // porque 39 < 60. Saida projetada: 28/05/2037.
        var aviso = holerite.Lancamentos.Single(l => l.CodigoRubrica == "AVISO");
        Assert.Equal("69 dias", aviso.Referencia);

        // Sem projecao o 13o iria ate marco: jan, fev e mar = 3 avos.
        // Com ela vai ate 28/05: mais abril e maio = 5. A diferenca sao os
        // 2 avos que viram a verba DEC13AV - e ela e SEPARADA porque nao
        // integra IRRF, ao contrario do 13o proporcional.
        var sobreAviso = holerite.Lancamentos.Single(l => l.CodigoRubrica == "DEC13AV");
        Assert.Equal("2/12", sobreAviso.Referencia);
        Assert.Equal(500.00m, sobreAviso.Valor);   // 3.000 x 2/12

        var proporcional = holerite.Lancamentos.Single(l => l.CodigoRubrica == "DEC13PROP");
        Assert.Equal("3/12", proporcional.Referencia);
    }

    [Fact]
    public async Task MotivoBloqueado_FicaDeFORA_EAFolhaDiz()
    {
        var admin = await AdminAsync();
        var sufixo = Sufixo();

        await RubricasAsync(admin);
        await DesligadoAsync(admin, sufixo, "2037-04-20", "Aposentadoria", 10000m);

        var detalhe = await CalcularAsync(admin, "04/2037");

        // Um holerite vazio no meio da folha pareceria erro de calculo em vez
        // de motivo sem fonte.
        Assert.DoesNotContain(detalhe.Funcionarios, f => f.Matricula == $"FR{sufixo}");
        Assert.NotNull(detalhe.ContratosIgnorados);
        Assert.NotEmpty(detalhe.ContratosIgnorados!);
    }

    [Fact]
    public async Task SemAsRubricas_ORecusaExplica()
    {
        // Organizacao B nao tem rubrica de rescisao alguma.
        var adminB = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminB);

        using var abertura = await adminB.PostAsJsonAsync("/api/folhas", new
        {
            idEmpresa = banco.IdEmpresaB,
            competencia = "05/2037",
            tipo = "Rescisao",
        });
        abertura.EnsureSuccessStatusCode();
        var folha = (await abertura.Content.ReadFromJsonAsync<FolhaResumo>())!;

        using var calculo = await adminB.PostAsync($"/api/folhas/{folha.Id}/calcular", null);

        Assert.Equal(HttpStatusCode.Conflict, calculo.StatusCode);
        Assert.Contains("Faltam rubricas de rescisao", await calculo.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TresTiposDeFolha_ConvivemNaMesmaCompetencia()
    {
        var admin = await AdminAsync();

        foreach (var tipo in new[] { "Mensal", "Ferias", "Rescisao" })
        {
            using var r = await admin.PostAsJsonAsync("/api/folhas", new
            {
                idEmpresa = banco.IdEmpresaG,
                competencia = "06/2037",
                tipo,
            });

            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        }
    }

    // ------------------------------------------------------- multiempresa

    [Fact]
    public async Task ValorBaseDeOutraOrganizacao_NaoEAlcancado()
    {
        var admin = await AdminAsync();
        var sufixo = Sufixo();
        var idContrato = await DesligadoAsync(admin, sufixo, "2037-01-25", "DispensaSemJustaCausa", 10000m);

        var adminB = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminB);

        using var resposta = await adminB.PutAsJsonAsync(
            $"/api/contratos/{idContrato}/rescisao/valor-base-fgts",
            new { valor = 1m, observacao = (string?)null });

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }
}
