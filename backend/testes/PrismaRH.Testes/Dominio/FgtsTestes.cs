using PrismaRH.Dominio.Folha;
using PrismaRH.Dominio.Parametros;

namespace PrismaRH.Testes.Dominio;

/// <summary>
/// Fase 4C: o deposito de FGTS do empregador.
///
/// Fonte: Lei n. 8.036, de 11/05/1990, art. 15 - deposito mensal de 8% da
/// remuneracao paga ou devida no mes anterior.
///
/// A diferenca que mais importa em relacao ao INSS: FGTS NAO sai do salario do
/// funcionario. E obrigacao da empresa. A rubrica e informativa e o valor nao
/// entra no liquido.
/// </summary>
public class FgtsTestes
{
    /// <summary>Sem dependentes: o cenario padrao da maioria dos testes.</summary>
    private static readonly Dictionary<Guid, int> SemDependentes = [];

    private static readonly DateTimeOffset Agora = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Org = Guid.CreateVersion7();
    private static readonly Guid Empresa = Guid.CreateVersion7();
    private static readonly Guid Cargo = Guid.CreateVersion7();
    private static readonly Guid Matriz = Guid.CreateVersion7();
    private static readonly Competencia Agosto = new(2026, 8);

    private const BaseCalculo IntegraTudo = BaseCalculo.Inss | BaseCalculo.Fgts | BaseCalculo.Irrf;

    private static TabelaFgts Tabela(decimal aliquota = 0.08m, DateOnly? inicio = null) =>
        new(inicio ?? new DateOnly(1990, 5, 11), aliquota,
            "Lei n. 8.036/1990, art. 15", Agora);

    private static Rubrica RubricaFgts() =>
        new(Org, "FGTS", "FGTS sobre a folha",
            TipoRubrica.Informativo, EstrategiaRubrica.FgtsMensal, BaseCalculo.Nenhuma, Agora);

    private static Rubrica Salario() =>
        new(Org, "SAL", "Salario base",
            TipoRubrica.Provento, EstrategiaRubrica.SalarioBaseProporcional, IntegraTudo, Agora);

    private static PrismaRH.Dominio.Contratos.ContratoTrabalho Contrato(decimal salario) =>
        new(Org, Guid.CreateVersion7(), Empresa, "1001",
            new DateOnly(2025, 3, 1), salario, Cargo, Matriz, 220, Agora);

