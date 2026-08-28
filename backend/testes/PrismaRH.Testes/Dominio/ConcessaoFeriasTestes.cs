using PrismaRH.Dominio.Ferias;

namespace PrismaRH.Testes.Dominio;

/// <summary>
/// Fase 4E, etapa 2a: a concessao (programacao) de ferias.
///
/// FONTES (CLAUDE.md secao 29):
/// - CLT art. 134, par. 1o (Lei 13.467/2017): ate TRES periodos, um com no
///   minimo 14 dias corridos e os demais com no minimo 5;
/// - CLT art. 143: conversao de ate UM TERCO em abono pecuniario.
///
/// Nao ha dinheiro aqui. O que se prova e que o sistema recusa uma programacao
/// que a lei nao permite - e recusa dizendo POR QUE.
/// </summary>
public class ConcessaoFeriasTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Org = Guid.CreateVersion7();
    private static readonly Guid Contrato = Guid.CreateVersion7();

    /// <summary>Periodo aquisitivo de 01/01/2025 a 31/12/2025, 30 dias.</summary>
    private static PeriodoAquisitivo Periodo() =>
        new(1, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), 30);

    private static ConcessaoFerias Conceder(int dias, DateOnly inicio, int abono = 0) =>
        new(Org, Contrato, Periodo(), inicio, dias, abono, Agora);

    private static PeriodoComSaldo ComSaldo(params ConcessaoFerias[] concessoes) =>
        new(Periodo(), concessoes);

    private static IReadOnlyList<RecusaConcessao> Conferir(
        PeriodoComSaldo periodo, int dias, int abono = 0, DateOnly? inicio = null) =>
        RegrasDeConcessao.Conferir(periodo, dias, abono, inicio ?? new DateOnly(2026, 3, 1));

    // ------------------------------------------------------------ entidade

    [Fact]
    public void ConcessaoDeTrintaDias_TemFimNoTrigesimoDia()
    {
        var c = Conceder(30, new DateOnly(2026, 3, 1));

        // Dias CORRIDOS: 01/03 mais 29 dias e 30/03. Contar 31 seria dar um
        // dia a mais de descanso e de pagamento.
        Assert.Equal(new DateOnly(2026, 3, 30), c.Fim);
        Assert.Equal(30, c.DiasBaixados);
    }

    [Fact]
    public void ConcessaoComAbono_BaixaGozoMaisAbono()
    {
        var c = Conceder(20, new DateOnly(2026, 3, 1), abono: 10);

        Assert.Equal(new DateOnly(2026, 3, 20), c.Fim);
        Assert.Equal(30, c.DiasBaixados);
    }

    [Fact]
    public void GozoAntesDoFimDoAquisitivo_ERecusado()
    {
        var erro = Assert.Throws<ArgumentException>(() =>
            Conceder(30, new DateOnly(2025, 12, 31)));

        Assert.Contains("periodo aquisitivo", erro.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GozoNoDiaSeguinteAoAquisitivo_EAceito() =>
        Assert.Equal(new DateOnly(2026, 1, 1), Conceder(30, new DateOnly(2026, 1, 1)).Inicio);

    [Fact]
    public void SemGozoESemAbono_ERecusada() =>
        Assert.Throws<ArgumentException>(() => Conceder(0, new DateOnly(2026, 3, 1)));

    [Fact]
    public void SoAbono_EPermitido()
    {
        // Vender dias sem gozar nenhum naquele lancamento e legitimo: o gozo
        // vem em outra concessao.
        var c = Conceder(0, new DateOnly(2026, 3, 1), abono: 10);

        Assert.Equal(10, c.DiasBaixados);
        Assert.Equal(c.Inicio, c.Fim);
    }

    [Fact]
    public void DiasNegativos_SaoRecusados() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Conceder(-1, new DateOnly(2026, 3, 1)));

    // ------------------------------------------------------------- situacao

    [Fact]
    public void Situacao_SegueOCalendario()
    {
        var c = Conceder(10, new DateOnly(2026, 3, 1));

        Assert.Equal(SituacaoConcessao.Programada, c.SituacaoEm(new DateOnly(2026, 2, 28)));
        Assert.Equal(SituacaoConcessao.EmGozo, c.SituacaoEm(new DateOnly(2026, 3, 1)));
        Assert.Equal(SituacaoConcessao.EmGozo, c.SituacaoEm(new DateOnly(2026, 3, 10)));
        Assert.Equal(SituacaoConcessao.Concluida, c.SituacaoEm(new DateOnly(2026, 3, 11)));
    }

    [Fact]
    public void SoCancelaAntesDeComecar()
    {
        var c = Conceder(10, new DateOnly(2026, 3, 1));

        Assert.True(c.PodeSerCancelada(new DateOnly(2026, 2, 28)));
        Assert.False(c.PodeSerCancelada(new DateOnly(2026, 3, 1)));
        Assert.False(c.PodeSerCancelada(new DateOnly(2026, 3, 11)));
    }

    // --------------------------------------------------------------- saldo

    [Fact]
    public void PeriodoSemConcessao_TemSaldoCheio()
    {
        var p = ComSaldo();

        Assert.Equal(0, p.DiasConcedidos);
        Assert.Equal(30, p.Saldo);
        Assert.Equal(10, p.SaldoAbono);
        Assert.Equal(0, p.FracoesUsadas);
        Assert.False(p.TotalmenteConcedido);
    }

    [Fact]
    public void SaldoDesconta_GozoEAbono()
    {
        var p = ComSaldo(Conceder(20, new DateOnly(2026, 3, 1), abono: 10));

        Assert.Equal(30, p.DiasConcedidos);
        Assert.Equal(0, p.Saldo);
        Assert.Equal(0, p.SaldoAbono);
        Assert.True(p.TotalmenteConcedido);
    }

    [Fact]
    public void AbonoPuro_NaoContaComoFracao()
    {
        // Vender dias nao e gozar. Se contasse, quem vendesse 10 dias teria
        // gastado uma das tres fracoes do art. 134 sem ter descansado nada.
        var p = ComSaldo(Conceder(0, new DateOnly(2026, 3, 1), abono: 10));

        Assert.Equal(0, p.FracoesUsadas);
        Assert.Equal(20, p.Saldo);
    }

    // -------------------------------------------------- regras de concessao

    [Fact]
    public void TrintaDiasDeUmaVez_EAceito() =>
        Assert.Empty(Conferir(ComSaldo(), 30));

    [Fact]
    public void MaisDiasQueOSaldo_ERecusado() =>
        Assert.Contains(RecusaConcessao.SaldoInsuficiente, Conferir(ComSaldo(), 31));

    [Fact]
    public void AbonoAcimaDeUmTerco_ERecusado()
    {
        // 30 dias: no maximo 10 podem ser vendidos (art. 143).
        Assert.Empty(Conferir(ComSaldo(), 20, abono: 10));
        Assert.Contains(RecusaConcessao.AbonoAcimaDoTerco, Conferir(ComSaldo(), 19, abono: 11));
    }

    [Fact]
    public void AbonoJaUsado_ReduzOSaldoDeAbono()
    {
        var p = ComSaldo(Conceder(5, new DateOnly(2026, 3, 1), abono: 6));

        Assert.Equal(4, p.SaldoAbono);
        Assert.Contains(RecusaConcessao.AbonoAcimaDoTerco, Conferir(p, 10, abono: 5));
        Assert.DoesNotContain(RecusaConcessao.AbonoAcimaDoTerco, Conferir(p, 10, abono: 4, inicio: new DateOnly(2026, 6, 1)));
    }

    [Fact]
    public void QuartaFracao_ERecusada()
    {
        var p = ComSaldo(
            Conceder(14, new DateOnly(2026, 2, 1)),
            Conceder(5, new DateOnly(2026, 4, 1)),
            Conceder(5, new DateOnly(2026, 6, 1)));

        Assert.Equal(3, p.FracoesUsadas);
        Assert.Contains(RecusaConcessao.FracoesDemais, Conferir(p, 6, inicio: new DateOnly(2026, 8, 1)));
    }

    [Fact]
    public void FracaoAbaixoDeCincoDias_ERecusada() =>
        Assert.Contains(RecusaConcessao.FracaoAbaixoDoMinimo,
            Conferir(ComSaldo(), 4));

    [Fact]
    public void FracaoDeExatamenteCincoDias_EAceita() =>
        Assert.DoesNotContain(RecusaConcessao.FracaoAbaixoDoMinimo, Conferir(ComSaldo(), 5));

    [Fact]
    public void FracionarSemNenhumaFracaoDeQuatorze_ERecusadoAoFechar()
    {
        // 10 + 10 + 10 = 30, nenhuma alcanca 14. A recusa vem na TERCEIRA,
        // que e quando o periodo se fecharia sem cumprir a regra.
        var p = ComSaldo(
            Conceder(10, new DateOnly(2026, 2, 1)),
            Conceder(10, new DateOnly(2026, 4, 1)));

        Assert.Contains(RecusaConcessao.SemFracaoDeQuatorzeDias,
            Conferir(p, 10, inicio: new DateOnly(2026, 6, 1)));
    }

    [Fact]
    public void FracionarComUmaFracaoDeQuatorze_EAceito()
    {
        var p = ComSaldo(
            Conceder(14, new DateOnly(2026, 2, 1)),
            Conceder(8, new DateOnly(2026, 4, 1)));

        Assert.Empty(Conferir(p, 8, inicio: new DateOnly(2026, 6, 1)));
    }

    [Fact]
    public void PrimeiraFracaoPequena_NaoERecusadaAntesDoTempo()
    {
        // 5 dias em janeiro, com 25 de saldo: a fracao de 14 ainda cabe
        // depois. Recusar agora impediria uma programacao legitima.
        Assert.Empty(Conferir(ComSaldo(), 5));

        var p = ComSaldo(Conceder(5, new DateOnly(2026, 1, 5)));
        Assert.Empty(Conferir(p, 25, inicio: new DateOnly(2026, 6, 1)));
    }

    [Fact]
    public void GozoSobrepostoAOutro_ERecusado()
    {
        var p = ComSaldo(Conceder(15, new DateOnly(2026, 3, 1)));

        // 10/03 cai dentro de 01/03 a 15/03.
        Assert.Contains(RecusaConcessao.SobrepoeOutroGozo,
            Conferir(p, 5, inicio: new DateOnly(2026, 3, 10)));

        // 16/03 e o dia seguinte ao fim: nao sobrepoe.
        Assert.DoesNotContain(RecusaConcessao.SobrepoeOutroGozo,
            Conferir(p, 5, inicio: new DateOnly(2026, 3, 16)));
    }

    [Fact]
    public void VariasRecusasVemJuntas()
    {
        // 40 dias de gozo e 15 de abono: estoura saldo, estoura o terco.
        var recusas = Conferir(ComSaldo(), 40, abono: 15);

        Assert.Contains(RecusaConcessao.SaldoInsuficiente, recusas);
        Assert.Contains(RecusaConcessao.AbonoAcimaDoTerco, recusas);
    }

    [Fact]
    public void CadaRecusaTemExplicacaoEmPortugues()
    {
        foreach (var recusa in Enum.GetValues<RecusaConcessao>())
        {
            var texto = RegrasDeConcessao.Explicar(recusa);

            Assert.NotEqual("Concessao invalida.", texto);
            Assert.EndsWith(".", texto);
        }
    }

    [Fact]
    public void EDoPeriodo_CasaPelasDatas()
    {
        var c = Conceder(30, new DateOnly(2026, 3, 1));

        Assert.True(c.EDoPeriodo(Periodo()));
        Assert.False(c.EDoPeriodo(new PeriodoAquisitivo(
            2, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 30)));
    }
}
