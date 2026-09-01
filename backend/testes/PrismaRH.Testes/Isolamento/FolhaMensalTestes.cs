using System.Net;
using System.Net.Http.Json;

namespace PrismaRH.Testes.Isolamento;

/// <summary>
/// A folha mensal ponta a ponta, contra PostgreSQL real.
///
/// Os testes de dominio provam a aritmetica; estes provam o que o EF Core faz
/// com ela. Foi exatamente nessa fronteira que os dois defeitos da Fase 2
/// apareceram - e o recalculo, que apaga e recria lancamentos dentro de um
/// grafo rastreado, e o candidato natural ao proximo.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class FolhaMensalTestes(BancoPostgresFixture banco) : IDisposable
{
    private readonly FabricaApiIsolada _fabrica = new(banco.StringConexao);

    public void Dispose() => _fabrica.Dispose();

    private sealed record Identificado(Guid Id);

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
        List<LinhaMemoria> Memoria);

    private sealed record Holerite(
        HoleriteResumo Resumo, string Competencia, string SituacaoFolha, List<Lancamento> Lancamentos);

    private static int _sufixo;

    private static string Sufixo() => Interlocked.Increment(ref _sufixo).ToString("D4");

    /// <summary>
    /// Garante a rubrica de salario-base da organizacao. So pode existir uma
    /// ativa, entao criar de novo devolve 409 - e isso e sucesso aqui.
    /// </summary>
    private static async Task<Guid> RubricaSalarioAsync(HttpClient admin)
    {
        using var criacao = await admin.PostAsJsonAsync("/api/rubricas", new
        {
            codigo = "SAL",
            nome = "Salario base",
            tipo = "Provento",
            estrategia = "SalarioBaseProporcional",
        });

        if (criacao.StatusCode == HttpStatusCode.Created)
        {
            return (await criacao.Content.ReadFromJsonAsync<Identificado>())!.Id;
        }

        Assert.Equal(HttpStatusCode.Conflict, criacao.StatusCode);

        var existentes = await admin.PaginaDe<RubricaItem>("/api/rubricas");

        return existentes!.Single(r => r.Codigo == "SAL" && r.Ativa).Id;
    }

    private sealed record RubricaItem(Guid Id, string Codigo, string Nome, bool Ativa);

    private sealed record CargoItem(Guid Id, string Codigo, string Nome, bool Ativo);

    private static async Task<Guid> RubricaAsync(
        HttpClient admin, string codigo, string nome, string tipo)
    {
        using var criacao = await admin.PostAsJsonAsync("/api/rubricas", new
        {
            codigo,
            nome,
            tipo,
            estrategia = "ValorInformado",
        });

        if (criacao.StatusCode == HttpStatusCode.Created)
        {
            return (await criacao.Content.ReadFromJsonAsync<Identificado>())!.Id;
        }

        var existentes = await admin.PaginaDe<RubricaItem>("/api/rubricas");

        return existentes!.Single(r => r.Codigo == codigo).Id;
    }

    /// <summary>Cria funcionario com contrato na empresa A e devolve o id do contrato.</summary>
    private static async Task<Guid> ContratoAsync(
        HttpClient cliente,
        Guid idEmpresa,
        Guid idEstabelecimento,
        string cpf,
        string sufixo,
        decimal salario,
        string admissao)
    {
        using var respostaCargo = await cliente.PostAsJsonAsync("/api/cargos", new
        {
            codigo = $"F{sufixo}",
            nome = $"Cargo folha {sufixo}",
        });
        respostaCargo.EnsureSuccessStatusCode();
        var cargo = (await respostaCargo.Content.ReadFromJsonAsync<Identificado>())!;

        using var respostaFuncionario = await cliente.PostAsJsonAsync("/api/funcionarios", new
        {
            nome = $"Folha Pessoa {sufixo}",
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
                matricula = $"F{sufixo}",
                dataAdmissao = admissao,
                salarioInicial = salario,
                idCargo = cargo.Id,
                idEstabelecimento,
                jornadaMensalHoras = 220,
            });
        respostaContrato.EnsureSuccessStatusCode();

        return (await respostaContrato.Content.ReadFromJsonAsync<Identificado>())!.Id;
    }

    private static async Task<FolhaResumo> AbrirAsync(HttpClient cliente, Guid idEmpresa, string competencia)
    {
        using var resposta = await cliente.PostAsJsonAsync("/api/folhas", new { idEmpresa, competencia });
        resposta.EnsureSuccessStatusCode();

        return (await resposta.Content.ReadFromJsonAsync<FolhaResumo>())!;
    }

    private static async Task<FolhaDetalhe> CalcularAsync(HttpClient cliente, Guid idFolha)
    {
        using var resposta = await cliente.PostAsync($"/api/folhas/{idFolha}/calcular", null);
        resposta.EnsureSuccessStatusCode();

        return (await resposta.Content.ReadFromJsonAsync<FolhaDetalhe>())!;
    }

    // ------------------------------------------------------------------ calculo

    [Fact]
    public async Task Calcular_PersisteOValor_E_AMemoriaDeCalculo()
    {
        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();

        await RubricaSalarioAsync(admin);
        await ContratoAsync(
            admin, banco.IdEmpresaA, banco.IdEstabelecimentoA,
            BancoPostgresFixture.CpfDeTeste(9000 + int.Parse(sufixo)), sufixo, 3000m, "2025-03-01");

        var folha = await AbrirAsync(admin, banco.IdEmpresaA, "07/2026");
        Assert.Equal("Rascunho", folha.Situacao);

        var detalhe = await CalcularAsync(admin, folha.Id);

        Assert.Equal("Calculada", detalhe.Folha.Situacao);
        Assert.Equal(1, detalhe.Folha.VersaoCalculo);

        var meu = detalhe.Funcionarios.Single(f => f.Matricula == $"F{sufixo}");
        Assert.Equal(30, meu.Avos);
        Assert.Equal(3000m, meu.SalarioReferencia);
        Assert.Equal(3000m, meu.Liquido);

        // A memoria de calculo sobreviveu ao banco - e o que o ROADMAP chama
        // de "exibir memoria de calculo".
        var holerite = await admin.GetFromJsonAsync<Holerite>(
            $"/api/folhas/{folha.Id}/funcionarios/{meu.Id}");

        var lancamento = Assert.Single(holerite!.Lancamentos);
        Assert.Equal("SAL", lancamento.CodigoRubrica);
        Assert.Equal("Calculado", lancamento.Origem);
        Assert.Equal("30/30", lancamento.Referencia);

        var passo = Assert.Single(lancamento.Memoria);
        Assert.Equal("3.000,00 x 30/30", passo.Expressao);
        Assert.Equal(3000m, passo.Valor);
    }

    [Fact]
    public async Task Recalcular_NaoDuplicaLancamento_E_PreservaOManual()
    {
        // O teste que o EF Core pode reprovar mesmo com o dominio correto:
        // remover lancamentos calculados de um grafo rastreado e inserir novos
        // com chave ja preenchida foi a origem dos dois defeitos da Fase 2.
        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();

        await RubricaSalarioAsync(admin);
        var idComissao = await RubricaAsync(admin, "COM", "Comissao", "Provento");

        await ContratoAsync(
            admin, banco.IdEmpresaA, banco.IdEstabelecimentoA,
            BancoPostgresFixture.CpfDeTeste(9000 + int.Parse(sufixo)), sufixo, 3000m, "2025-03-01");

        var folha = await AbrirAsync(admin, banco.IdEmpresaA, "08/2026");
        var detalhe = await CalcularAsync(admin, folha.Id);
        var meu = detalhe.Funcionarios.Single(f => f.Matricula == $"F{sufixo}");

        using var lancamento = await admin.PostAsJsonAsync(
            $"/api/folhas/{folha.Id}/funcionarios/{meu.Id}/lancamentos",
            new { idRubrica = idComissao, valor = 450m, referencia = (string?)null });
        lancamento.EnsureSuccessStatusCode();

        var segundo = await CalcularAsync(admin, folha.Id);
        var depois = segundo.Funcionarios.Single(f => f.Matricula == $"F{sufixo}");

        Assert.Equal(2, segundo.Folha.VersaoCalculo);
        Assert.Equal(3450m, depois.Liquido);

        var holerite = await admin.GetFromJsonAsync<Holerite>(
            $"/api/folhas/{folha.Id}/funcionarios/{meu.Id}");

        Assert.Equal(2, holerite!.Lancamentos.Count);
        Assert.Single(holerite.Lancamentos, l => l.CodigoRubrica == "SAL");
        Assert.Single(holerite.Lancamentos, l => l.CodigoRubrica == "COM" && l.Valor == 450m);

        // O salario encabeca o holerite mesmo tendo sido recriado depois.
        Assert.Equal("SAL", holerite.Lancamentos[0].CodigoRubrica);
    }

    [Fact]
    public async Task Aumento_NoMeioDoMes_ChegaRepartidoAteOBanco()
    {
        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();

        await RubricaSalarioAsync(admin);
        var idContrato = await ContratoAsync(
            admin, banco.IdEmpresaA, banco.IdEstabelecimentoA,
            BancoPostgresFixture.CpfDeTeste(9000 + int.Parse(sufixo)), sufixo, 3000m, "2025-03-01");

        using var alteracao = await admin.PostAsJsonAsync(
            $"/api/contratos/{idContrato}/vigencias",
            new
            {
                validoDe = "2026-09-15",
                salario = 3600m,
                idCargo = (await admin.PaginaDe<CargoItem>("/api/cargos"))!
                    .First(c => c.Codigo == $"F{sufixo}").Id,
                idEstabelecimento = banco.IdEstabelecimentoA,
                jornadaMensalHoras = 220,
                motivo = "AlteracaoSalarial",
            });
        alteracao.EnsureSuccessStatusCode();

        var folha = await AbrirAsync(admin, banco.IdEmpresaA, "09/2026");
        var detalhe = await CalcularAsync(admin, folha.Id);
        var meu = detalhe.Funcionarios.Single(f => f.Matricula == $"F{sufixo}");

        // 3000 x 14/30 + 3600 x 16/30 = 1400 + 1920.
        Assert.Equal(3320m, meu.Liquido);
        Assert.Equal(3600m, meu.SalarioReferencia);

        var holerite = await admin.GetFromJsonAsync<Holerite>(
            $"/api/folhas/{folha.Id}/funcionarios/{meu.Id}");

        // Dois trechos mais a soma, na ordem, persistidos.
        var memoria = Assert.Single(holerite!.Lancamentos).Memoria;
        Assert.Equal(3, memoria.Count);
        Assert.Equal([1, 2, 3], memoria.Select(m => m.Ordem));
        Assert.Equal(1400m, memoria[0].Valor);
        Assert.Equal(1920m, memoria[1].Valor);
        Assert.Equal(3320m, memoria[2].Valor);
    }

    // --------------------------------------------------------------- fechamento

    [Fact]
    public async Task Fechar_TravaAFolha_E_ORecalculoPassaASerRecusado()
    {
        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();

        await RubricaSalarioAsync(admin);
        await ContratoAsync(
            admin, banco.IdEmpresaA, banco.IdEstabelecimentoA,
            BancoPostgresFixture.CpfDeTeste(9000 + int.Parse(sufixo)), sufixo, 3000m, "2025-03-01");

        var folha = await AbrirAsync(admin, banco.IdEmpresaA, "10/2026");
        await CalcularAsync(admin, folha.Id);

        using var fechamento = await admin.PostAsync($"/api/folhas/{folha.Id}/fechar", null);
        fechamento.EnsureSuccessStatusCode();

        var fechada = (await fechamento.Content.ReadFromJsonAsync<FolhaResumo>())!;
        Assert.Equal("Fechada", fechada.Situacao);

        // 409, e nao 500: a folha fechada e um fato historico, nao um erro.
        using var recalculo = await admin.PostAsync($"/api/folhas/{folha.Id}/calcular", null);
        Assert.Equal(HttpStatusCode.Conflict, recalculo.StatusCode);

        using var deNovo = await admin.PostAsync($"/api/folhas/{folha.Id}/fechar", null);
        Assert.Equal(HttpStatusCode.Conflict, deNovo.StatusCode);
    }

    [Fact]
    public async Task AbrirDuasVezes_AMesmaCompetencia_Recusado()
    {
        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var competencia = "11/2026";

        await AbrirAsync(admin, banco.IdEmpresaA, competencia);

        using var segunda = await admin.PostAsJsonAsync(
            "/api/folhas", new { idEmpresa = banco.IdEmpresaA, competencia });

        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
    }

    [Fact]
    public async Task Calcular_SemRubricaDeSalario_Explica_EmVezDeCalcularZero()
    {
        // Usa a organizacao B de proposito: todos os outros testes desta
        // classe criam a rubrica de salario em A, e aqui o cenario e
        // justamente a organizacao que ainda NAO tem nenhuma.
        var adminB = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminB);
        var folha = await AbrirAsync(adminB, banco.IdEmpresaB, "12/2026");

        using var resposta = await adminB.PostAsync($"/api/folhas/{folha.Id}/calcular", null);

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    }

    // ----------------------------------------------------- isolamento e perfis

    [Fact]
    public async Task Folha_DeOutraOrganizacao_NaoAparece_E_Devolve404()
    {
        var adminA = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var adminB = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminB);

        var folha = await AbrirAsync(adminA, banco.IdEmpresaA, "01/2027");

        // PRESENCA: a propria organizacao enxerga.
        var listaA = await adminA.PaginaDe<FolhaResumo>("/api/folhas");
        Assert.Contains(listaA!, f => f.Id == folha.Id);

        // AUSENCIA: a vizinha nao.
        var listaB = await adminB.PaginaDe<FolhaResumo>("/api/folhas");
        Assert.DoesNotContain(listaB!, f => f.Id == folha.Id);

        using var acessoDireto = await adminB.GetAsync($"/api/folhas/{folha.Id}");
        Assert.Equal(HttpStatusCode.NotFound, acessoDireto.StatusCode);

        // E nao consegue nem calcular a folha alheia.
        using var calculoAlheio = await adminB.PostAsync($"/api/folhas/{folha.Id}/calcular", null);
        Assert.Equal(HttpStatusCode.NotFound, calculoAlheio.StatusCode);
    }

    [Fact]
    public async Task Analista_ProcessaFolha_Visualizador_E_Auditor_Nao()
    {
        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var analista = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAnalistaA);
        var visualizador = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailVisualizadorA);
        var auditor = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAuditorA);

        await RubricaSalarioAsync(admin);

        // O Analista de RH processa folha (CLAUDE.md secao 6).
        using var abertura = await analista.PostAsJsonAsync(
            "/api/folhas", new { idEmpresa = banco.IdEmpresaA, competencia = "02/2027" });
        Assert.Equal(HttpStatusCode.Created, abertura.StatusCode);

        var folha = (await abertura.Content.ReadFromJsonAsync<FolhaResumo>())!;

        // Auditor e Visualizador LEEM.
        using var leituraAuditor = await auditor.GetAsync($"/api/folhas/{folha.Id}");
        Assert.Equal(HttpStatusCode.OK, leituraAuditor.StatusCode);

        using var leituraVisualizador = await visualizador.GetAsync("/api/folhas");
        Assert.Equal(HttpStatusCode.OK, leituraVisualizador.StatusCode);

        // Mas nao PROCESSAM.
        using var calculoAuditor = await auditor.PostAsync($"/api/folhas/{folha.Id}/calcular", null);
        Assert.Equal(HttpStatusCode.Forbidden, calculoAuditor.StatusCode);

        using var fechamentoVisualizador = await visualizador.PostAsync($"/api/folhas/{folha.Id}/fechar", null);
        Assert.Equal(HttpStatusCode.Forbidden, fechamentoVisualizador.StatusCode);

        // E o Analista nao cria rubrica: isso e parametrizacao da empresa.
        using var rubricaAnalista = await analista.PostAsJsonAsync("/api/rubricas", new
        {
            codigo = "NAO",
            nome = "Nao deveria",
            tipo = "Provento",
            estrategia = "ValorInformado",
        });
        Assert.Equal(HttpStatusCode.Forbidden, rubricaAnalista.StatusCode);
    }

    [Fact]
    public async Task LancamentoManual_NaRubricaDeSalario_Recusado()
    {
        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();

        var idSalario = await RubricaSalarioAsync(admin);
        await ContratoAsync(
            admin, banco.IdEmpresaA, banco.IdEstabelecimentoA,
            BancoPostgresFixture.CpfDeTeste(9000 + int.Parse(sufixo)), sufixo, 3000m, "2025-03-01");

        var folha = await AbrirAsync(admin, banco.IdEmpresaA, "03/2027");
        var detalhe = await CalcularAsync(admin, folha.Id);
        var meu = detalhe.Funcionarios.Single(f => f.Matricula == $"F{sufixo}");

        using var resposta = await admin.PostAsJsonAsync(
            $"/api/folhas/{folha.Id}/funcionarios/{meu.Id}/lancamentos",
            new { idRubrica = idSalario, valor = 9999m, referencia = (string?)null });

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    }
}