    // -------------------------------------------------------------- calculo

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1621.00, 129.68)]     // 1.621,00 x 8% = 129,68
    [InlineData(3000.00, 240.00)]
    [InlineData(5000.00, 400.00)]
    [InlineData(8475.55, 678.04)]     // 678,044 -> 678,04
    [InlineData(30000.00, 2400.00)]   // sem teto: incide sobre tudo
    public void Deposito(decimal baseFgts, decimal esperado) =>
        Assert.Equal(esperado, CalculadoraFgts.Apurar(baseFgts, Tabela()).Valor);

    [Fact]
    public void NaoTemTeto_AoContrarioDoInss()
    {
        // O teto do INSS nao limita o FGTS. Quem ganha 30 mil recolhe INSS
        // sobre 8.475,55 e FGTS sobre os 30 mil.
        var noTetoDoInss = CalculadoraFgts.Apurar(8475.55m, Tabela()).Valor;
        var acima = CalculadoraFgts.Apurar(30000.00m, Tabela()).Valor;

        Assert.True(acima > noTetoDoInss);
        Assert.Equal(2400.00m, acima);
    }

    [Fact]
    public void Arredonda_NoValorFinal()
    {
        // 1.234,56 x 8% = 98,7648 -> 98,76
        var apuracao = CalculadoraFgts.Apurar(1234.56m, Tabela());

        Assert.Equal(98.76m, apuracao.Valor);
        Assert.Contains("arredondado", apuracao.Passos[^1].Expressao);
    }

    [Fact]
    public void BaseNegativa_ERecusada() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CalculadoraFgts.Apurar(-1m, Tabela()));

    [Fact]
    public void Memoria_TemBaseEDeposito()
    {
        var apuracao = CalculadoraFgts.Apurar(3000.00m, Tabela());

        Assert.Equal(2, apuracao.Passos.Count);
        Assert.Equal("Base de calculo do FGTS", apuracao.Passos[0].Descricao);
        Assert.Equal(3000.00m, apuracao.Passos[0].Valor);
        Assert.Contains("8%", apuracao.Passos[1].Descricao);
        Assert.Equal("3.000,00 x 8%", apuracao.Passos[1].Expressao);
        Assert.Equal(240.00m, apuracao.Passos[1].Valor);
    }

    // -------------------------------------------------------------- tabela

    [Fact]
    public void Tabela_SemFonte_ERecusada()
    {
        var erro = Assert.Throws<ArgumentException>(() =>
            new TabelaFgts(new DateOnly(1990, 5, 11), 0.08m, "  ", Agora));

        Assert.Contains("fonte", erro.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tabela_ComAliquotaEmPercentual_ERecusada() =>
        // 8 em vez de 0.08: depositaria oito vezes o salario.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TabelaFgts(new DateOnly(1990, 5, 11), 8m, "x", Agora));

    [Fact]
    public void VigenteEm_EscolheAMaisRecenteQueJaComecou()
    {
        var antiga = Tabela(0.08m, new DateOnly(1990, 5, 11));
        var nova = Tabela(0.09m, new DateOnly(2030, 1, 1));

        Assert.Same(antiga, TabelaFgts.VigenteEm([antiga, nova], new DateOnly(2026, 8, 1)));
        Assert.Same(nova, TabelaFgts.VigenteEm([antiga, nova], new DateOnly(2030, 6, 1)));
    }

    [Fact]
    public void VigenteEm_AntesDeQualquerVigencia_DevolveNull() =>
        Assert.Null(TabelaFgts.VigenteEm([Tabela()], new DateOnly(1989, 12, 31)));

    // ------------------------------------------------------------ rubrica

    [Fact]
    public void RubricaDeFgts_ComoDesconto_ERecusada()
    {
        // Seria o erro caro: 8% saindo do salario do funcionario, com o
        // holerite continuando a fechar.
        var erro = Assert.Throws<ArgumentException>(() => new Rubrica(
            Org, "FGTS", "FGTS", TipoRubrica.Desconto,
            EstrategiaRubrica.FgtsMensal, BaseCalculo.Nenhuma, Agora));

        Assert.Contains("informativa", erro.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RubricaDeFgts_ComoProvento_ERecusada() =>
        Assert.Throws<ArgumentException>(() => new Rubrica(
            Org, "FGTS", "FGTS", TipoRubrica.Provento,
            EstrategiaRubrica.FgtsMensal, BaseCalculo.Nenhuma, Agora));

    [Fact]
    public void RubricaDeFgts_QueCompoeBase_ERecusada()
    {
        // Informativo PODE compor base - por isso a recusa precisa ser
        // explicita. Se o FGTS compusesse a base de FGTS, cada calculo
        // aumentaria a base do calculo seguinte.
        var erro = Assert.Throws<ArgumentException>(() => new Rubrica(
            Org, "FGTS", "FGTS", TipoRubrica.Informativo,
            EstrategiaRubrica.FgtsMensal, BaseCalculo.Fgts, Agora));

        Assert.Contains("nao compoe base", erro.Message, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------- folha

    private static (FolhaPagamento Folha, PrismaRH.Dominio.Contratos.ContratoTrabalho Contrato)
        FolhaComFgts(decimal salario = 3000m)
    {
        var folha = new FolhaPagamento(Org, Empresa, Agosto, Agora);
        var contrato = Contrato(salario);
        var salarioRubrica = Salario();
        var fgts = new ParametrosFgts(RubricaFgts(), Tabela());

        folha.Calcular(
            [contrato], salarioRubrica, [salarioRubrica],
            new ParametrosEncargos(null, fgts), SemDependentes, Agora);

        return (folha, contrato);
    }

    [Fact]
    public void FgtsEntraNoHolerite_SemMexerNoLiquido()
    {
        var holerite = FolhaComFgts().Folha.Funcionarios[0];

        var fgts = holerite.Lancamentos.Single(l => l.CodigoRubrica == "FGTS");

        Assert.Equal(240.00m, fgts.Valor);
        Assert.Equal(TipoRubrica.Informativo, fgts.Tipo);

        // A prova de que nao sai do salario: o liquido e o salario cheio.
        Assert.Equal(3000m, holerite.Liquido);
        Assert.Equal(0m, holerite.TotalDescontos);
        Assert.Equal(0m, fgts.EfeitoNoLiquido);
    }

    [Fact]
    public void FgtsNaoCompoeBaseAlguma()
    {
        var holerite = FolhaComFgts().Folha.Funcionarios[0];

        // A base de FGTS continua sendo so o salario. Se o proprio FGTS
        // entrasse nela, ela viraria 3.240 e o proximo calculo, 3.499,20.
        Assert.Equal(3000m, holerite.BaseDe(BaseCalculo.Fgts));
    }

    [Fact]
    public void SemParametros_NaoAparece()
    {
        var folha = new FolhaPagamento(Org, Empresa, Agosto, Agora);
        var salario = Salario();

        folha.Calcular(
            [Contrato(3000m)], salario, [salario],
            ParametrosEncargos.Nenhum, SemDependentes, Agora);

        Assert.DoesNotContain(folha.Funcionarios[0].Lancamentos, l => l.CodigoRubrica == "FGTS");
    }

    [Fact]
    public void Recalcular_NaoDuplicaOFgts()
    {
        var (folha, contrato) = FolhaComFgts();
        var salario = Salario();
        var fgts = new ParametrosFgts(RubricaFgts(), Tabela());

        folha.Calcular(
            [contrato], salario, [salario],
            new ParametrosEncargos(null, fgts), SemDependentes, Agora);
        folha.Calcular(
            [contrato], salario, [salario],
            new ParametrosEncargos(null, fgts), SemDependentes, Agora);

        Assert.Single(folha.Funcionarios[0].Lancamentos, l => l.CodigoRubrica == "FGTS");
    }

    [Fact]
    public void LancamentoManual_ReapuraOFgts()
    {
        var (folha, _) = FolhaComFgts();
        var holerite = folha.Funcionarios[0];
        var fgts = new ParametrosFgts(RubricaFgts(), Tabela());

        var comissao = new Rubrica(
            Org, "COM", "Comissao", TipoRubrica.Provento,
            EstrategiaRubrica.ValorInformado, IntegraTudo, Agora);

        folha.AdicionarLancamentoManual(
            holerite.Id, comissao, 1000m, null,
            new ParametrosEncargos(null, fgts));

        // Base virou 4.000 e o deposito acompanhou, sem recalcular a folha.
        Assert.Equal(4000m, holerite.BaseDe(BaseCalculo.Fgts));
        Assert.Equal(320.00m, holerite.Lancamentos.Single(l => l.CodigoRubrica == "FGTS").Valor);
    }

    [Fact]
    public void InssEFgts_ConvivemNoMesmoHolerite()
    {
        var folha = new FolhaPagamento(Org, Empresa, Agosto, Agora);
        var contrato = Contrato(5000m);
        var salario = Salario();

        var rubricaInss = new Rubrica(
            Org, "INSS", "INSS", TipoRubrica.Desconto,
            EstrategiaRubrica.InssProgressivo, BaseCalculo.Nenhuma, Agora);

        var tabelaInss = new TabelaInss(
            new DateOnly(2026, 1, 1), "Portaria Interministerial MPS/MF n. 13, de 09/01/2026",
            [(1621.00m, 0.075m), (2902.84m, 0.09m), (4354.27m, 0.12m), (8475.55m, 0.14m)], Agora);

        folha.Calcular(
            [contrato], salario, [salario],
            new ParametrosEncargos(new ParametrosInss(rubricaInss, tabelaInss), new ParametrosFgts(RubricaFgts(), Tabela())), SemDependentes, Agora);

        var holerite = folha.Funcionarios[0];

        // INSS desconta (501,51, conferido na 4B); FGTS nao.
        Assert.Equal(501.51m, holerite.Lancamentos.Single(l => l.CodigoRubrica == "INSS").Valor);
        Assert.Equal(400.00m, holerite.Lancamentos.Single(l => l.CodigoRubrica == "FGTS").Valor);
        Assert.Equal(501.51m, holerite.TotalDescontos);
        Assert.Equal(4498.49m, holerite.Liquido);
    }
}
