using System.Net;
using System.Net.Http.Json;

namespace PrismaRH.Testes.Isolamento;

/// <summary>
/// A Fase 4B ponta a ponta, contra PostgreSQL real.
///
/// Os testes de dominio provam a aritmetica progressiva; estes provam que a
/// tabela sai do banco, que o desconto entra no holerite com a memoria, e que
/// mexer no holerite reapura o INSS em vez de deixar um valor velho.
///
/// Competencias EXCLUSIVAS desta classe: 01/2029 a 05/2029. Ver a nota em
/// BasesDeCalculoIntegracaoTestes sobre por que competencia repetida derruba o
/// teste por colisao e nao por defeito.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class InssIntegracaoTestes(BancoPostgresFixture banco) : IDisposable
{
    private readonly FabricaApiIsolada _fabrica = new(banco.StringConexao);

    public void Dispose() => _fabrica.Dispose();

    private sealed record Identificado(Guid Id);

    private sealed record RubricaItem(
        Guid Id, string Codigo, string Nome, string Tipo, string Estrategia, string BasesIncidentes, bool Ativa);

    private sealed record FaixaItem(
        int Ordem, decimal LimiteInferior, decimal LimiteSuperior, decimal Aliquota, decimal AliquotaPercentual);

    private sealed record TabelaItem(
        Guid Id, DateOnly VigenciaInicio, string Fonte, decimal Teto, bool Vigente, List<FaixaItem> Faixas);

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

    private static async Task<Guid> RubricaSalarioAsync(HttpClient admin)
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
            return (await criacao.Content.ReadFromJsonAsync<Identificado>())!.Id;
        }

        var existentes = await admin.PaginaDe<RubricaItem>("/api/rubricas");
        var salario = existentes!.Single(r => r.Codigo == "SAL" && r.Ativa);

        using var ajuste = await admin.PutAsJsonAsync(
            $"/api/rubricas/{salario.Id}/incidencias", new { basesIncidentes = "Inss, Fgts, Irrf" });
        ajuste.EnsureSuccessStatusCode();

        return salario.Id;
    }

    /// <summary>Garante a rubrica de INSS da organizacao. So pode haver uma ativa.</summary>
    private static async Task<Guid> RubricaInssAsync(HttpClient admin)
    {
        using var criacao = await admin.PostAsJsonAsync("/api/rubricas", new
        {
            codigo = "INSS",
            nome = "INSS sobre a folha",
            tipo = "Desconto",
            estrategia = "InssProgressivo",
            basesIncidentes = "Nenhuma",
        });

        if (criacao.StatusCode == HttpStatusCode.Created)
        {
            return (await criacao.Content.ReadFromJsonAsync<Identificado>())!.Id;
        }

        var existentes = await admin.PaginaDe<RubricaItem>("/api/rubricas");

        return existentes!.Single(r => r.Estrategia == "InssProgressivo" && r.Ativa).Id;
    }

    private static async Task<Guid> ContratoAsync(
        HttpClient cliente, Guid idEmpresa, Guid idEstabelecimento,
        string cpf, string sufixo, decimal salario)
    {
        using var respostaCargo = await cliente.PostAsJsonAsync("/api/cargos", new
        {
            codigo = $"I{sufixo}",
            nome = $"Cargo inss {sufixo}",
        });
        respostaCargo.EnsureSuccessStatusCode();
        var cargo = (await respostaCargo.Content.ReadFromJsonAsync<Identificado>())!;

        using var respostaFuncionario = await cliente.PostAsJsonAsync("/api/funcionarios", new
        {
            nome = $"Inss Pessoa {sufixo}",
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
                matricula = $"I{sufixo}",
                dataAdmissao = "2025-03-01",
                salarioInicial = salario,
                idCargo = cargo.Id,
                idEstabelecimento,
                jornadaMensalHoras = 220,
            });
        respostaContrato.EnsureSuccessStatusCode();

        return (await respostaContrato.Content.ReadFromJsonAsync<Identificado>())!.Id;
    }

    private static async Task<FolhaDetalhe> AbrirECalcularAsync(
        HttpClient admin, Guid idEmpresa, string competencia)
    {
        using var abertura = await admin.PostAsJsonAsync("/api/folhas", new { idEmpresa, competencia });
        abertura.EnsureSuccessStatusCode();
        var folha = (await abertura.Content.ReadFromJsonAsync<FolhaResumo>())!;

        using var calculo = await admin.PostAsync($"/api/folhas/{folha.Id}/calcular", null);
        calculo.EnsureSuccessStatusCode();

        return (await calculo.Content.ReadFromJsonAsync<FolhaDetalhe>())!;
    }

    private static Task<Holerite?> HoleriteAsync(HttpClient cliente, Guid idFolha, Guid idHolerite) =>
        cliente.GetFromJsonAsync<Holerite>($"/api/folhas/{idFolha}/funcionarios/{idHolerite}");

    private static Lancamento? Inss(Holerite h) =>
        h.Lancamentos.SingleOrDefault(l => l.CodigoRubrica == "INSS");

    // ------------------------------------------------------------- tabela

    [Fact]
    public async Task Tabela2026_EstaCadastrada_ComFonteEFaixas()
    {
        var leitor = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminC);

        var tabelas = await leitor.GetFromJsonAsync<List<TabelaItem>>("/api/tabelas-inss");
        var t2026 = tabelas!.Single(t => t.VigenciaInicio == new DateOnly(2026, 1, 1));

        Assert.Contains("Portaria Interministerial MPS/MF", t2026.Fonte);
        Assert.Equal(8475.55m, t2026.Teto);
        Assert.Equal(4, t2026.Faixas.Count);

        Assert.Equal((0m, 1621.00m, 7.5m), Faixa(t2026, 1));
        Assert.Equal((1621.00m, 2902.84m, 9m), Faixa(t2026, 2));
        Assert.Equal((2902.84m, 4354.27m, 12m), Faixa(t2026, 3));
        Assert.Equal((4354.27m, 8475.55m, 14m), Faixa(t2026, 4));

        static (decimal, decimal, decimal) Faixa(TabelaItem t, int ordem)
        {
            var f = t.Faixas.Single(x => x.Ordem == ordem);
            return (f.LimiteInferior, f.LimiteSuperior, f.AliquotaPercentual);
        }
    }

    [Fact]
    public async Task Tabela_SoOAdministradorDaPlataformaCadastra()
    {
        var adminEmpresa = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminC);

        using var resposta = await adminEmpresa.PostAsJsonAsync("/api/tabelas-inss", new
        {
            vigenciaInicio = "2030-01-01",
            fonte = "Tentativa indevida",
            faixas = new[] { new { limiteSuperior = 2000m, aliquota = 0.08m } },
        });

        // Parametro legal e federal: um Administrador de Empresa alterando a
        // tabela mudaria o desconto de TODAS as organizacoes.
        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    [Fact]
    public async Task Tabela_SemFonte_ERecusada()
    {
        var plataforma = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailPlataformaA);

        using var resposta = await plataforma.PostAsJsonAsync("/api/tabelas-inss", new
        {
            vigenciaInicio = "2031-01-01",
            fonte = "   ",
            faixas = new[] { new { limiteSuperior = 2000m, aliquota = 0.08m } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    // -------------------------------------------------------- na folha

    [Fact]
    public async Task Holerite_TemODescontoDeInss_ComAMemoriaFaixaAFaixa()
    {
        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminC);
        var sufixo = Sufixo();

        await RubricaSalarioAsync(admin);
        await RubricaInssAsync(admin);
        await ContratoAsync(
            admin, banco.IdEmpresaC, banco.IdEstabelecimentoC,
            BancoPostgresFixture.CpfDeTeste(5100 + int.Parse(sufixo)), sufixo, 5000.00m);

        var detalhe = await AbrirECalcularAsync(admin, banco.IdEmpresaC, "01/2029");
        var meu = detalhe.Funcionarios.Single(f => f.Matricula == $"I{sufixo}");

        var holerite = (await HoleriteAsync(admin, detalhe.Folha.Id, meu.Id))!;
        var inss = Inss(holerite);

        Assert.NotNull(inss);
        Assert.Equal("Desconto", inss!.Tipo);
        Assert.Equal("Calculado", inss.Origem);

        // Base 5.000, tabela de 2026: 501,51 (conferido no teste de dominio).
        Assert.Equal(501.51m, inss.Valor);
        Assert.Equal(5000.00m, holerite.Bases.Single(b => b.Base == "Inss").Valor);
        Assert.Equal(4498.49m, holerite.Resumo.Liquido);

        // A memoria sobreviveu ao banco: base + 4 faixas + total.
        Assert.Equal(6, inss.Memoria.Count);
        Assert.Equal("Base de contribuição", inss.Memoria[0].Descricao);
        Assert.Equal("1.621,00 x 7,5% = 121,575", inss.Memoria[1].Expressao);
        Assert.Equal("Total do INSS", inss.Memoria[^1].Descricao);
        Assert.Equal(501.51m, inss.Memoria[^1].Valor);
    }

    [Fact]
    public async Task AcimaDoTeto_ODescontoPara_NoValorDoTeto()
    {
        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminC);
        var sufixo = Sufixo();

        await RubricaSalarioAsync(admin);
        await RubricaInssAsync(admin);
        await ContratoAsync(
            admin, banco.IdEmpresaC, banco.IdEstabelecimentoC,
            BancoPostgresFixture.CpfDeTeste(5200 + int.Parse(sufixo)), sufixo, 30000.00m);

        var detalhe = await AbrirECalcularAsync(admin, banco.IdEmpresaC, "02/2029");
        var meu = detalhe.Funcionarios.Single(f => f.Matricula == $"I{sufixo}");

        var holerite = (await HoleriteAsync(admin, detalhe.Folha.Id, meu.Id))!;

        Assert.Equal(988.09m, Inss(holerite)!.Valor);
        Assert.Contains("teto", Inss(holerite)!.Memoria[0].Expressao, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LancamentoManual_ReapuraOInss_SemPrecisarRecalcular()
    {
        // Sem isto, adicionar uma comissao deixaria a base maior e o INSS
        // parado no valor antigo - e o liquido sairia errado ate alguem
        // lembrar de clicar em recalcular.
        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminC);
        var sufixo = Sufixo();

        await RubricaSalarioAsync(admin);
        await RubricaInssAsync(admin);

        using var criacaoComissao = await admin.PostAsJsonAsync("/api/rubricas", new
        {
            codigo = $"CM{sufixo}",
            nome = "Comissao",
            tipo = "Provento",
            estrategia = "ValorInformado",
            basesIncidentes = "Inss, Fgts, Irrf",
        });
        criacaoComissao.EnsureSuccessStatusCode();
        var comissao = (await criacaoComissao.Content.ReadFromJsonAsync<Identificado>())!;

        await ContratoAsync(
            admin, banco.IdEmpresaC, banco.IdEstabelecimentoC,
            BancoPostgresFixture.CpfDeTeste(5300 + int.Parse(sufixo)), sufixo, 3000.00m);

        var detalhe = await AbrirECalcularAsync(admin, banco.IdEmpresaC, "03/2029");
        var meu = detalhe.Funcionarios.Single(f => f.Matricula == $"I{sufixo}");

        var antes = (await HoleriteAsync(admin, detalhe.Folha.Id, meu.Id))!;

        // Base 3.000: 121,575 + 115,3656 + (97,16 x 12% = 11,6592) = 248,5998 -> 248,60
        Assert.Equal(248.60m, Inss(antes)!.Valor);

        using var lancamento = await admin.PostAsJsonAsync(
            $"/api/folhas/{detalhe.Folha.Id}/funcionarios/{meu.Id}/lancamentos",
            new { idRubrica = comissao.Id, valor = 2000.00m, referencia = (string?)null });
        lancamento.EnsureSuccessStatusCode();

        var depois = (await HoleriteAsync(admin, detalhe.Folha.Id, meu.Id))!;

        // Base virou 5.000 e o INSS acompanhou, sem recalcular a folha.
        Assert.Equal(5000.00m, depois.Bases.Single(b => b.Base == "Inss").Valor);
        Assert.Equal(501.51m, Inss(depois)!.Valor);
        Assert.Equal(detalhe.Folha.VersaoCalculo, depois.SituacaoFolha == "Calculada" ? detalhe.Folha.VersaoCalculo : -1);
    }

    [Fact]
    public async Task RemoverLancamento_TambemReapuraOInss()
    {
        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminC);
        var sufixo = Sufixo();

        await RubricaSalarioAsync(admin);
        await RubricaInssAsync(admin);

        using var criacaoBonus = await admin.PostAsJsonAsync("/api/rubricas", new
        {
            codigo = $"BN{sufixo}",
            nome = "Bonus",
            tipo = "Provento",
            estrategia = "ValorInformado",
            basesIncidentes = "Inss",
        });
        criacaoBonus.EnsureSuccessStatusCode();
        var bonus = (await criacaoBonus.Content.ReadFromJsonAsync<Identificado>())!;

        await ContratoAsync(
            admin, banco.IdEmpresaC, banco.IdEstabelecimentoC,
            BancoPostgresFixture.CpfDeTeste(5400 + int.Parse(sufixo)), sufixo, 3000.00m);

        var detalhe = await AbrirECalcularAsync(admin, banco.IdEmpresaC, "04/2029");
        var meu = detalhe.Funcionarios.Single(f => f.Matricula == $"I{sufixo}");

        using var lancamento = await admin.PostAsJsonAsync(
            $"/api/folhas/{detalhe.Folha.Id}/funcionarios/{meu.Id}/lancamentos",
            new { idRubrica = bonus.Id, valor = 2000.00m, referencia = (string?)null });
        lancamento.EnsureSuccessStatusCode();

        var comBonus = (await HoleriteAsync(admin, detalhe.Folha.Id, meu.Id))!;
        Assert.Equal(501.51m, Inss(comBonus)!.Valor);

        var idBonus = comBonus.Lancamentos.Single(l => l.CodigoRubrica == $"BN{sufixo}").Id;

        using var remocao = await admin.DeleteAsync(
            $"/api/folhas/{detalhe.Folha.Id}/funcionarios/{meu.Id}/lancamentos/{idBonus}");
        remocao.EnsureSuccessStatusCode();

        var semBonus = (await HoleriteAsync(admin, detalhe.Folha.Id, meu.Id))!;

        Assert.Equal(3000.00m, semBonus.Bases.Single(b => b.Base == "Inss").Valor);
        Assert.Equal(248.60m, Inss(semBonus)!.Valor);
    }

    [Fact]
    public async Task Recalcular_NaoDuplicaODescontoDeInss()
    {
        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminC);
        var sufixo = Sufixo();

        await RubricaSalarioAsync(admin);
        await RubricaInssAsync(admin);
        await ContratoAsync(
            admin, banco.IdEmpresaC, banco.IdEstabelecimentoC,
            BancoPostgresFixture.CpfDeTeste(5500 + int.Parse(sufixo)), sufixo, 4000.00m);

        var detalhe = await AbrirECalcularAsync(admin, banco.IdEmpresaC, "05/2029");
        var meu = detalhe.Funcionarios.Single(f => f.Matricula == $"I{sufixo}");

        for (var i = 0; i < 3; i++)
        {
            using var recalculo = await admin.PostAsync($"/api/folhas/{detalhe.Folha.Id}/calcular", null);
            recalculo.EnsureSuccessStatusCode();
        }

        var holerite = (await HoleriteAsync(admin, detalhe.Folha.Id, meu.Id))!;

        Assert.Single(holerite.Lancamentos, l => l.CodigoRubrica == "INSS");

        // Base 4.000: 121,575 + 115,3656 + (1.097,16 x 12% = 131,6592) = 368,5998 -> 368,60
        Assert.Equal(368.60m, Inss(holerite)!.Valor);
    }

    [Fact]
    public async Task RubricaDeInss_NaoAceitaValorDigitado()
    {
        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminC);
        var sufixo = Sufixo();

        await RubricaSalarioAsync(admin);
        var idInss = await RubricaInssAsync(admin);
        await ContratoAsync(
            admin, banco.IdEmpresaC, banco.IdEstabelecimentoC,
            BancoPostgresFixture.CpfDeTeste(5600 + int.Parse(sufixo)), sufixo, 3000.00m);

        var detalhe = await AbrirECalcularAsync(admin, banco.IdEmpresaC, "06/2029");
        var meu = detalhe.Funcionarios.Single(f => f.Matricula == $"I{sufixo}");

        using var resposta = await admin.PostAsJsonAsync(
            $"/api/folhas/{detalhe.Folha.Id}/funcionarios/{meu.Id}/lancamentos",
            new { idRubrica = idInss, valor = 1m, referencia = (string?)null });

        // 409 e nao 400: a rubrica existe e o corpo esta bem formado - o que
        // conflita e o estado, porque essa rubrica e calculada pelo sistema.
        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    }

    [Fact]
    public async Task RubricaDeInss_ComoProvento_ERecusada()
    {
        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminC);

        using var resposta = await admin.PostAsJsonAsync("/api/rubricas", new
        {
            codigo = $"IX{Sufixo()}",
            nome = "INSS invertido",
            tipo = "Provento",
            estrategia = "InssProgressivo",
            basesIncidentes = "Nenhuma",
        });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }
}
