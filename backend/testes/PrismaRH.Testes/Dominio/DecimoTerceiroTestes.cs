using PrismaRH.Dominio.DecimoTerceiro;
using PrismaRH.Dominio.Folha;

namespace PrismaRH.Testes.Dominio;

/// <summary>
/// O calculo do 13o salario nas duas parcelas.
///
/// FONTES conferidas em 29/08/2026, texto extraido dos PDFs oficiais:
///
/// - **MOS eSocial S-1.3**, consolidado ate a NO S-1.3 - 10.2026, itens 10.3.4
///   e 10.3.4.1;
/// - **Nota Orientativa 2018.13** do eSocial;
/// - **Lei 4.090/1962** e **Lei 4.749/1965**.
/// </summary>
public class DecimoTerceiroTestes
{
    private const decimal Salario = 3000.00m;

    // ------------------------------------------------------- adiantamento

    [Fact]
    public void Adiantamento_EMetadeDo13Devido()
    {
        var r = CalculadoraDecimoTerceiro.ApurarAdiantamento(2026, 12, Salario);

        // Ano cheio: 13o = 3.000, adiantamento = 1.500.
        Assert.Equal(1500.00m, r.Valor);
        Assert.Equal("12/12", r.Parcelas.Single().Referencia);
    }

    [Fact]
    public void Adiantamento_NuncaUltrapassaO13Devido()
    {
        // Admitido em outubro: 3 avos. Lido ao pe da letra, a Lei 4.749 art. 2o
        // ("metade do salario do mes anterior") daria 1.500 - MAIS que os
        // 750 de 13o a que a pessoa tem direito.
        //
        // O MOS S-1.3, item 10.3.4.1, admite adiantar mais que o devido, mas
        // isso deixaria o liquido de dezembro NEGATIVO. O padrao do produto e
        // o conservador: metade do 13o proporcional.
        var r = CalculadoraDecimoTerceiro.ApurarAdiantamento(2026, 3, Salario);

        Assert.Equal(375.00m, r.Valor);   // (3.000 x 3/12) / 2
        Assert.True(r.Valor < Salario * 3 / 12);
    }

    [Fact]
    public void Adiantamento_SoTemUmaParcela_ESoElaCarregaFgts()
    {
        var r = CalculadoraDecimoTerceiro.ApurarAdiantamento(2026, 12, Salario);

        // Uma parcela so. INSS e IRRF NAO aparecem aqui - MOS S-1.3, 10.3.4:
        // "A apuracao da CP e do IRRF (...) e feita apenas na folha de 13o
        // (anual)". Quem declara a incidencia e a rubrica, e a estrategia
        // DecimoTerceiroAdiantamento e a unica desta folha.
        Assert.Equal(
            EstrategiaRubrica.DecimoTerceiroAdiantamento,
            Assert.Single(r.Parcelas).Estrategia);
    }

    [Fact]
    public void Adiantamento_SemAvos_NaoGeraParcela()
    {
        var r = CalculadoraDecimoTerceiro.ApurarAdiantamento(2026, 0, Salario);

        Assert.Equal(0m, r.Valor);
        Assert.Empty(r.Parcelas);
    }

    // --------------------------------------------------------- folha anual

    [Fact]
    public void Anual_TresParcelas_ComTresBasesDIFERENTES()
    {
        // Este e o teste central da Fase 4F. Adiantamento de 1.500 pago antes;
        // 13o total de 3.000.
        var r = CalculadoraDecimoTerceiro.ApurarAnual(2026, 12, Salario, 1500.00m);

        Assert.Equal(3000.00m, r.Total);
        Assert.Equal(1500.00m, r.AdiantamentoJaPago);

        // A DIFERENCA e a base de FGTS - MOS S-1.3, 10.3.4: "o FGTS incidente
        // sobre a folha do 13o salario e calculado apenas sobre a diferenca
        // entre o valor da gratificacao natalina e a primeira parcela".
        Assert.Equal(1500.00m, r.BaseFgtsRestante);

        var total = r.Parcelas.Single(p => p.Estrategia == EstrategiaRubrica.DecimoTerceiroTotal);
        var desconto = r.Parcelas.Single(
            p => p.Estrategia == EstrategiaRubrica.DecimoTerceiroAdiantamentoDescontado);
        var baseFgts = r.Parcelas.Single(
            p => p.Estrategia == EstrategiaRubrica.DecimoTerceiroBaseFgts);

        Assert.Equal(3000.00m, total.Valor);      // compoe INSS e IRRF
        Assert.Equal(1500.00m, desconto.Valor);   // nao compoe nada
        Assert.Equal(1500.00m, baseFgts.Valor);   // compoe SO o FGTS
    }

    [Fact]
    public void Anual_SemAdiantamento_ABaseDeFgtsEOTotal()
    {
        var r = CalculadoraDecimoTerceiro.ApurarAnual(2026, 12, Salario, 0m);

        Assert.Equal(3000.00m, r.Total);
        Assert.Equal(3000.00m, r.BaseFgtsRestante);

        // Sem adiantamento nao ha o que compensar: a parcela de desconto nao
        // existe, em vez de existir valendo zero.
        Assert.DoesNotContain(
            r.Parcelas,
            p => p.Estrategia == EstrategiaRubrica.DecimoTerceiroAdiantamentoDescontado);
    }

