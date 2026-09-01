using System.Net;
using System.Net.Http.Json;

namespace PrismaRH.Testes.Isolamento;

/// <summary>
/// Fase 4A ponta a ponta, contra PostgreSQL real.
///
/// Os testes de dominio provam a aritmetica das bases; estes provam o que o
/// EF Core faz com elas. O ponto sensivel e o recalculo: ApurarBases atualiza
/// linhas existentes, e se o grafo nao vier carregado o indice unico
/// ux_bases_apuradas_holerite_base recusa a insercao duplicada.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class BasesDeCalculoIntegracaoTestes(BancoPostgresFixture banco) : IDisposable
{
    private readonly FabricaApiIsolada _fabrica = new(banco.StringConexao);

    public void Dispose() => _fabrica.Dispose();

    private sealed record Identificado(Guid Id);

    private sealed record RubricaItem(
        Guid Id, string Codigo, string Nome, string Tipo, string BasesIncidentes, bool Ativa);

    private sealed record HoleriteResumo(
        Guid Id,
        Guid IdFuncionario,
        string Funcionario,
        string Matricula,
        int Avos,
        int Divisor,
        decimal SalarioReferencia,
        decimal TotalProventos,
        decimal TotalDescontos,
        decimal Liquido);

    private sealed record FolhaResumo(
        Guid Id,
        string Empresa,
        string Competencia,
        string Situacao,
        int VersaoCalculo,
        int QuantidadeFuncionarios,
        decimal TotalProventos,
        decimal TotalDescontos,
        decimal TotalLiquido);

    private sealed record FolhaDetalhe(FolhaResumo Folha, List<HoleriteResumo> Funcionarios);

    private sealed record LinhaMemoria(int Ordem, string Descricao, string Expressao, decimal Valor);

    private sealed record Lancamento(
        Guid Id,
        string CodigoRubrica,
        string NomeRubrica,
        string Tipo,
        string Origem,
        string? Referencia,
        decimal Valor,
        int Ordem,
        string BasesIncidentes,
        List<LinhaMemoria> Memoria);

    private sealed record BaseApurada(string Base, decimal Valor, List<string> Composta);

    private sealed record Holerite(
        HoleriteResumo Resumo,
        string Competencia,
        string SituacaoFolha,
        List<Lancamento> Lancamentos,
        List<BaseApurada> Bases);

    /// <summary>
    /// As competencias sao fixas e EXCLUSIVAS desta classe: 06/2027 a 10/2027.
    ///
    /// A folha e unica por (empresa, competencia), e esta classe compartilha a
    /// fixture do Testcontainers e a empresa A com FolhaMensalTestes, que ja
    /// ocupa 07/2026 a 03/2027. Reaproveitar uma competencia de la faz o
    /// POST /api/folhas devolver 409 e o teste falhar por colisao, nao por
    /// defeito. A proxima subfase comeca em 11/2027.
    /// </summary>
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

        Assert.Equal(HttpStatusCode.Conflict, criacao.StatusCode);

        var existentes = await admin.PaginaDe<RubricaItem>("/api/rubricas");
        var salario = existentes!.Single(r => r.Codigo == "SAL" && r.Ativa);

        // Outra classe de teste pode ter criado a SAL sem incidencia. As bases
        // desta suite dependem dela, entao acerta antes de calcular.
        using var ajuste = await admin.PutAsJsonAsync(
            $"/api/rubricas/{salario.Id}/incidencias",
            new { basesIncidentes = "Inss, Fgts, Irrf" });
        ajuste.EnsureSuccessStatusCode();

        return salario.Id;
    }

    private static async Task<RubricaItem> RubricaAsync(
        HttpClient admin, string codigo, string tipo, string bases)
    {
        using var criacao = await admin.PostAsJsonAsync("/api/rubricas", new
        {
            codigo,
            nome = codigo,
            tipo,
            estrategia = "ValorInformado",
            basesIncidentes = bases,
        });

        criacao.EnsureSuccessStatusCode();

        return (await criacao.Content.ReadFromJsonAsync<RubricaItem>())!;
    }

    private static async Task<Guid> ContratoAsync(
        HttpClient cliente, Guid idEmpresa, Guid idEstabelecimento,
        string cpf, string sufixo, decimal salario, string admissao)
    {
        using var respostaCargo = await cliente.PostAsJsonAsync("/api/cargos", new
        {
            codigo = $"B{sufixo}",
            nome = $"Cargo base {sufixo}",
        });
        respostaCargo.EnsureSuccessStatusCode();
        var cargo = (await respostaCargo.Content.ReadFromJsonAsync<Identificado>())!;

        using var respostaFuncionario = await cliente.PostAsJsonAsync("/api/funcionarios", new
        {
            nome = $"Base Pessoa {sufixo}",
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
                matricula = $"B{sufixo}",
                dataAdmissao = admissao,
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
        using var abertura = await admin.PostAsJsonAsync(
            "/api/folhas", new { idEmpresa, competencia });
        abertura.EnsureSuccessStatusCode();
        var folha = (await abertura.Content.ReadFromJsonAsync<FolhaResumo>())!;

        using var calculo = await admin.PostAsync($"/api/folhas/{folha.Id}/calcular", null);
        calculo.EnsureSuccessStatusCode();

        return (await calculo.Content.ReadFromJsonAsync<FolhaDetalhe>())!;
    }

    private static Task<Holerite?> HoleriteAsync(HttpClient cliente, Guid idFolha, Guid idHolerite) =>
        cliente.GetFromJsonAsync<Holerite>($"/api/folhas/{idFolha}/funcionarios/{idHolerite}");

    private static decimal Base(Holerite holerite, string nome) =>
        holerite.Bases.Single(b => b.Base == nome).Valor;

    // ------------------------------------------------------------- rubrica

    [Fact]
    public async Task RubricaDeDesconto_ComIncidencia_Devolve400()
    {
        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);

        using var resposta = await admin.PostAsJsonAsync("/api/rubricas", new
        {
            codigo = $"VTX{Sufixo()}",
            nome = "Vale-transporte",
            tipo = "Desconto",
            estrategia = "ValorInformado",
            basesIncidentes = "Inss",
        });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Rubrica_PersisteAIncidencia_E_ADevolveComoTexto()
    {
        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var codigo = $"PRM{Sufixo()}";

        await RubricaAsync(admin, codigo, "Provento", "Inss, Fgts");

        var lista = await admin.PaginaDe<RubricaItem>("/api/rubricas");
        var gravada = lista!.Single(r => r.Codigo == codigo);

        // Enum de bits trafega como texto: o contrato nao depende do numero.
        Assert.Contains("Inss", gravada.BasesIncidentes);
        Assert.Contains("Fgts", gravada.BasesIncidentes);
        Assert.DoesNotContain("Irrf", gravada.BasesIncidentes);
    }

    [Fact]
    public async Task AlterarIncidencias_DeRubricaDeDesconto_Devolve400()
    {
        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var rubrica = await RubricaAsync(admin, $"DSC{Sufixo()}", "Desconto", "Nenhuma");

        using var resposta = await admin.PutAsJsonAsync(
            $"/api/rubricas/{rubrica.Id}/incidencias", new { basesIncidentes = "Inss" });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    // --------------------------------------------------------------- bases

    [Fact]
    public async Task Calcular_PersisteAsTresBases()
    {
        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();

        await RubricaSalarioAsync(admin);
        await ContratoAsync(
            admin, banco.IdEmpresaA, banco.IdEstabelecimentoA,
            BancoPostgresFixture.CpfDeTeste(4100 + int.Parse(sufixo)), sufixo, 3000m, "2025-03-01");

        var detalhe = await AbrirECalcularAsync(admin, banco.IdEmpresaA, "06/2027");
        var meu = detalhe.Funcionarios.Single(f => f.Matricula == $"B{sufixo}");

        var holerite = (await HoleriteAsync(admin, detalhe.Folha.Id, meu.Id))!;

        Assert.Equal(3, holerite.Bases.Count);
        Assert.Equal(3000m, Base(holerite, "Inss"));
        Assert.Equal(3000m, Base(holerite, "Fgts"));
        Assert.Equal(3000m, Base(holerite, "Irrf"));

        // A memoria derivada: quais rubricas formaram a base.
        Assert.Equal(["SAL"], holerite.Bases.Single(b => b.Base == "Inss").Composta);
    }

    [Fact]
    public async Task ProventoSomaNaBase_E_DescontoNaoReduz()
    {
        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();

        await RubricaSalarioAsync(admin);
        var comissao = await RubricaAsync(admin, $"COM{sufixo}", "Provento", "Inss, Fgts, Irrf");
        var vale = await RubricaAsync(admin, $"VT{sufixo}", "Desconto", "Nenhuma");

        await ContratoAsync(
            admin, banco.IdEmpresaA, banco.IdEstabelecimentoA,
            BancoPostgresFixture.CpfDeTeste(4200 + int.Parse(sufixo)), sufixo, 3000m, "2025-03-01");

        var detalhe = await AbrirECalcularAsync(admin, banco.IdEmpresaA, "07/2027");
        var meu = detalhe.Funcionarios.Single(f => f.Matricula == $"B{sufixo}");

        using var lancamentoComissao = await admin.PostAsJsonAsync(
            $"/api/folhas/{detalhe.Folha.Id}/funcionarios/{meu.Id}/lancamentos",
            new { idRubrica = comissao.Id, valor = 500m, referencia = (string?)null });
        lancamentoComissao.EnsureSuccessStatusCode();

        using var lancamentoVale = await admin.PostAsJsonAsync(
            $"/api/folhas/{detalhe.Folha.Id}/funcionarios/{meu.Id}/lancamentos",
            new { idRubrica = vale.Id, valor = 180m, referencia = (string?)null });
        lancamentoVale.EnsureSuccessStatusCode();

        var holerite = (await HoleriteAsync(admin, detalhe.Folha.Id, meu.Id))!;

        // 3000 + 500. O desconto de 180 sai do liquido, nao da base.
        Assert.Equal(3500m, Base(holerite, "Inss"));
        Assert.Equal(3320m, holerite.Resumo.Liquido);

        Assert.Equal(2, holerite.Bases.Single(b => b.Base == "Inss").Composta.Count);
        Assert.DoesNotContain($"VT{sufixo}", holerite.Bases.Single(b => b.Base == "Inss").Composta);
    }

    [Fact]
    public async Task Recalcular_NaoDuplicaAsBases_NoBanco()
    {
        // O teste que o indice unico existe para provar: se o grafo nao vier
        // com as bases carregadas, ApurarBases cria tres linhas novas e o
        // banco recusa com violacao de constraint.
        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();

        await RubricaSalarioAsync(admin);
        await ContratoAsync(
            admin, banco.IdEmpresaA, banco.IdEstabelecimentoA,
            BancoPostgresFixture.CpfDeTeste(4300 + int.Parse(sufixo)), sufixo, 3000m, "2025-03-01");

        var detalhe = await AbrirECalcularAsync(admin, banco.IdEmpresaA, "08/2027");
        var meu = detalhe.Funcionarios.Single(f => f.Matricula == $"B{sufixo}");

        using var segundo = await admin.PostAsync($"/api/folhas/{detalhe.Folha.Id}/calcular", null);
        segundo.EnsureSuccessStatusCode();

        using var terceiro = await admin.PostAsync($"/api/folhas/{detalhe.Folha.Id}/calcular", null);
        terceiro.EnsureSuccessStatusCode();

        var holerite = (await HoleriteAsync(admin, detalhe.Folha.Id, meu.Id))!;

        Assert.Equal(3, holerite.Bases.Count);
        Assert.Equal(3000m, Base(holerite, "Inss"));
    }

    [Fact]
    public async Task AlterarIncidencia_NaoMexeNoHoleriteJaCalculado()
    {
        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();

        await RubricaSalarioAsync(admin);
        var premio = await RubricaAsync(admin, $"PRE{sufixo}", "Provento", "Inss");

        await ContratoAsync(
            admin, banco.IdEmpresaA, banco.IdEstabelecimentoA,
            BancoPostgresFixture.CpfDeTeste(4400 + int.Parse(sufixo)), sufixo, 3000m, "2025-03-01");

        var detalhe = await AbrirECalcularAsync(admin, banco.IdEmpresaA, "09/2027");
        var meu = detalhe.Funcionarios.Single(f => f.Matricula == $"B{sufixo}");

        using var lancamento = await admin.PostAsJsonAsync(
            $"/api/folhas/{detalhe.Folha.Id}/funcionarios/{meu.Id}/lancamentos",
            new { idRubrica = premio.Id, valor = 200m, referencia = (string?)null });
        lancamento.EnsureSuccessStatusCode();

        var antes = (await HoleriteAsync(admin, detalhe.Folha.Id, meu.Id))!;
        Assert.Equal(3200m, Base(antes, "Inss"));
        Assert.Equal(3000m, Base(antes, "Fgts"));

        // A lei muda: o premio passa a compor tambem o FGTS.
        using var ajuste = await admin.PutAsJsonAsync(
            $"/api/rubricas/{premio.Id}/incidencias", new { basesIncidentes = "Inss, Fgts" });
        ajuste.EnsureSuccessStatusCode();

        // O holerite ja calculado nao muda: a incidencia esta congelada no
        // lancamento, nao e lida da rubrica.
        var depois = (await HoleriteAsync(admin, detalhe.Folha.Id, meu.Id))!;
        Assert.Equal(3000m, Base(depois, "Fgts"));
    }

    // ----------------------------------------------------------- isolamento

    [Fact]
    public async Task BaseDeOutraOrganizacao_NaoEAcessivel()
    {
        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var vizinha = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminB);
        var sufixo = Sufixo();

        await RubricaSalarioAsync(admin);
        await ContratoAsync(
            admin, banco.IdEmpresaA, banco.IdEstabelecimentoA,
            BancoPostgresFixture.CpfDeTeste(4500 + int.Parse(sufixo)), sufixo, 3000m, "2025-03-01");

        var detalhe = await AbrirECalcularAsync(admin, banco.IdEmpresaA, "10/2027");
        var meu = detalhe.Funcionarios.Single(f => f.Matricula == $"B{sufixo}");

        // 404, nao 403: um 403 confirmaria que este holerite existe.
        using var tentativa = await vizinha.GetAsync(
            $"/api/folhas/{detalhe.Folha.Id}/funcionarios/{meu.Id}");

        Assert.Equal(HttpStatusCode.NotFound, tentativa.StatusCode);
    }

    [Fact]
    public async Task RubricaDeOutraOrganizacao_NaoApareceNaLista()
    {
        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var vizinha = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminB);
        var codigo = $"ISO{Sufixo()}";

        await RubricaAsync(admin, codigo, "Provento", "Inss");

        var daVizinha = await vizinha.PaginaDe<RubricaItem>("/api/rubricas");

        Assert.DoesNotContain(daVizinha!, r => r.Codigo == codigo);
    }
}
