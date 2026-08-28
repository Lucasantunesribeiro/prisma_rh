using PrismaRH.Dominio.Ferias;
using PrismaRH.Dominio.Folha;

namespace PrismaRH.Testes.Dominio;

/// <summary>
/// Fase 4E, etapa 2b: o pagamento das ferias.
///
/// FONTES (CLAUDE.md secao 29):
/// - CLT art. 142: remuneracao devida na DATA DA CONCESSAO;
/// - CF art. 7o, XVII: um terco a mais que o salario normal;
/// - CLT art. 143: abono pecuniario.
///
/// As INCIDENCIAS nao sao testadas aqui: elas sao atributo da rubrica, e a
/// prova de que chegam ao holerite esta nos testes de integracao. A fonte
/// delas e o Manual do eSocial - ver RubricaDeFeriasTestes.
/// </summary>
public class CalculoFeriasTestes
{
    private static ParcelaFerias Parcela(ApuracaoFerias a, EstrategiaRubrica e) =>
        a.Parcelas.Single(p => p.Estrategia == e);

    private static decimal Valor(ApuracaoFerias a, EstrategiaRubrica e) => Parcela(a, e).Valor;

    // ------------------------------------------------------ ferias gozadas

    [Fact]
    public void TrintaDias_PagamOSalarioInteiro()
    {
        var a = CalculadoraFerias.Apurar(3000m, diasGozo: 30, diasAbono: 0);

        // 3.000 / 30 x 30 = 3.000. Trinta dias de ferias valem um salario -
        // e um resultado obvio que so aparece se o divisor estiver certo.
        Assert.Equal(3000.00m, Valor(a, EstrategiaRubrica.FeriasGozadas));
        Assert.Equal(1000.00m, Valor(a, EstrategiaRubrica.TercoFerias));
        Assert.Equal(4000.00m, a.Total);
    }

    [Fact]
    public void VinteDias_SaoDoisTercosDoSalario()
    {
        var a = CalculadoraFerias.Apurar(3000m, diasGozo: 20, diasAbono: 0);

        Assert.Equal(2000.00m, Valor(a, EstrategiaRubrica.FeriasGozadas));
        Assert.Equal(666.67m, Valor(a, EstrategiaRubrica.TercoFerias));
    }

    [Fact]
    public void ODivisorESempre30_NaoOsDiasDoMes()
    {
        // O mesmo funcionario, os mesmos 15 dias: o valor nao pode depender de
        // as ferias caírem em fevereiro ou em marco.
        var a = CalculadoraFerias.Apurar(3000m, diasGozo: 15, diasAbono: 0);

        Assert.Equal(1500.00m, Valor(a, EstrategiaRubrica.FeriasGozadas));
        Assert.Equal(30, CalculadoraFerias.Divisor);
    }

    // ---------------------------------------------------------- abono

    [Fact]
    public void AbonoEDezDias_ComSeuProprioTerco()
    {
        var a = CalculadoraFerias.Apurar(3000m, diasGozo: 20, diasAbono: 10);

        Assert.Equal(2000.00m, Valor(a, EstrategiaRubrica.FeriasGozadas));
        Assert.Equal(666.67m, Valor(a, EstrategiaRubrica.TercoFerias));
        Assert.Equal(1000.00m, Valor(a, EstrategiaRubrica.AbonoPecuniario));
        Assert.Equal(333.33m, Valor(a, EstrategiaRubrica.TercoAbono));

        // 20 dias gozados + 10 vendidos: a pessoa recebe mais do que um
        // salario, porque os dois tercos se somam.
        Assert.Equal(4000.00m, a.Total);
    }

    [Fact]
    public void SemAbono_NaoGeraAsDuasRubricasDele()
    {
        var a = CalculadoraFerias.Apurar(3000m, diasGozo: 30, diasAbono: 0);

        Assert.DoesNotContain(a.Parcelas, p => p.Estrategia == EstrategiaRubrica.AbonoPecuniario);
        Assert.DoesNotContain(a.Parcelas, p => p.Estrategia == EstrategiaRubrica.TercoAbono);
    }

