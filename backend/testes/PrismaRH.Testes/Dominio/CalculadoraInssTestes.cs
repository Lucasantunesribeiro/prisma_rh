using PrismaRH.Dominio.Parametros;

namespace PrismaRH.Testes.Dominio;

/// <summary>
/// Fase 4B: a tabela progressiva do INSS e a conta que ela produz.
///
/// TODA tabela aqui e INVENTADA, com numeros redondos escolhidos para a conta
/// ser conferivel de cabeca. Isso e deliberado: teste que depende da tabela
/// legal vigente quebra sozinho toda vez que a lei muda, e ai o time aprende a
/// ignorar o teste vermelho. O que se prova aqui e a MATEMATICA - progressao,
/// teto, bordas, arredondamento. Que a tabela real esteja correta e
/// responsabilidade da fonte oficial registrada em TabelaInss.Fonte.
/// </summary>
public class CalculadoraInssTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Tabela de mentira, de propósito redonda:
    ///   ate 1.000 -> 10%
    ///   ate 2.000 -> 20%
    ///   ate 3.000 -> 30%   (teto: 3.000)
    /// </summary>
    private static TabelaInss Tabela(DateOnly? inicio = null) =>
        new(inicio ?? new DateOnly(2026, 1, 1),
            "Tabela ficticia de teste - nao e fonte legal",
            [(1000m, 0.10m), (2000m, 0.20m), (3000m, 0.30m)],
            Agora);

    // ------------------------------------------------------------- progressao

    [Fact]
    public void BaseDentroDaPrimeiraFaixa_PagaSoAPrimeiraAliquota()
    {
        var apuracao = CalculadoraInss.Apurar(800m, Tabela());

        Assert.Equal(80m, apuracao.Valor);

        // Base + a unica faixa alcancada + total.
        Assert.Equal(3, apuracao.Passos.Count);
    }

    [Fact]
    public void BaseNaSegundaFaixa_PagaProgressivo_E_NaoAliquotaUnica()
    {
        // 1.000 x 10% + 500 x 20% = 100 + 100 = 200.
        // A conta errada, e a mais comum, seria 1.500 x 20% = 300.
        var apuracao = CalculadoraInss.Apurar(1500m, Tabela());

        Assert.Equal(200m, apuracao.Valor);
        Assert.NotEqual(300m, apuracao.Valor);
    }

    [Fact]
    public void BaseNaTerceiraFaixa_SomaAsTres()
    {
        // 1.000 x 10% + 1.000 x 20% + 500 x 30% = 100 + 200 + 150 = 450
        var apuracao = CalculadoraInss.Apurar(2500m, Tabela());

        Assert.Equal(450m, apuracao.Valor);
    }

    [Fact]
    public void BaseNoTetoExato_PagaTodasAsFaixasCheias()
    {
        // 100 + 200 + 300 = 600
        var apuracao = CalculadoraInss.Apurar(3000m, Tabela());

        Assert.Equal(600m, apuracao.Valor);
        Assert.Equal(3000m, apuracao.BaseLimitadaAoTeto);
    }

    // -------------------------------------------------------------- teto

    [Fact]
    public void BaseAcimaDoTeto_NaoPagaMaisQueOTeto()
    {
        var noTeto = CalculadoraInss.Apurar(3000m, Tabela());
        var acima = CalculadoraInss.Apurar(50000m, Tabela());

        Assert.Equal(noTeto.Valor, acima.Valor);
        Assert.Equal(3000m, acima.BaseLimitadaAoTeto);
        Assert.Equal(50000m, acima.BaseInss);
    }

    [Fact]
    public void BaseAcimaDoTeto_RegistraOCorteNaMemoria()
    {
        var apuracao = CalculadoraInss.Apurar(50000m, Tabela());

        // O corte aparece na expressao da linha da base.
        Assert.Contains("teto", apuracao.Passos[0].Expressao, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3000m, apuracao.Passos[0].Valor);
    }

    [Fact]
    public void BaseAbaixoDoTeto_NaoInventaPassoDeTeto()
    {
        var apuracao = CalculadoraInss.Apurar(1500m, Tabela());

        Assert.DoesNotContain("teto", apuracao.Passos[0].Expressao, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------- bordas

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.01, 0.00)]     // 0,01 x 10% = 0,001 -> arredonda para zero
    [InlineData(1000, 100)]      // ultimo centavo da faixa 1
    [InlineData(1000.01, 100.00)]// primeiro centavo da faixa 2: +0,002
    [InlineData(2000, 300)]      // ultimo centavo da faixa 2
    [InlineData(2000.01, 300.00)]
    public void Bordas(decimal baseInss, decimal esperado) =>
        Assert.Equal(esperado, CalculadoraInss.Apurar(baseInss, Tabela()).Valor);

    [Fact]
    public void BaseZero_NaoAlcancaFaixaNenhuma()
    {
        var apuracao = CalculadoraInss.Apurar(0m, Tabela());

        Assert.Equal(0m, apuracao.Valor);

        // So a base e o total: nenhuma faixa foi alcancada.
        Assert.Equal(2, apuracao.Passos.Count);
        Assert.DoesNotContain(apuracao.Passos, p => p.Descricao.StartsWith("Faixa "));
    }

    [Fact]
    public void BaseNegativa_ERecusada() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CalculadoraInss.Apurar(-1m, Tabela()));

    // ------------------------------------------------------ arredondamento

    [Fact]
    public void Arredonda_UmaVezSo_NoValorFinal()
    {
        // Tabela escolhida para produzir dizima em toda faixa:
        //   ate 1.000 -> 7,5% ; ate 2.000 -> 9% ; ate 3.000 -> 12%
        // 1.000 x 7,5% = 75 ; 1.000 x 9% = 90 ; 33,33 x 12% = 3,9996
        // Soma exata = 168,9996 -> arredonda para 169,00.
        // Arredondando faixa a faixa daria 75 + 90 + 4,00 = 169,00 tambem,
        // mas o teste existe para travar a REGRA, nao a coincidencia: o valor
        // final tem que sair da soma exata.
        var tabela = new TabelaInss(
            new DateOnly(2026, 1, 1), "ficticia",
            [(1000m, 0.075m), (2000m, 0.09m), (3000m, 0.12m)], Agora);

        var apuracao = CalculadoraInss.Apurar(2033.33m, tabela);

        Assert.Equal(169.00m, apuracao.Valor);
    }

    [Fact]
    public void MemoriaMostraCadaFaixa_ComAExpressao()
    {
        var apuracao = CalculadoraInss.Apurar(1500m, Tabela());

        // Base, duas faixas e o total.
        Assert.Equal(4, apuracao.Passos.Count);
        Assert.Equal("1.000,00 x 10% = 100", apuracao.Passos[1].Expressao);
        Assert.Equal("500,00 x 20% = 100", apuracao.Passos[2].Expressao);
        Assert.Equal(200m, apuracao.Passos[3].Valor);
    }

    [Fact]
    public void MemoriaSempreTermina_NaLinhaDoTotal()
    {
        // Mesmo com uma faixa so. O formato pedido pelo responsavel termina
        // sempre em "Total INSS", e uma memoria que as vezes tem a linha e as
        // vezes nao obrigaria a tela a tratar dois casos.
        var apuracao = CalculadoraInss.Apurar(500m, Tabela());

        Assert.Equal("Total do INSS", apuracao.Passos[^1].Descricao);
        Assert.Equal(50m, apuracao.Passos[^1].Valor);
    }

    // -------------------------------------------------------- tabela e vigencia

    [Fact]
    public void Tabela_SemFaixa_ERecusada() =>
        Assert.Throws<ArgumentException>(() =>
            new TabelaInss(new DateOnly(2026, 1, 1), "x", [], Agora));

    [Fact]
    public void Tabela_SemFonte_ERecusada()
    {
        // CLAUDE.md secao 29: regra legal sem fonte registrada nao entra.
        var erro = Assert.Throws<ArgumentException>(() =>
            new TabelaInss(new DateOnly(2026, 1, 1), "   ", [(1000m, 0.1m)], Agora));

        Assert.Contains("fonte", erro.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tabela_ComLimitesForaDeOrdem_ERecusada() =>
        Assert.Throws<ArgumentException>(() =>
            new TabelaInss(new DateOnly(2026, 1, 1), "x", [(2000m, 0.1m), (1000m, 0.2m)], Agora));

    [Fact]
    public void Tabela_ComLimitesRepetidos_ERecusada() =>
        Assert.Throws<ArgumentException>(() =>
            new TabelaInss(new DateOnly(2026, 1, 1), "x", [(1000m, 0.1m), (1000m, 0.2m)], Agora));

    [Fact]
    public void Faixa_ComAliquotaEmPercentual_ERecusada() =>
        // 7.5 em vez de 0.075: o erro que descontaria o salario inteiro.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TabelaInss(new DateOnly(2026, 1, 1), "x", [(1000m, 7.5m)], Agora));

    [Fact]
    public void Tabela_TetoEOLimiteDaUltimaFaixa() =>
        Assert.Equal(3000m, Tabela().Teto);

    [Fact]
    public void VigenteEm_EscolheAMaisRecenteQueJaComecou()
    {
        var antiga = Tabela(new DateOnly(2025, 1, 1));
        var nova = Tabela(new DateOnly(2026, 1, 1));
        var futura = Tabela(new DateOnly(2027, 1, 1));

        var escolhida = TabelaInss.VigenteEm([futura, antiga, nova], new DateOnly(2026, 8, 1));

        Assert.Same(nova, escolhida);
    }

    [Fact]
    public void VigenteEm_NoDiaExatoDoInicio_JaValeATabelaNova()
    {
        var antiga = Tabela(new DateOnly(2025, 1, 1));
        var nova = Tabela(new DateOnly(2026, 1, 1));

        Assert.Same(nova, TabelaInss.VigenteEm([antiga, nova], new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public void VigenteEm_AntesDeQualquerTabela_DevolveNull()
    {
        // Null de proposito: a folha precisa recusar com mensagem
        // compreensivel, e nao aplicar a tabela mais proxima que encontrar.
        var nova = Tabela(new DateOnly(2026, 1, 1));

        Assert.Null(TabelaInss.VigenteEm([nova], new DateOnly(2025, 12, 31)));
    }

    [Fact]
    public void VigenteEm_SemTabelaNenhuma_DevolveNull() =>
        Assert.Null(TabelaInss.VigenteEm([], new DateOnly(2026, 8, 1)));
}
