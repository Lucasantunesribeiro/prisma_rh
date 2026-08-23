using PrismaRH.Dominio.Contratos;
using PrismaRH.Dominio.Folha;

namespace PrismaRH.Testes.Dominio;

/// <summary>
/// O agregado da folha. Aqui se prova o que o ROADMAP chama de
/// "reprocessamento controlado": recalcular tem que ser seguro o suficiente
/// para o analista clicar sem medo, e fechar tem que ser definitivo.
/// </summary>
public class FolhaPagamentoTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Org = Guid.CreateVersion7();
    private static readonly Guid Empresa = Guid.CreateVersion7();
    private static readonly Guid OutraEmpresa = Guid.CreateVersion7();
    private static readonly Guid CargoUm = Guid.CreateVersion7();
    private static readonly Guid Matriz = Guid.CreateVersion7();

    private static readonly Competencia Agosto = new(2026, 8);

    private static Rubrica Salario() =>
        new(Org, "SAL", "Salario base", TipoRubrica.Provento, EstrategiaRubrica.SalarioBaseProporcional, Agora);

    private static Rubrica Comissao() =>
        new(Org, "COM", "Comissao", TipoRubrica.Provento, EstrategiaRubrica.ValorInformado, Agora);

    private static Rubrica ValeTransporte() =>
        new(Org, "VT", "Vale-transporte", TipoRubrica.Desconto, EstrategiaRubrica.ValorInformado, Agora);

    private static ContratoTrabalho Contrato(
        string matricula, decimal salario = 3000m, DateOnly? admissao = null, Guid? empresa = null) =>
        new(Org, Guid.CreateVersion7(), empresa ?? Empresa, matricula,
            admissao ?? new DateOnly(2025, 3, 1), salario, CargoUm, Matriz, 220, Agora);

    private static FolhaPagamento Nova() => new(Org, Empresa, Agosto, Agora);

    // -----------------------------------------------------------------------
    // Calculo
    // -----------------------------------------------------------------------

    [Fact]
    public void Calcular_IncluiOsElegiveis_E_SomaOsTotais()
    {
        var folha = Nova();
        var contratos = new[] { Contrato("001"), Contrato("002", 5000m) };

        folha.Calcular(contratos, Salario(), Agora);

        Assert.Equal(2, folha.Funcionarios.Count);
        Assert.Equal(8000m, folha.TotalProventos);
        Assert.Equal(0m, folha.TotalDescontos);
        Assert.Equal(8000m, folha.TotalLiquido);
        Assert.Equal(SituacaoFolha.Calculada, folha.Situacao);
        Assert.Equal(1, folha.VersaoCalculo);
        Assert.Equal(Agora, folha.CalculadaEm);
    }

    [Fact]
    public void Calcular_IgnoraContratoDeOutraEmpresa()
    {
        var folha = Nova();

        folha.Calcular([Contrato("001"), Contrato("999", empresa: OutraEmpresa)], Salario(), Agora);

        var holerite = Assert.Single(folha.Funcionarios);
        Assert.Equal(3000m, holerite.TotalProventos);
    }

    [Fact]
    public void Calcular_IgnoraQuemNaoTeveVinculoNaCompetencia()
    {
        var folha = Nova();

        folha.Calcular([Contrato("002", admissao: new DateOnly(2026, 10, 1))], Salario(), Agora);

        Assert.Empty(folha.Funcionarios);
        Assert.Equal(0m, folha.TotalLiquido);
    }

    [Fact]
    public void Calcular_ComRubricaQueNaoEDeSalario_Recusa()
    {
        var folha = Nova();

        var erro = Assert.Throws<ArgumentException>(
            () => folha.Calcular([Contrato("001")], Comissao(), Agora));

        Assert.Contains("nao e a rubrica de salario-base", erro.Message);
    }

    [Fact]
    public void Calcular_GravaAMemoriaDoLancamento()
    {
        var folha = Nova();
        folha.Calcular([Contrato("001")], Salario(), Agora);

        var lancamento = Assert.Single(folha.Funcionarios[0].Lancamentos);

        Assert.Equal("SAL", lancamento.CodigoRubrica);
        Assert.Equal(OrigemLancamento.Calculado, lancamento.Origem);
        Assert.Equal("30/30", lancamento.Referencia);

        var passo = Assert.Single(lancamento.Memoria);
        Assert.Equal(1, passo.Ordem);
        Assert.Equal("3.000,00 x 30/30", passo.Expressao);
    }

    // -----------------------------------------------------------------------
    // Reprocessamento
    // -----------------------------------------------------------------------

    [Fact]
    public void Recalcular_NaoDuplicaOSalario_E_PreservaOsManuais()
    {
        // O teste que justifica a existencia de OrigemLancamento. Sem ele, o
        // segundo calculo ou duplicaria o salario ou apagaria o que o analista
        // digitou - e as duas falhas so apareceriam no total.
        var folha = Nova();
        var contratos = new[] { Contrato("001") };

        folha.Calcular(contratos, Salario(), Agora);
        folha.AdicionarLancamentoManual(folha.Funcionarios[0].Id, Comissao(), 450m, null);

        folha.Calcular(contratos, Salario(), Agora.AddHours(1));

        var holerite = Assert.Single(folha.Funcionarios);
        Assert.Equal(2, holerite.Lancamentos.Count);
        Assert.Single(holerite.Lancamentos, l => l.Origem == OrigemLancamento.Calculado);
        Assert.Single(holerite.Lancamentos, l => l.CodigoRubrica == "COM" && l.Valor == 450m);
        Assert.Equal(3450m, holerite.Liquido);
        Assert.Equal(2, folha.VersaoCalculo);
    }

    [Fact]
    public void Recalcular_DepoisDeUmAumento_AtualizaOValor()
    {
        var folha = Nova();
        var contrato = Contrato("001");

        folha.Calcular([contrato], Salario(), Agora);
        Assert.Equal(3000m, folha.TotalLiquido);

        contrato.RegistrarAlteracao(
            new DateOnly(2026, 8, 15), 3600m, CargoUm, Matriz, 220,
            MotivoVigencia.AlteracaoSalarial, Agora);

        folha.Calcular([contrato], Salario(), Agora);

        Assert.Equal(3320m, folha.TotalLiquido);
    }

    [Fact]
    public void Recalcular_TiraDaFolhaQuemDeixouDeSerElegivel()
    {
        // Acontece de verdade: o desligamento de junho e lancado em setembro,
        // depois da folha de agosto ja ter sido aberta.
        var folha = Nova();
        var contrato = Contrato("001");

        folha.Calcular([contrato], Salario(), Agora);
        Assert.Single(folha.Funcionarios);

        contrato.Desligar(new DateOnly(2026, 7, 20));
        folha.Calcular([contrato], Salario(), Agora);

        Assert.Empty(folha.Funcionarios);
        Assert.Equal(0m, folha.TotalLiquido);
    }

    // -----------------------------------------------------------------------
    // Lancamentos manuais
    // -----------------------------------------------------------------------

    [Fact]
    public void Desconto_DiminuiOLiquido_SemVirarValorNegativo()
    {
        var folha = Nova();
        folha.Calcular([Contrato("001")], Salario(), Agora);

        var lancamento = folha.AdicionarLancamentoManual(
            folha.Funcionarios[0].Id, ValeTransporte(), 180m, null);

        Assert.Equal(180m, lancamento.Valor);
        Assert.Equal(-180m, lancamento.EfeitoNoLiquido);
        Assert.Equal(3000m, folha.TotalProventos);
        Assert.Equal(180m, folha.TotalDescontos);
        Assert.Equal(2820m, folha.TotalLiquido);
    }

    [Fact]
    public void LancamentoManual_ComValorNegativo_Recusado()
    {
        var folha = Nova();
        folha.Calcular([Contrato("001")], Salario(), Agora);

        Assert.Throws<ArgumentException>(
            () => folha.AdicionarLancamentoManual(folha.Funcionarios[0].Id, Comissao(), -50m, null));
    }

    [Fact]
    public void LancamentoManual_NaRubricaDeSalario_Recusado()
    {
        var folha = Nova();
        folha.Calcular([Contrato("001")], Salario(), Agora);

        var erro = Assert.Throws<InvalidOperationException>(
            () => folha.AdicionarLancamentoManual(folha.Funcionarios[0].Id, Salario(), 9999m, null));

        Assert.Contains("calculada pelo sistema", erro.Message);
    }

    [Fact]
    public void LancamentoManual_EmRubricaInativa_Recusado()
    {
        var folha = Nova();
        folha.Calcular([Contrato("001")], Salario(), Agora);

        var rubrica = Comissao();
        rubrica.Inativar();

        Assert.Throws<InvalidOperationException>(
            () => folha.AdicionarLancamentoManual(folha.Funcionarios[0].Id, rubrica, 100m, null));
    }

    [Fact]
    public void Remover_ApagaOManual_MasNaoOCalculado()
    {
        var folha = Nova();
        folha.Calcular([Contrato("001")], Salario(), Agora);

        var holerite = folha.Funcionarios[0];
        var manual = folha.AdicionarLancamentoManual(holerite.Id, Comissao(), 450m, null);
        var calculado = holerite.Lancamentos.Single(l => l.Origem == OrigemLancamento.Calculado);

        Assert.Throws<InvalidOperationException>(() => folha.RemoverLancamento(holerite.Id, calculado.Id));

        Assert.True(folha.RemoverLancamento(holerite.Id, manual.Id));
        Assert.Equal(3000m, folha.TotalLiquido);

        // Remover duas vezes nao explode: informa que nao havia nada.
        Assert.False(folha.RemoverLancamento(holerite.Id, manual.Id));
    }

    [Fact]
    public void Salario_EncabecaOHolerite_MesmoLancadoDepois()
    {
        var folha = Nova();
        var contratos = new[] { Contrato("001") };

        folha.Calcular(contratos, Salario(), Agora);
        folha.AdicionarLancamentoManual(folha.Funcionarios[0].Id, Comissao(), 450m, null);
        folha.Calcular(contratos, Salario(), Agora);

        Assert.Equal("SAL", folha.Funcionarios[0].Lancamentos[0].CodigoRubrica);
        Assert.Equal("COM", folha.Funcionarios[0].Lancamentos[1].CodigoRubrica);
    }

    // -----------------------------------------------------------------------
    // Fechamento
    // -----------------------------------------------------------------------

    [Fact]
    public void Fechar_ExigeQueAFolhaTenhaSidoCalculada()
    {
        var folha = Nova();

        var erro = Assert.Throws<InvalidOperationException>(() => folha.Fechar(Agora));

        Assert.Contains("precisa ser calculada", erro.Message);
    }

    [Fact]
    public void Fechar_RecusaFolhaVazia()
    {
        var folha = Nova();
        folha.Calcular([], Salario(), Agora);

        Assert.Throws<InvalidOperationException>(() => folha.Fechar(Agora));
    }

    [Fact]
    public void FolhaFechada_NaoAceitaMaisNada()
    {
        var folha = Nova();
        var contratos = new[] { Contrato("001") };

        folha.Calcular(contratos, Salario(), Agora);
        folha.Fechar(Agora);

        Assert.True(folha.EstaFechada);
        Assert.Equal(Agora, folha.FechadaEm);

        // Os tres caminhos que poderiam reescrever um fato historico.
        Assert.Throws<InvalidOperationException>(() => folha.Calcular(contratos, Salario(), Agora));
        Assert.Throws<InvalidOperationException>(
            () => folha.AdicionarLancamentoManual(folha.Funcionarios[0].Id, Comissao(), 10m, null));
        Assert.Throws<InvalidOperationException>(() => folha.Fechar(Agora));
    }

    [Fact]
    public void Fechar_CongelaOValor_MesmoQueOContratoMudeDepois()
    {
        // O criterio de aceite mais importante da fase: alteracao cadastral
        // posterior nao reescreve uma folha ja fechada.
        var folha = Nova();
        var contrato = Contrato("001");

        folha.Calcular([contrato], Salario(), Agora);
        folha.Fechar(Agora);

        contrato.RegistrarAlteracao(
            new DateOnly(2026, 8, 1), 9000m, CargoUm, Matriz, 220,
            MotivoVigencia.AlteracaoSalarial, Agora);

        Assert.Equal(3000m, folha.TotalLiquido);
        Assert.Equal(3000m, folha.Funcionarios[0].SalarioReferencia);
    }
}