    [Fact]
    public void SoAbono_NaoGeraAsRubricasDeGozo()
    {
        var a = CalculadoraFerias.Apurar(3000m, diasGozo: 0, diasAbono: 10);

        Assert.Equal(2, a.Parcelas.Count);
        Assert.Equal(1000.00m, Valor(a, EstrategiaRubrica.AbonoPecuniario));
        Assert.Equal(333.33m, Valor(a, EstrategiaRubrica.TercoAbono));
    }

    // -------------------------------------------------------- arredondamento

    [Fact]
    public void OTercoIncideSobreOValorARREDONDADO()
    {
        // Salario 2.500, 7 dias: 2.500/30 x 7 = 583,3333... -> 583,33
        // O terco sai de 583,33, e nao de 583,3333: e o numero que aparece no
        // holerite, e a pessoa precisa conseguir refazer a conta a mao.
        var a = CalculadoraFerias.Apurar(2500m, diasGozo: 7, diasAbono: 0);

        Assert.Equal(583.33m, Valor(a, EstrategiaRubrica.FeriasGozadas));
        Assert.Equal(194.44m, Valor(a, EstrategiaRubrica.TercoFerias));

        // 583,33 / 3 = 194,4433... -> 194,44. Conferido a mao.
        Assert.Equal(decimal.Round(583.33m / 3m, 2, MidpointRounding.AwayFromZero),
            Valor(a, EstrategiaRubrica.TercoFerias));
    }

    [Fact]
    public void SalarioComCentavos_NaoPerdePrecisao()
    {
        var a = CalculadoraFerias.Apurar(3333.33m, diasGozo: 30, diasAbono: 0);

        Assert.Equal(3333.33m, Valor(a, EstrategiaRubrica.FeriasGozadas));
        Assert.Equal(1111.11m, Valor(a, EstrategiaRubrica.TercoFerias));
    }

    // -------------------------------------------------------------- memoria

    [Fact]
    public void Memoria_MostraODivisorEOsDias()
    {
        var a = CalculadoraFerias.Apurar(3000m, diasGozo: 20, diasAbono: 0);

        var ferias = Parcela(a, EstrategiaRubrica.FeriasGozadas);
        Assert.Equal("20/30", ferias.Referencia);
        Assert.Equal("3.000,00 / 30 x 20", ferias.Passos[1].Expressao);

        var terco = Parcela(a, EstrategiaRubrica.TercoFerias);
        Assert.Equal("1/3", terco.Referencia);
        Assert.Equal("2.000,00 / 3", terco.Passos[1].Expressao);
    }

    // ------------------------------------------------------------- recusas

    [Fact]
    public void SalarioNegativo_ERecusado() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CalculadoraFerias.Apurar(-1m, 30, 0));

    [Fact]
    public void DiasNegativos_SaoRecusados()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CalculadoraFerias.Apurar(3000m, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CalculadoraFerias.Apurar(3000m, 30, -1));
    }

    [Fact]
    public void SemDiaAlgum_NaoGeraParcela() =>
        Assert.Empty(CalculadoraFerias.Apurar(3000m, 0, 0).Parcelas);
}

