using System.Net;
using System.Net.Http.Json;

namespace PrismaRH.Testes.Isolamento;

/// <summary>
/// A Fase 4C ponta a ponta, contra PostgreSQL real.
///
/// Os testes de dominio provam a conta; estes provam que a aliquota sai do
/// banco, que a linha informativa chega ao holerite com a memoria, e - o item
/// que mais importa - que ela NAO reduz o liquido de ninguem.
///
/// Competencias EXCLUSIVAS desta classe: 01/2031 a 05/2031. Ver a nota em
/// BasesDeCalculoTestes sobre por que competencia repetida derruba o teste por
/// colisao e nao por defeito.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class FgtsIntegracaoTestes(BancoPostgresFixture banco) : IDisposable
{
    private readonly FabricaApiIsolada _fabrica = new(banco.StringConexao);

    public void Dispose() => _fabrica.Dispose();

    private sealed record Identificado(Guid Id);

    private sealed record RubricaItem(
        Guid Id, string Codigo, string Nome, string Tipo, string Estrategia, string BasesIncidentes, bool Ativa);

    private sealed record TabelaFgtsItem(
        Guid Id, DateOnly VigenciaInicio, decimal Aliquota, decimal AliquotaPercentual, string Fonte, bool Vigente);

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

        var existentes = await admin.PaginaDe<RubricaItem>("/api/rubricas");
        var salario = existentes!.Single(r => r.Codigo == "SAL" && r.Ativa);

        using var ajuste = await admin.PutAsJsonAsync(
            $"/api/rubricas/{salario.Id}/incidencias", new { basesIncidentes = "Inss, Fgts, Irrf" });
        ajuste.EnsureSuccessStatusCode();
    }

    /// <summary>Garante a rubrica de FGTS da organizacao. So pode haver uma ativa.</summary>
    private static async Task<Guid> RubricaFgtsAsync(HttpClient admin)
    {
        using var criacao = await admin.PostAsJsonAsync("/api/rubricas", new
        {
            codigo = "FGTS",
            nome = "FGTS sobre a folha",
            tipo = "Informativo",
            estrategia = "FgtsMensal",
            basesIncidentes = "Nenhuma",
        });

        if (criacao.StatusCode == HttpStatusCode.Created)
        {
            return (await criacao.Content.ReadFromJsonAsync<Identificado>())!.Id;
        }

        var existentes = await admin.PaginaDe<RubricaItem>("/api/rubricas");

        return existentes!.Single(r => r.Estrategia == "FgtsMensal" && r.Ativa).Id;
    }

    private static async Task<Guid> ContratoAsync(
        HttpClient cliente, Guid idEmpresa, Guid idEstabelecimento,
        string cpf, string sufixo, decimal salario)
    {
        using var respostaCargo = await cliente.PostAsJsonAsync("/api/cargos", new
        {
            codigo = $"G{sufixo}",
            nome = $"Cargo fgts {sufixo}",
        });
        respostaCargo.EnsureSuccessStatusCode();
        var cargo = (await respostaCargo.Content.ReadFromJsonAsync<Identificado>())!;

        using var respostaFuncionario = await cliente.PostAsJsonAsync("/api/funcionarios", new
        {
            nome = $"Fgts Pessoa {sufixo}",
            cpf,
            dataNascimento = "1992-08-14",
        });
        respostaFuncionario.EnsureSuccessStatusCode();
        var funcionario = (await respostaFuncionario.Content.ReadFromJsonAsync<Identificado>())!;

        using var respostaContrato = await cliente.PostAsJsonAsync(
            $"/api/funcionarios/{funcionario.Id}/contratos",
            new
            {
                idEmpresa,
                matricula = $"G{sufixo}",
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

    private static Lancamento? Fgts(Holerite h) =>
        h.Lancamentos.SingleOrDefault(l => l.CodigoRubrica == "FGTS");

    // ------------------------------------------------------------- tabela

    [Fact]
    public async Task Aliquota_EstaCadastrada_ComFonteOficial()
    {
        var leitor = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminD);

        var tabelas = await leitor.GetFromJsonAsync<List<TabelaFgtsItem>>("/api/tabelas-fgts");
        var lei = tabelas!.Single(t => t.VigenciaInicio == new DateOnly(1990, 5, 11));

        Assert.Equal(0.08m, lei.Aliquota);
        Assert.Equal(8m, lei.AliquotaPercentual);
        Assert.Contains("Lei n. 8.036", lei.Fonte);
        Assert.True(lei.Vigente);
    }

    [Fact]
    public async Task Aliquota_SoOAdministradorDaPlataformaCadastra()
    {
        var adminEmpresa = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminD);

        using var resposta = await adminEmpresa.PostAsJsonAsync("/api/tabelas-fgts", new
        {
            vigenciaInicio = "2040-01-01",
            aliquota = 0.09m,
            fonte = "Tentativa indevida",
        });

        // Parametro legal federal: um Administrador de Empresa mudando a
        // aliquota mudaria o deposito de TODAS as organizacoes.
        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    [Fact]
    public async Task Aliquota_EmPercentual_ERecusada()
    {
        var plataforma = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailPlataformaA);

        using var resposta = await plataforma.PostAsJsonAsync("/api/tabelas-fgts", new
        {
            vigenciaInicio = "2041-01-01",
            aliquota = 8m,   // 8 em vez de 0.08
            fonte = "Fonte qualquer",
        });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Aliquota_SemFonte_ERecusada()
    {
        var plataforma = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailPlataformaA);

        using var resposta = await plataforma.PostAsJsonAsync("/api/tabelas-fgts", new
        {
            vigenciaInicio = "2042-01-01",
            aliquota = 0.08m,
            fonte = "   ",
        });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task VigenciaRepetida_ERecusada()
    {
        var plataforma = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailPlataformaA);

        using var resposta = await plataforma.PostAsJsonAsync("/api/tabelas-fgts", new
        {
            vigenciaInicio = "1990-05-11",
            aliquota = 0.08m,
            fonte = "Duplicata",
        });

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    }

    // --------------------------------------------------------- na rubrica

    [Fact]
    public async Task RubricaDeFgts_ComoDesconto_ERecusada()
    {
        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminD);

        using var resposta = await admin.PostAsJsonAsync("/api/rubricas", new
        {
            codigo = $"FG{Sufixo()}",
            nome = "FGTS errado",
            tipo = "Desconto",
            estrategia = "FgtsMensal",
            basesIncidentes = "Nenhuma",
        });

        // O erro caro: 8% saindo do salario do funcionario, com o holerite
        // continuando a fechar.
        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task RubricaDeFgts_NaoAceitaValorDigitado()
    {
        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminD);
        var sufixo = Sufixo();

        await RubricaSalarioAsync(admin);
        var idFgts = await RubricaFgtsAsync(admin);
        await ContratoAsync(
            admin, banco.IdEmpresaD, banco.IdEstabelecimentoD,
            BancoPostgresFixture.CpfDeTeste(7300 + int.Parse(sufixo)), sufixo, 3000.00m);

        var detalhe = await AbrirECalcularAsync(admin, banco.IdEmpresaD, "01/2031");
        var meu = detalhe.Funcionarios.Single(f => f.Matricula == $"G{sufixo}");

        using var resposta = await admin.PostAsJsonAsync(
            $"/api/folhas/{detalhe.Folha.Id}/funcionarios/{meu.Id}/lancamentos",
            new { idRubrica = idFgts, valor = 1.00m });

        // Digitar o FGTS a mao permitiria recolher um valor que a base nao
        // sustenta. Ele sai da aliquota, sempre.
        //
        // 409 e nao 400, pelo mesmo contrato ja adotado no INSS: a rubrica
        // existe e o corpo esta bem formado - o que conflita e o estado,
        // porque essa rubrica e calculada pelo sistema.
        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    }

    // ---------------------------------------------------------- na folha

    [Fact]
    public async Task Holerite_TemODepositoInformativo_SemReduzirOLiquido()
    {
        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminD);
        var sufixo = Sufixo();

        await RubricaSalarioAsync(admin);
        await RubricaFgtsAsync(admin);
        await ContratoAsync(
            admin, banco.IdEmpresaD, banco.IdEstabelecimentoD,
            BancoPostgresFixture.CpfDeTeste(7400 + int.Parse(sufixo)), sufixo, 3000.00m);

        var detalhe = await AbrirECalcularAsync(admin, banco.IdEmpresaD, "02/2031");
        var meu = detalhe.Funcionarios.Single(f => f.Matricula == $"G{sufixo}");

        var holerite = (await HoleriteAsync(admin, detalhe.Folha.Id, meu.Id))!;
        var fgts = Fgts(holerite);

        Assert.NotNull(fgts);
        Assert.Equal("Informativo", fgts!.Tipo);
        Assert.Equal("Calculado", fgts.Origem);
        Assert.Equal(240.00m, fgts.Valor);

        // O que este teste existe para provar: o deposito e do empregador.
        Assert.Equal(3000.00m, holerite.Resumo.Liquido);
        Assert.Equal(0m, holerite.Resumo.TotalDescontos);
        Assert.Equal(3000.00m, holerite.Resumo.TotalProventos);

        // E ele nao entra na propria base: 3.000, nao 3.240.
        Assert.Equal(3000.00m, holerite.Bases.Single(b => b.Base == "Fgts").Valor);

        // A memoria sobreviveu ao banco: base + deposito.
        Assert.Equal(2, fgts.Memoria.Count);
        Assert.Equal("Base de cálculo do FGTS", fgts.Memoria[0].Descricao);
        Assert.Equal("3.000,00 x 8%", fgts.Memoria[1].Expressao);
        Assert.Equal(240.00m, fgts.Memoria[1].Valor);
    }

    [Fact]
    public async Task AcimaDoTetoDoInss_ODepositoContinuaSobreTudo()
    {
        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminD);
        var sufixo = Sufixo();

        await RubricaSalarioAsync(admin);
        await RubricaFgtsAsync(admin);
        await ContratoAsync(
            admin, banco.IdEmpresaD, banco.IdEstabelecimentoD,
            BancoPostgresFixture.CpfDeTeste(7500 + int.Parse(sufixo)), sufixo, 20000.00m);

        var detalhe = await AbrirECalcularAsync(admin, banco.IdEmpresaD, "03/2031");
        var meu = detalhe.Funcionarios.Single(f => f.Matricula == $"G{sufixo}");

        var holerite = (await HoleriteAsync(admin, detalhe.Folha.Id, meu.Id))!;

        // O teto de 8.475,55 e do INSS. O FGTS nao tem teto.
        Assert.Equal(1600.00m, Fgts(holerite)!.Valor);
    }

    [Fact]
    public async Task LancamentoManual_ReapuraODeposito_SemPrecisarRecalcular()
    {
        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminD);
        var sufixo = Sufixo();

        await RubricaSalarioAsync(admin);
        await RubricaFgtsAsync(admin);
        await ContratoAsync(
            admin, banco.IdEmpresaD, banco.IdEstabelecimentoD,
            BancoPostgresFixture.CpfDeTeste(7600 + int.Parse(sufixo)), sufixo, 3000.00m);

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

        var detalhe = await AbrirECalcularAsync(admin, banco.IdEmpresaD, "04/2031");
        var meu = detalhe.Funcionarios.Single(f => f.Matricula == $"G{sufixo}");

        using var lancamento = await admin.PostAsJsonAsync(
            $"/api/folhas/{detalhe.Folha.Id}/funcionarios/{meu.Id}/lancamentos",
            new { idRubrica = comissao.Id, valor = 1000.00m });
        lancamento.EnsureSuccessStatusCode();

        var holerite = (await HoleriteAsync(admin, detalhe.Folha.Id, meu.Id))!;

        // Base virou 4.000 e o deposito acompanhou, sem recalcular a folha.
        Assert.Equal(4000.00m, holerite.Bases.Single(b => b.Base == "Fgts").Valor);
        Assert.Equal(320.00m, Fgts(holerite)!.Valor);

        // E continua nao mexendo no liquido, que agora e salario + comissao.
        Assert.Equal(4000.00m, holerite.Resumo.Liquido);
    }

    [Fact]
    public async Task Recalcular_NaoDuplicaODeposito()
    {
        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminD);
        var sufixo = Sufixo();

        await RubricaSalarioAsync(admin);
        await RubricaFgtsAsync(admin);
        await ContratoAsync(
            admin, banco.IdEmpresaD, banco.IdEstabelecimentoD,
            BancoPostgresFixture.CpfDeTeste(7700 + int.Parse(sufixo)), sufixo, 3000.00m);

        var detalhe = await AbrirECalcularAsync(admin, banco.IdEmpresaD, "05/2031");

        using var recalculo = await admin.PostAsync($"/api/folhas/{detalhe.Folha.Id}/calcular", null);
        recalculo.EnsureSuccessStatusCode();
        var depois = (await recalculo.Content.ReadFromJsonAsync<FolhaDetalhe>())!;

        var meu = depois.Funcionarios.Single(f => f.Matricula == $"G{sufixo}");
        var holerite = (await HoleriteAsync(admin, depois.Folha.Id, meu.Id))!;

        Assert.Single(holerite.Lancamentos, l => l.CodigoRubrica == "FGTS");
        Assert.Equal(240.00m, Fgts(holerite)!.Valor);
    }

    // ------------------------------------------------------- multiempresa

    [Fact]
    public async Task RubricaDeFgts_DeOutraOrganizacao_NaoAparece()
    {
        var adminD = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminD);
        await RubricaFgtsAsync(adminD);

        var adminB = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminB);
        var rubricasB = await adminB.PaginaDe<RubricaItem>("/api/rubricas");

        Assert.DoesNotContain(rubricasB!, r => r.Estrategia == "FgtsMensal");
    }

    [Fact]
    public async Task Aliquota_ELidaPorTodasAsOrganizacoes()
    {
        // Contraprova do teste acima: a tabela FEDERAL nao tem dono. Se ela
        // estivesse sob o filtro global, a organizacao B nao veria nada e a
        // folha dela sairia sem FGTS.
        var adminB = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminB);

        var tabelas = await adminB.GetFromJsonAsync<List<TabelaFgtsItem>>("/api/tabelas-fgts");

        Assert.Contains(tabelas!, t => t.VigenciaInicio == new DateOnly(1990, 5, 11));
    }
}