    [Fact]
    public void Anual_AdiantamentoMAIORQueODevido_NaoGeraBaseNegativa()
    {
        // Hipotese que o MOS 10.3.4.1 admite: adiantou-se mais que o devido.
        // O FGTS daquele excesso ja foi recolhido na competencia do
        // adiantamento. Base negativa DEVOLVERIA FGTS, e o Fundo nao faz isso.
        var r = CalculadoraDecimoTerceiro.ApurarAnual(2026, 12, Salario, 5000.00m);

        Assert.Equal(0m, r.BaseFgtsRestante);
        Assert.DoesNotContain(
            r.Parcelas, p => p.Estrategia == EstrategiaRubrica.DecimoTerceiroBaseFgts);
    }

    [Fact]
    public void Anual_OTotalNAOCarregaFgts_ESoAInformativaCarrega()
    {
        // A prova de que as tres bases nao colidem: se o total tambem
        // declarasse FGTS, a base sairia dobrada. Aqui o teste trava que sao
        // DUAS estrategias distintas para dois numeros distintos.
        var r = CalculadoraDecimoTerceiro.ApurarAnual(2026, 12, Salario, 1200.00m);

        var total = r.Parcelas.Single(p => p.Estrategia == EstrategiaRubrica.DecimoTerceiroTotal);
        var baseFgts = r.Parcelas.Single(
            p => p.Estrategia == EstrategiaRubrica.DecimoTerceiroBaseFgts);

        Assert.NotEqual(total.Valor, baseFgts.Valor);
        Assert.Equal(1800.00m, baseFgts.Valor);   // 3.000 - 1.200
    }

    // --------------------------------------------------------- arredondamento

    [Fact]
    public void Arredondamento_AcontecUmaVezPorParcela()
    {
        // Salario que nao divide certo: 1.000 / 12 = 83,333...
        var r = CalculadoraDecimoTerceiro.ApurarAnual(2026, 7, 1000.00m, 0m);

        // 1.000 x 7/12 = 583,3333 -> 583,33
        Assert.Equal(583.33m, r.Total);
        Assert.Equal(583.33m, r.BaseFgtsRestante);
    }

    [Fact]
    public void Adiantamento_MetadeSobreOValorJAARREDONDADO()
    {
        // 1.000 x 7/12 = 583,33 (arredondado); metade = 291,665 -> 291,67.
        // A metade sai sobre o numero que aparece no holerite, e nao sobre o
        // valor exato - senao a memoria de calculo nao se refaz a mao.
        var r = CalculadoraDecimoTerceiro.ApurarAdiantamento(2026, 7, 1000.00m);

        Assert.Equal(291.67m, r.Valor);
    }

    // -------------------------------------------------------------- limites

    [Theory]
    [InlineData(-1)]
    [InlineData(13)]
    public void Avos_ForaDeZeroADoze_SaoRecusados(int avos)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CalculadoraDecimoTerceiro.ApurarAnual(2026, avos, Salario, 0m));
    }

    [Fact]
    public void SalarioNegativo_ERecusado()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CalculadoraDecimoTerceiro.ApurarAdiantamento(2026, 12, -1m));
    }

    [Fact]
    public void AdiantamentoNegativo_ERecusado()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CalculadoraDecimoTerceiro.ApurarAnual(2026, 12, Salario, -1m));
    }

    [Fact]
    public void SalarioZero_NaoGeraParcelaAlguma()
    {
        var r = CalculadoraDecimoTerceiro.ApurarAnual(2026, 12, 0m, 0m);

        Assert.Equal(0m, r.Total);
        Assert.Empty(r.Parcelas);
    }

    // ---------------------------------------------- invariantes das rubricas

    [Theory]
    [InlineData(EstrategiaRubrica.DecimoTerceiroAdiantamento)]
    [InlineData(EstrategiaRubrica.DecimoTerceiroTotal)]
    public void RubricaDe13_PrecisaSerProvento(EstrategiaRubrica estrategia)
    {
        Assert.Throws<ArgumentException>(() => new Rubrica(
            Guid.CreateVersion7(), "X13", "Teste", TipoRubrica.Desconto,
            estrategia, BaseCalculo.Nenhuma, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void CompensacaoDoAdiantamento_PrecisaSerDesconto()
    {
        Assert.Throws<ArgumentException>(() => new Rubrica(
            Guid.CreateVersion7(), "X13D", "Teste", TipoRubrica.Provento,
            EstrategiaRubrica.DecimoTerceiroAdiantamentoDescontado,
            BaseCalculo.Nenhuma, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void BaseDeFgtsDo13_PrecisaSerInformativa()
    {
        // Como provento pagaria o 13o duas vezes; como desconto, a invariante
        // da Fase 4A a proibiria de compor base - que e tudo o que ela faz.
        Assert.Throws<ArgumentException>(() => new Rubrica(
            Guid.CreateVersion7(), "X13F", "Teste", TipoRubrica.Provento,
            EstrategiaRubrica.DecimoTerceiroBaseFgts,
            BaseCalculo.Fgts, DateTimeOffset.UtcNow));

        Assert.Throws<ArgumentException>(() => new Rubrica(
            Guid.CreateVersion7(), "X13F", "Teste", TipoRubrica.Desconto,
            EstrategiaRubrica.DecimoTerceiroBaseFgts,
            BaseCalculo.Fgts, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void BaseDeFgtsDo13_ACEITA_SerInformativaComIncidencia()
    {
        // O caso que PRECISA passar: informativa compondo base. E a unica forma
        // de ter tres bases diferentes num holerite so.
        var rubrica = new Rubrica(
            Guid.CreateVersion7(), "X13F", "Base FGTS do 13o", TipoRubrica.Informativo,
            EstrategiaRubrica.DecimoTerceiroBaseFgts,
            BaseCalculo.Fgts, DateTimeOffset.UtcNow);

        Assert.Equal(BaseCalculo.Fgts, rubrica.BasesIncidentes);
    }
}