/// <summary>
/// As invariantes das rubricas de ferias e as INCIDENCIAS de cada uma.
///
/// FONTE das incidencias: Manual do eSocial, tabela de rubricas e bases de
/// calculo, informada pelo responsavel pelo projeto em 28/08/2026.
///
/// Este teste existe para travar os quatro conjuntos: eles sao DIFERENTES
/// entre si, e um copiar-colar entre as rubricas passaria despercebido no
/// codigo mas mudaria o imposto de todo mundo.
/// </summary>
public class RubricaDeFeriasTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Org = Guid.CreateVersion7();

    private static Rubrica Criar(
        EstrategiaRubrica estrategia,
        TipoRubrica tipo = TipoRubrica.Provento,
        BaseCalculo bases = BaseCalculo.Nenhuma) =>
        new(Org, "X", "Rubrica", tipo, estrategia, bases, Agora);

    [Theory]
    [InlineData(EstrategiaRubrica.FeriasGozadas)]
    [InlineData(EstrategiaRubrica.TercoFerias)]
    [InlineData(EstrategiaRubrica.AbonoPecuniario)]
    [InlineData(EstrategiaRubrica.TercoAbono)]
    public void RubricaDeFerias_ComoDesconto_ERecusada(EstrategiaRubrica estrategia)
    {
        // Como desconto elas inverteriam o sinal do holerite: a pessoa sairia
        // de ferias DEVENDO.
        var erro = Assert.Throws<ArgumentException>(() => Criar(estrategia, TipoRubrica.Desconto));

        Assert.Contains("provento", erro.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(EstrategiaRubrica.FeriasGozadas)]
    [InlineData(EstrategiaRubrica.TercoFerias)]
    [InlineData(EstrategiaRubrica.AbonoPecuniario)]
    [InlineData(EstrategiaRubrica.TercoAbono)]
    public void RubricaDeFerias_ComoInformativo_ERecusada(EstrategiaRubrica estrategia) =>
        // Como informativo a pessoa sairia de ferias sem receber nada.
        Assert.Throws<ArgumentException>(() => Criar(estrategia, TipoRubrica.Informativo));

    [Fact]
    public void AsQuatroEstrategias_EstaoNoConjunto()
    {
        Assert.Equal(4, Rubrica.EstrategiasDeFerias.Length);
        Assert.Contains(EstrategiaRubrica.FeriasGozadas, Rubrica.EstrategiasDeFerias);
        Assert.Contains(EstrategiaRubrica.TercoFerias, Rubrica.EstrategiasDeFerias);
        Assert.Contains(EstrategiaRubrica.AbonoPecuniario, Rubrica.EstrategiasDeFerias);
        Assert.Contains(EstrategiaRubrica.TercoAbono, Rubrica.EstrategiasDeFerias);
    }

    [Fact]
    public void FeriasGozadas_IntegramAsTresBases() =>
        // Lei 8.212/91 art. 28 par. 9o "d" exclui apenas as ferias
        // INDENIZADAS - as gozadas integram o salario-de-contribuicao. IRRF e
        // FGTS acompanham.
        Assert.Equal(
            BaseCalculo.Inss | BaseCalculo.Fgts | BaseCalculo.Irrf,
            Criar(EstrategiaRubrica.FeriasGozadas,
                bases: BaseCalculo.Inss | BaseCalculo.Fgts | BaseCalculo.Irrf).BasesIncidentes);

    [Fact]
    public void TercoSobreFeriasGozadas_IntegraAsTresBases() =>
        // eSocial 1920: INSS sim, IRRF sim, FGTS sim.
        Assert.Equal(
            BaseCalculo.Inss | BaseCalculo.Fgts | BaseCalculo.Irrf,
            Criar(EstrategiaRubrica.TercoFerias,
                bases: BaseCalculo.Inss | BaseCalculo.Fgts | BaseCalculo.Irrf).BasesIncidentes);

    [Fact]
    public void TercoSobreAbono_SoIntegraIrrf() =>
        // eSocial 1940: INSS NAO, IRRF sim, FGTS NAO. E a diferenca exata em
        // relacao ao terco sobre ferias gozadas.
        Assert.Equal(
            BaseCalculo.Irrf,
            Criar(EstrategiaRubrica.TercoAbono, bases: BaseCalculo.Irrf).BasesIncidentes);

    [Fact]
    public void AsDuasRubricasDeTerco_PodemTerIncidenciasDiferentes()
    {
        // A razao de existirem DUAS estrategias de terco, e nao uma. Com uma
        // so, seria preciso escolher uma das duas tabelas de incidencia - e
        // errar a outra em todo holerite com abono.
        var sobreFerias = Criar(EstrategiaRubrica.TercoFerias,
            bases: BaseCalculo.Inss | BaseCalculo.Fgts | BaseCalculo.Irrf);
        var sobreAbono = Criar(EstrategiaRubrica.TercoAbono, bases: BaseCalculo.Irrf);

        Assert.NotEqual(sobreFerias.BasesIncidentes, sobreAbono.BasesIncidentes);
    }
}
