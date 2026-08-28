using System.Net;
using System.Net.Http.Json;

namespace PrismaRH.Testes.Isolamento;

/// <summary>
/// A Fase 4D etapa 2 ponta a ponta, contra PostgreSQL real.
///
/// Os testes de dominio provam a aritmetica contra os exemplos oficiais da
/// Receita. Estes provam o que so o sistema inteiro pode provar: que a tabela
/// sai do banco, que o IRRF deduz o INSS que a MESMA folha acabou de apurar, e
/// que os dependentes cadastrados chegam ate a conta.
///
/// Competencias EXCLUSIVAS desta classe: 01/2033 a 06/2033.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class IrrfIntegracaoTestes(BancoPostgresFixture banco) : IDisposable
{
    private readonly FabricaApiIsolada _fabrica = new(banco.StringConexao);

    public void Dispose() => _fabrica.Dispose();

    private sealed record Identificado(Guid Id);

    private sealed record RubricaItem(
        Guid Id, string Codigo, string Nome, string Tipo, string Estrategia, string BasesIncidentes, bool Ativa);

    private sealed record FaixaItem(
        int Ordem, decimal LimiteInferior, decimal? LimiteSuperior,
        decimal Aliquota, decimal AliquotaPercentual, decimal ParcelaADeduzir);

    private sealed record TabelaItem(
        Guid Id, DateOnly VigenciaInicio, string Fonte, decimal DeducaoPorDependente,
        decimal DescontoSimplificado, decimal RedutorBase, decimal RedutorCoeficiente,
        decimal LimiteDoRedutor, decimal LimiteIsencao, bool TemRedutor, bool Vigente,
        List<FaixaItem> Faixas);

    private sealed record HoleriteResumo(
        Guid Id, Guid IdFuncionario, string Funcionario, string Matricula,
        int Avos, int Divisor, decimal SalarioReferencia,
        decimal TotalProventos, decimal TotalDescontos, decimal Liquido);

    private sealed record FolhaResumo(
        Guid Id, string Empresa, string Competencia, string Situacao, int VersaoCalculo,
        int QuantidadeFuncionarios, decimal TotalProventos, decimal TotalDescontos, decimal TotalLiquido);

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

    // -----------------------------------------------------------------------

    private static async Task RubricaSalarioAsync(HttpClient admin)
    {
        using var criacao = await admin.PostAsJsonAsync("/api/rubricas", new
        {
            codigo = "SAL",
            nome = "Salario base",
            tipo = "Provento",
            estrategia = "SalarioBaseProporcional",
            basesIncidentes = "Inss, Fgts, Irrf",
        });

        if (criacao.StatusCode == HttpStatusCode.Created)
        {
            return;
        }

        var existentes = await admin.GetFromJsonAsync<List<RubricaItem>>("/api/rubricas");
        var salario = existentes!.Single(r => r.Codigo == "SAL" && r.Ativa);

        using var ajuste = await admin.PutAsJsonAsync(
            $"/api/rubricas/{salario.Id}/incidencias", new { basesIncidentes = "Inss, Fgts, Irrf" });
        ajuste.EnsureSuccessStatusCode();
    }

    private static async Task<Guid> RubricaCalculadaAsync(
        HttpClient admin, string codigo, string estrategia, string tipo)
    {
        using var criacao = await admin.PostAsJsonAsync("/api/rubricas", new
        {
            codigo,
            nome = codigo + " sobre a folha",
            tipo,
            estrategia,
            basesIncidentes = "Nenhuma",
        });

        if (criacao.StatusCode == HttpStatusCode.Created)
        {
            return (await criacao.Content.ReadFromJsonAsync<Identificado>())!.Id;
        }

        var existentes = await admin.GetFromJsonAsync<List<RubricaItem>>("/api/rubricas");

        return existentes!.Single(r => r.Estrategia == estrategia && r.Ativa).Id;
    }

    private static Task RubricaInssAsync(HttpClient a) =>
        RubricaCalculadaAsync(a, "INSS", "InssProgressivo", "Desconto");

    private static Task RubricaIrrfAsync(HttpClient a) =>
        RubricaCalculadaAsync(a, "IRRF", "IrrfMensal", "Desconto");

    private async Task<Guid> FuncionarioComContratoAsync(
        HttpClient cliente, string sufixo, decimal salario)
    {
        using var respostaCargo = await cliente.PostAsJsonAsync("/api/cargos", new
        {
            codigo = $"R{sufixo}",
            nome = $"Cargo irrf {sufixo}",
        });
        respostaCargo.EnsureSuccessStatusCode();
        var cargo = (await respostaCargo.Content.ReadFromJsonAsync<Identificado>())!;

        using var respostaFuncionario = await cliente.PostAsJsonAsync("/api/funcionarios", new
        {
            nome = $"Irrf Pessoa {sufixo}",
            cpf = BancoPostgresFixture.CpfDeTeste(20000 + int.Parse(sufixo)),
            dataNascimento = "1987-09-30",
        });
        respostaFuncionario.EnsureSuccessStatusCode();
        var funcionario = (await respostaFuncionario.Content.ReadFromJsonAsync<Identificado>())!;

        using var respostaContrato = await cliente.PostAsJsonAsync(
            $"/api/funcionarios/{funcionario.Id}/contratos",
            new
            {
                idEmpresa = banco.IdEmpresaE,
                matricula = $"R{sufixo}",
                dataAdmissao = "2025-03-01",
                salarioInicial = salario,
                idCargo = cargo.Id,
                idEstabelecimento = banco.IdEstabelecimentoE,
                jornadaMensalHoras = 220,
            });
        respostaContrato.EnsureSuccessStatusCode();

        return funcionario.Id;
    }

    private async Task<FolhaDetalhe> AbrirECalcularAsync(HttpClient admin, string competencia)
    {
        using var abertura = await admin.PostAsJsonAsync(
            "/api/folhas", new { idEmpresa = banco.IdEmpresaE, competencia });
        abertura.EnsureSuccessStatusCode();
        var folha = (await abertura.Content.ReadFromJsonAsync<FolhaResumo>())!;

        using var calculo = await admin.PostAsync($"/api/folhas/{folha.Id}/calcular", null);
        calculo.EnsureSuccessStatusCode();

        return (await calculo.Content.ReadFromJsonAsync<FolhaDetalhe>())!;
    }

    private static Task<Holerite?> HoleriteAsync(HttpClient cliente, Guid idFolha, Guid idHolerite) =>
        cliente.GetFromJsonAsync<Holerite>($"/api/folhas/{idFolha}/funcionarios/{idHolerite}");

    private static Lancamento? Irrf(Holerite h) =>
        h.Lancamentos.SingleOrDefault(l => l.CodigoRubrica == "IRRF");

    private Task<HttpClient> AdminAsync() =>
        _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminE);

    // ------------------------------------------------------------- tabela

    [Fact]
    public async Task Tabela2026_EstaCadastrada_ComFonteFaixasERedutor()
    {
        var leitor = await AdminAsync();

        var tabelas = await leitor.GetFromJsonAsync<List<TabelaItem>>("/api/tabelas-irrf");
        var t = tabelas!.Single(x => x.VigenciaInicio == new DateOnly(2026, 1, 1));

        Assert.Contains("Lei n. 15.191", t.Fonte);
        Assert.Contains("Lei n. 15.270", t.Fonte);
        Assert.Equal(189.59m, t.DeducaoPorDependente);
        Assert.Equal(607.20m, t.DescontoSimplificado);
        Assert.True(t.TemRedutor);
        Assert.Equal(978.62m, t.RedutorBase);
        Assert.Equal(2428.80m, t.LimiteIsencao);
        Assert.Equal(5, t.Faixas.Count);

        // A ultima faixa nao tem teto, e isso sobreviveu ao banco.
        Assert.Null(t.Faixas.Single(f => f.Ordem == 5).LimiteSuperior);
        Assert.Equal(908.73m, t.Faixas.Single(f => f.Ordem == 5).ParcelaADeduzir);
    }

    [Fact]
    public async Task Tabela_SoOAdministradorDaPlataformaCadastra()
    {
        var adminEmpresa = await AdminAsync();

        using var resposta = await adminEmpresa.PostAsJsonAsync("/api/tabelas-irrf", new
        {
            vigenciaInicio = "2035-01-01",
            fonte = "Tentativa indevida",
            deducaoPorDependente = 200m,
            descontoSimplificado = 700m,
            redutorBase = 0m,
            redutorCoeficiente = 0m,
            faixas = new[] { new { limiteSuperior = 3000m, aliquota = 0m, parcelaADeduzir = 0m } },
        });

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    [Fact]
    public async Task Tabela_SemFonte_ERecusada()
    {
        var plataforma = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailPlataformaA);

        using var resposta = await plataforma.PostAsJsonAsync("/api/tabelas-irrf", new
        {
            vigenciaInicio = "2036-01-01",
            fonte = "   ",
            deducaoPorDependente = 200m,
            descontoSimplificado = 700m,
            redutorBase = 0m,
            redutorCoeficiente = 0m,
            faixas = new[] { new { limiteSuperior = 3000m, aliquota = 0m, parcelaADeduzir = 0m } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Tabela_ComRedutorPelaMetade_ERecusada()
    {
        var plataforma = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailPlataformaA);

        using var resposta = await plataforma.PostAsJsonAsync("/api/tabelas-irrf", new
        {
            vigenciaInicio = "2037-01-01",
            fonte = "Base sem coeficiente",
            deducaoPorDependente = 200m,
            descontoSimplificado = 700m,
            redutorBase = 900m,
            redutorCoeficiente = 0m,
            faixas = new[] { new { limiteSuperior = 3000m, aliquota = 0m, parcelaADeduzir = 0m } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    // ------------------------------------------------------------ rubrica

    [Fact]
    public async Task RubricaDeIrrf_ComoInformativa_ERecusada()
    {
        var admin = await AdminAsync();

        using var resposta = await admin.PostAsJsonAsync("/api/rubricas", new
        {
            codigo = $"IR{Sufixo()}",
            nome = "IRRF errado",
            tipo = "Informativo",
            estrategia = "IrrfMensal",
            basesIncidentes = "Nenhuma",
        });

        // Como informativo o IRRF nao reduziria o liquido, e a pessoa
        // receberia dinheiro que a empresa ja recolheu.
        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    // ------------------------------------------------------------- folha

    [Fact]
    public async Task Holerite_DescontaIrrf_DeduzindoOInssDaMesmaFolha()
    {
        var admin = await AdminAsync();
        var sufixo = Sufixo();

        await RubricaSalarioAsync(admin);
        await RubricaInssAsync(admin);
        await RubricaIrrfAsync(admin);
        await FuncionarioComContratoAsync(admin, sufixo, 6000.00m);

        var detalhe = await AbrirECalcularAsync(admin, "01/2033");
        var meu = detalhe.Funcionarios.Single(f => f.Matricula == $"R{sufixo}");
        var holerite = (await HoleriteAsync(admin, detalhe.Folha.Id, meu.Id))!;

        var inss = holerite.Lancamentos.Single(l => l.CodigoRubrica == "INSS");
        var irrf = Irrf(holerite);

        Assert.NotNull(irrf);
        Assert.Equal("Desconto", irrf!.Tipo);
        Assert.Equal("Calculado", irrf.Origem);

        // Base 6.000, INSS pela tabela de 2026 (Portaria MPS/MF 13/2026):
        // 121,575 + 115,3656 + 174,1716 + 230,4022 = 641,5144 -> 641,51.
        //
        // NAO e o 649,60 do exemplo oficial 4 da Receita, e isso e proposital:
        // aquele exemplo ilustra o IRRF e informa um INSS proprio, que nao sai
        // da tabela vigente. Assumir que os dois numeros coincidiriam era
        // suposicao minha, e o teste a derrubou.
        Assert.Equal(641.51m, inss.Valor);

        // base legal   = 6.000,00 - 641,51 = 5.358,49  (menor que 5.392,80)
        // imposto      = 5.358,49 x 27,5% - 908,73 = 564,85475
        // redutor      = 978,62 - 0,133145 x 6.000 = 179,75
        // IRRF         = 385,10
        Assert.Equal(385.10m, irrf.Valor);

        Assert.Equal(641.51m + 385.10m, holerite.Resumo.TotalDescontos);
        Assert.Equal(6000.00m - 641.51m - 385.10m, holerite.Resumo.Liquido);

        // A memoria sobreviveu ao banco e mostra a deducao do INSS - o da
        // MESMA folha, nao um valor de outro calculo.
        Assert.Contains(irrf.Memoria, m => m.Descricao == "Deducao do INSS" && m.Valor == 641.51m);
        Assert.Contains(irrf.Memoria, m => m.Descricao.Contains("Redutor"));
        Assert.Equal("Total do IRRF", irrf.Memoria[^1].Descricao);
    }

    [Fact]
    public async Task AteCincoMil_NaoPagaImposto()
    {
        var admin = await AdminAsync();
        var sufixo = Sufixo();

        await RubricaSalarioAsync(admin);
        await RubricaInssAsync(admin);
        await RubricaIrrfAsync(admin);
        await FuncionarioComContratoAsync(admin, sufixo, 5000.00m);

        var detalhe = await AbrirECalcularAsync(admin, "02/2033");
        var meu = detalhe.Funcionarios.Single(f => f.Matricula == $"R{sufixo}");
        var holerite = (await HoleriteAsync(admin, detalhe.Folha.Id, meu.Id))!;

        // A promessa da Lei 15.270/2025, ponta a ponta.
        Assert.Equal(0m, Irrf(holerite)!.Valor);
    }

    [Fact]
    public async Task Dependentes_ReduzemOImposto()
    {
        var admin = await AdminAsync();
        var sufixo = Sufixo();

        await RubricaSalarioAsync(admin);
        await RubricaInssAsync(admin);
        await RubricaIrrfAsync(admin);
        var idFuncionario = await FuncionarioComContratoAsync(admin, sufixo, 9000.00m);

        var semDependente = await AbrirECalcularAsync(admin, "03/2033");
        var antesResumo = semDependente.Funcionarios.Single(f => f.Matricula == $"R{sufixo}");
        var antes = (await HoleriteAsync(admin, semDependente.Folha.Id, antesResumo.Id))!;
        var impostoSem = Irrf(antes)!.Valor;

        // Dois dependentes dedutiveis a partir de 2026.
        for (var i = 0; i < 2; i++)
        {
            using var criacao = await admin.PostAsJsonAsync(
                $"/api/funcionarios/{idFuncionario}/dependentes", new
                {
                    nome = $"Dependente {i} de {sufixo}",
                    dataNascimento = "2015-05-05",
                    relacao = "Filho",
                    inicioDeducaoIrrf = "2026-01-01",
                });
            criacao.EnsureSuccessStatusCode();
        }

        using var recalculo = await admin.PostAsync($"/api/folhas/{semDependente.Folha.Id}/calcular", null);
        recalculo.EnsureSuccessStatusCode();
        var depoisDetalhe = (await recalculo.Content.ReadFromJsonAsync<FolhaDetalhe>())!;
        var depoisResumo = depoisDetalhe.Funcionarios.Single(f => f.Matricula == $"R{sufixo}");
        var depois = (await HoleriteAsync(admin, depoisDetalhe.Folha.Id, depoisResumo.Id))!;

        var impostoCom = Irrf(depois)!.Valor;

        // 2 x 189,59 = 379,18 de base a menos, na faixa de 27,5%.
        //
        // 379,18 x 27,5% = 104,2745, mas a diferenca observada e 104,28: cada
        // holerite arredonda o SEU imposto uma vez, e a subtracao acontece
        // depois. Um centavo de diferenca entre "arredondar a diferenca" e
        // "diferenca dos arredondados" e esperado, e o teste registra qual dos
        // dois o sistema faz.
        Assert.Equal(104.28m, impostoSem - impostoCom);
        Assert.Contains(Irrf(depois)!.Memoria, m => m.Descricao.Contains("2 dependente"));
    }

    [Fact]
    public async Task DependenteForaDaCompetencia_NaoConta()
    {
        var admin = await AdminAsync();
        var sufixo = Sufixo();

        await RubricaSalarioAsync(admin);
        await RubricaInssAsync(admin);
        await RubricaIrrfAsync(admin);
        var idFuncionario = await FuncionarioComContratoAsync(admin, sufixo, 9000.00m);

        // Periodo que termina ANTES da competencia da folha.
        using var criacao = await admin.PostAsJsonAsync(
            $"/api/funcionarios/{idFuncionario}/dependentes", new
            {
                nome = $"Ja saiu {sufixo}",
                dataNascimento = "2000-01-01",
                relacao = "Filho",
                inicioDeducaoIrrf = "2026-01-01",
                fimDeducaoIrrf = "2026-12-31",
            });
        criacao.EnsureSuccessStatusCode();

        var detalhe = await AbrirECalcularAsync(admin, "04/2033");
        var meu = detalhe.Funcionarios.Single(f => f.Matricula == $"R{sufixo}");
        var holerite = (await HoleriteAsync(admin, detalhe.Folha.Id, meu.Id))!;

        Assert.DoesNotContain(Irrf(holerite)!.Memoria, m => m.Descricao.Contains("dependente"));
    }

    [Fact]
    public async Task LancamentoManual_ReapuraOIrrf_EOInssJunto()
    {
        var admin = await AdminAsync();
        var sufixo = Sufixo();

        await RubricaSalarioAsync(admin);
        await RubricaInssAsync(admin);
        await RubricaIrrfAsync(admin);
        await FuncionarioComContratoAsync(admin, sufixo, 6000.00m);

        using var criacaoComissao = await admin.PostAsJsonAsync("/api/rubricas", new
        {
            codigo = $"CI{sufixo}",
            nome = "Comissao",
            tipo = "Provento",
            estrategia = "ValorInformado",
            basesIncidentes = "Inss, Fgts, Irrf",
        });
        criacaoComissao.EnsureSuccessStatusCode();
        var comissao = (await criacaoComissao.Content.ReadFromJsonAsync<Identificado>())!;

        var detalhe = await AbrirECalcularAsync(admin, "05/2033");
        var meu = detalhe.Funcionarios.Single(f => f.Matricula == $"R{sufixo}");

        var antes = (await HoleriteAsync(admin, detalhe.Folha.Id, meu.Id))!;
        var irrfAntes = Irrf(antes)!.Valor;

        using var lancamento = await admin.PostAsJsonAsync(
            $"/api/folhas/{detalhe.Folha.Id}/funcionarios/{meu.Id}/lancamentos",
            new { idRubrica = comissao.Id, valor = 2000.00m });
        lancamento.EnsureSuccessStatusCode();

        var depois = (await HoleriteAsync(admin, detalhe.Folha.Id, meu.Id))!;

        // O que este teste existe para provar: a comissao aumentou a base, o
        // INSS foi reapurado, e o IRRF usou o INSS NOVO - nao o antigo.
        var inssDepois = depois.Lancamentos.Single(l => l.CodigoRubrica == "INSS").Valor;
        var irrfDepois = Irrf(depois)!;

        Assert.Equal(8000.00m, depois.Bases.Single(b => b.Base == "Irrf").Valor);
        Assert.True(irrfDepois.Valor > irrfAntes);
        Assert.Contains(irrfDepois.Memoria, m => m.Descricao == "Deducao do INSS" && m.Valor == inssDepois);
    }

    [Fact]
    public async Task Recalcular_NaoDuplicaOIrrf()
    {
        var admin = await AdminAsync();
        var sufixo = Sufixo();

        await RubricaSalarioAsync(admin);
        await RubricaInssAsync(admin);
        await RubricaIrrfAsync(admin);
        await FuncionarioComContratoAsync(admin, sufixo, 6000.00m);

        var detalhe = await AbrirECalcularAsync(admin, "06/2033");

        using var recalculo = await admin.PostAsync($"/api/folhas/{detalhe.Folha.Id}/calcular", null);
        recalculo.EnsureSuccessStatusCode();
        var depois = (await recalculo.Content.ReadFromJsonAsync<FolhaDetalhe>())!;

        var meu = depois.Funcionarios.Single(f => f.Matricula == $"R{sufixo}");
        var holerite = (await HoleriteAsync(admin, depois.Folha.Id, meu.Id))!;

        Assert.Single(holerite.Lancamentos, l => l.CodigoRubrica == "IRRF");
    }

    // ------------------------------------------------------- multiempresa

    [Fact]
    public async Task RubricaDeIrrf_DeOutraOrganizacao_NaoAparece()
    {
        var adminE = await AdminAsync();
        await RubricaIrrfAsync(adminE);

        var adminB = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminB);
        var rubricasB = await adminB.GetFromJsonAsync<List<RubricaItem>>("/api/rubricas");

        Assert.DoesNotContain(rubricasB!, r => r.Estrategia == "IrrfMensal");
    }

    [Fact]
    public async Task Tabela_ELidaPorTodasAsOrganizacoes()
    {
        // Contraprova: parametro FEDERAL nao tem dono. Sob o filtro global, a
        // organizacao B nao veria nada e a folha dela sairia sem IRRF.
        var adminB = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminB);

        var tabelas = await adminB.GetFromJsonAsync<List<TabelaItem>>("/api/tabelas-irrf");

        Assert.Contains(tabelas!, t => t.VigenciaInicio == new DateOnly(2026, 1, 1));
    }
}
