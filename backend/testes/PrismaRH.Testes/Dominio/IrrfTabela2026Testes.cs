using PrismaRH.Dominio.Parametros;

namespace PrismaRH.Testes.Dominio;

/// <summary>
/// O IRRF com a tabela REAL vigente em 2026.
///
/// FONTE OFICIAL (CLAUDE.md secao 29):
///
/// - Lei n. 15.191, de 11/08/2025 - tabela progressiva mensal, publicada em
///   gov.br/receitafederal/pt-br/assuntos/meu-imposto-de-renda/tabelas/2026;
/// - Lei n. 15.270, de 26/11/2025 - redutor mensal, formula
///   978,62 - 0,133145 x rendimentos tributaveis.
///
/// Os cinco primeiros testes reproduzem, numero por numero, os EXEMPLOS
/// OFICIAIS publicados pela Receita Federal em
/// gov.br/receitafederal/pt-br/assuntos/meu-imposto-de-renda/tabelas/
/// exemplos-de-aplicacao-da-lei-15-270-2025.
///
/// Eles valem mais que qualquer teste que eu escrevesse sozinho: nao provam
/// que o codigo faz o que eu quis, provam que ele faz o que a Receita publicou.
/// </summary>
public class IrrfTabela2026Testes
{
    private static readonly DateTimeOffset Agora = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    private static TabelaIrrf Tabela2026() => new(
        new DateOnly(2026, 1, 1),
        "Lei n. 15.191, de 11/08/2025 (tabela) e Lei n. 15.270, de 26/11/2025 (redutor)",
        deducaoPorDependente: 189.59m,
        descontoSimplificado: 607.20m,
        redutorBase: 978.62m,
        redutorCoeficiente: 0.133145m,
        [
            (2428.80m, 0m, 0m),
            (2826.65m, 0.075m, 182.16m),
            (3751.05m, 0.15m, 394.16m),
            (4664.68m, 0.225m, 675.49m),
            (0m, 0.275m, 908.73m),   // o limite da ultima e ignorado: sem teto
        ],
        Agora);

    private static ApuracaoIrrf Apurar(decimal rendimentos, decimal inss, int dependentes = 0) =>
        CalculadoraIrrf.Apurar(rendimentos, inss, dependentes, Tabela2026());

    // ---------------------------------------------- exemplos oficiais da RFB

    [Fact]
    public void ExemploOficial1_AliquotaZero()
    {
        var a = Apurar(3036.00m, 257.73m);

        Assert.Equal(FormaDeducaoIrrf.Simplificada, a.Forma);
        Assert.Equal(2428.80m, a.BaseCalculo);
        Assert.Equal(0m, a.ImpostoDaTabela);
        Assert.Equal(0m, a.Valor);
    }

    [Fact]
    public void ExemploOficial2_RedutorZeraOImposto()
    {
        var a = Apurar(4000.00m, 373.41m);

        Assert.Equal(FormaDeducaoIrrf.Simplificada, a.Forma);
        Assert.Equal(3392.80m, a.BaseCalculo);
        Assert.Equal(114.76m, a.ImpostoDaTabela);

        // O redutor bruto seria 446,04. Fica LIMITADO ao imposto: ele zera,
        // nunca restitui.
        Assert.Equal(114.76m, a.Redutor);
        Assert.Equal(0m, a.Valor);
    }

    [Fact]
    public void ExemploOficial3_CincoMil_ERedutorMaximo()
    {
        var a = Apurar(5000.00m, 509.60m);

        Assert.Equal(4392.80m, a.BaseCalculo);
        Assert.Equal(312.89m, a.ImpostoDaTabela);
        Assert.Equal(312.89m, a.Redutor);

        // A promessa da Lei 15.270/2025: quem ganha ate 5.000 nao paga.
        Assert.Equal(0m, a.Valor);
    }

    [Fact]
    public void ExemploOficial4_DeducoesLegaisVencemOSimplificado()
    {
        var a = Apurar(6000.00m, 649.60m);

        // 6.000 - 649,60 = 5.350,40 e MENOR que 6.000 - 607,20 = 5.392,80.
        Assert.Equal(FormaDeducaoIrrf.Legal, a.Forma);
        Assert.Equal(5350.40m, a.BaseCalculo);
        Assert.Equal(562.63m, a.ImpostoDaTabela);

        // 978,62 - 0,133145 x 6.000 = 179,75, e desta vez o redutor NAO zera.
        Assert.Equal(179.75m, a.Redutor);
        Assert.Equal(382.88m, a.Valor);
    }

    [Fact]
    public void ExemploOficial5_AcimaDoLimite_SemRedutor()
    {
        var a = Apurar(7607.20m, 0m);

        Assert.Equal(FormaDeducaoIrrf.Simplificada, a.Forma);
        Assert.Equal(7000.00m, a.BaseCalculo);
        Assert.Equal(1016.27m, a.ImpostoDaTabela);

        // A formula daria negativo acima de 7.350: o redutor simplesmente nao
        // existe ali.
        Assert.Equal(0m, a.Redutor);
        Assert.Equal(1016.27m, a.Valor);
    }

    // ------------------------------------------------------------- limites

    [Fact]
    public void LimiteDaIsencao_PERTENCE_AFaixaIsenta()
    {
        // A base EXATAMENTE no limite e isenta. Um erro de <= para < aqui
        // cobraria imposto de quem a lei isenta.
        var a = CalculadoraIrrf.Apurar(2428.80m, 0m, 0, SemDescontoSimplificado());

        Assert.Equal(0m, a.ImpostoDaTabela);
        Assert.Contains(a.Passos, p => p.Descricao.Contains("isencao", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UmCentavoAcimaDaIsencao_CaiNaSegundaFaixa_ComImpostoDesprezivel()
    {
        // 2.428,81 x 7,5% - 182,16 = 0,00075, que arredonda para ZERO.
        //
        // Nao e defeito: e a propria calibragem da parcela a deduzir, que
        // existe para a transicao entre faixas ser continua. O teste registra
        // isso, porque "passou da isencao" e "passou a pagar" nao sao a mesma
        // coisa - e alguem poderia ler o zero como erro.
        var a = CalculadoraIrrf.Apurar(2428.81m, 0m, 0, SemDescontoSimplificado());

        Assert.Equal(0m, a.ImpostoDaTabela);
        Assert.DoesNotContain(a.Passos, p => p.Descricao.Contains("isencao", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(a.Passos, p => p.Descricao.Contains("7,5%"));
    }

    [Fact]
    public void UmRealAcimaDaIsencao_JaPagaCentavos()
    {
        // 2.429,80 x 7,5% - 182,16 = 0,075 -> 0,08
        var a = CalculadoraIrrf.Apurar(2429.80m, 0m, 0, SemDescontoSimplificado());

        Assert.Equal(0.08m, a.ImpostoDaTabela);
    }

    [Theory]
    [InlineData(2826.65, 29.84)]   // 2.826,65 x 7,5% - 182,16
    [InlineData(3751.05, 168.50)]  // 3.751,05 x 15% - 394,16
    [InlineData(4664.68, 374.06)]  // 4.664,68 x 22,5% - 675,49
    public void TopoDeCadaFaixa(decimal baseAlvo, decimal esperado) =>
        Assert.Equal(esperado, CalculadoraIrrf.Apurar(baseAlvo, 0m, 0, SemDescontoSimplificado()).ImpostoDaTabela);

    [Fact]
    public void NaoTemTeto_AoContrarioDoInss()
    {
        // 100.000 - 607,20 = 99.392,80; x 27,5% - 908,73 = 26.424,29
        var a = Apurar(100000.00m, 0m);

        Assert.Equal(99392.80m, a.BaseCalculo);
        Assert.Equal(26424.29m, a.Valor);
    }

    [Fact]
    public void NaoESomaTrechoATrecho()
    {
        // A pegadinha classica: quem implementa o IRRF como o INSS soma
        // 7,5% do primeiro trecho, 15% do segundo, e assim por diante.
        //
        // Base 5.350,40 pela formula oficial: 562,63.
        // Trecho a trecho daria o MESMO numero - e por isso o erro nao
        // aparece hoje. O que este teste trava e a FORMULA: aliquota unica
        // sobre a base inteira, menos a parcela a deduzir.
        var a = CalculadoraIrrf.Apurar(5350.40m, 0m, 0, SemDescontoSimplificado());

        Assert.Equal(562.63m, a.ImpostoDaTabela);
        Assert.Equal(decimal.Round((5350.40m * 0.275m) - 908.73m, 2), a.ImpostoDaTabela);
    }

    // --------------------------------------------------------- dependentes

    [Fact]
    public void CadaDependenteAbate18959()
    {
        var sem = Apurar(6000.00m, 649.60m);
        var com = Apurar(6000.00m, 649.60m, dependentes: 2);

        Assert.Equal(2 * 189.59m, com.DeducaoDependentes);
        Assert.Equal(sem.BaseCalculo - 379.18m, com.BaseCalculo);
        Assert.True(com.Valor < sem.Valor);
    }

    [Fact]
    public void DependentesPodemMudarAFormaVencedora()
    {
        // Sem dependente, a legal vence (exemplo oficial 4). Com dependente
        // ela vence por mais ainda - o que o teste prova e que a deducao por
        // dependente NAO se soma ao desconto simplificado.
        var a = Apurar(6000.00m, 0m, dependentes: 1);

        // Legal: 6.000 - 0 - 189,59 = 5.810,41
        // Simplificada: 6.000 - 607,20 = 5.392,80  <- menor, vence
        Assert.Equal(FormaDeducaoIrrf.Simplificada, a.Forma);
        Assert.Equal(5392.80m, a.BaseCalculo);
        Assert.Equal(0m, a.DeducaoDependentes == 189.59m ? 0m : 1m);
    }

    [Fact]
    public void MuitosDependentes_NaoLevamABaseAbaixoDeZero()
    {
        var a = Apurar(1000.00m, 100.00m, dependentes: 50);

        Assert.Equal(0m, a.BaseCalculo);
        Assert.Equal(0m, a.Valor);
    }

    // ------------------------------------------------------------- memoria

    [Fact]
    public void Memoria_ExplicaOCaminhoInteiro()
    {
        var a = Apurar(6000.00m, 649.60m, dependentes: 1);

        // rendimentos, INSS, dependentes, base, imposto, redutor, total
        Assert.Equal(7, a.Passos.Count);
        Assert.Equal("Rendimentos tributaveis", a.Passos[0].Descricao);
        Assert.Equal("Deducao do INSS", a.Passos[1].Descricao);
        Assert.Contains("1 dependente", a.Passos[2].Descricao);
        Assert.Equal("Base de calculo do IRRF", a.Passos[3].Descricao);
        Assert.Contains("Redutor", a.Passos[5].Descricao);
        Assert.Equal("Total do IRRF", a.Passos[^1].Descricao);
        Assert.Equal(a.Valor, a.Passos[^1].Valor);
    }

    [Fact]
    public void Memoria_DizQuandoOSimplificadoVenceu()
    {
        var a = Apurar(4000.00m, 373.41m);

        Assert.Contains(a.Passos, p => p.Descricao.Contains("simplificado", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(a.Passos, p => p.Descricao == "Deducao do INSS");
    }

    // ------------------------------------------------------------ recusas

    [Fact]
    public void RendimentosNegativos_SaoRecusados() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Apurar(-1m, 0m));

    [Fact]
    public void InssNegativo_ERecusado() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Apurar(1000m, -1m));

    [Fact]
    public void DependentesNegativos_SaoRecusados() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Apurar(1000m, 0m, -1));

    // -------------------------------------------------------------- tabela

    [Fact]
    public void Tabela_SemFonte_ERecusada()
    {
        var erro = Assert.Throws<ArgumentException>(() => new TabelaIrrf(
            new DateOnly(2026, 1, 1), "   ", 189.59m, 607.20m, 978.62m, 0.133145m,
            [(2428.80m, 0m, 0m), (0m, 0.275m, 908.73m)], Agora));

        Assert.Contains("fonte", erro.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tabela_ComCoeficienteEmPercentual_ERecusada() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new TabelaIrrf(
            new DateOnly(2026, 1, 1), "x", 189.59m, 607.20m, 978.62m, 13.3145m,
            [(2428.80m, 0m, 0m), (0m, 0.275m, 908.73m)], Agora));

    [Fact]
    public void Tabela_ComRedutorPelaMetade_ERecusada()
    {
        // Base sem coeficiente daria redutor constante para qualquer renda.
        var erro = Assert.Throws<ArgumentException>(() => new TabelaIrrf(
            new DateOnly(2026, 1, 1), "x", 189.59m, 607.20m, 978.62m, 0m,
            [(2428.80m, 0m, 0m), (0m, 0.275m, 908.73m)], Agora));

        Assert.Contains("juntos", erro.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tabela_FaixaIsentaComParcela_ERecusada() =>
        // Produziria imposto NEGATIVO na primeira faixa.
        Assert.Throws<ArgumentException>(() => new TabelaIrrf(
            new DateOnly(2026, 1, 1), "x", 189.59m, 607.20m, 978.62m, 0.133145m,
            [(2428.80m, 0m, 10m), (0m, 0.275m, 908.73m)], Agora));

    [Fact]
    public void Tabela_LimitesNaoCrescentes_SaoRecusados() =>
        Assert.Throws<ArgumentException>(() => new TabelaIrrf(
            new DateOnly(2026, 1, 1), "x", 189.59m, 607.20m, 978.62m, 0.133145m,
            [(2428.80m, 0m, 0m), (2000m, 0.075m, 182.16m), (0m, 0.275m, 908.73m)], Agora));

    [Fact]
    public void UltimaFaixa_NaoTemTeto()
    {
        var tabela = Tabela2026();

        Assert.True(tabela.Faixas[^1].SemTeto);
        Assert.Null(tabela.Faixas[^1].LimiteSuperior);
        Assert.All(tabela.Faixas.Take(4), f => Assert.False(f.SemTeto));
    }

    [Fact]
    public void LimiteDoRedutor_EDerivadoDaFormula_ENaoDoNumeroAnunciado()
    {
        // 978,62 / 0,133145 = 7.350,0319..., e nao os R$ 7.350,00 redondos que
        // a divulgacao anuncia.
        //
        // A diferenca e IRRELEVANTE na pratica e o teste existe para provar
        // isso: em 7.350,00 exatos o redutor bruto vale 0,004, que arredonda
        // para zero. Ninguem paga nem deixa de pagar por causa disso.
        //
        // O que NAO se pode fazer e "corrigir" a propriedade para devolver
        // 7.350,00: isso seria cravar no codigo um numero de divulgacao no
        // lugar do que a formula da lei produz.
        Assert.Equal(7350.03m, Tabela2026().LimiteDoRedutor);

        var noNumeroAnunciado = CalculadoraIrrf.Apurar(7350.00m, 0m, 0, Tabela2026());
        Assert.Equal(0m, noNumeroAnunciado.Redutor);
    }

    [Fact]
    public void LimiteIsencao_EOTopoDaPrimeiraFaixa() =>
        Assert.Equal(2428.80m, Tabela2026().LimiteIsencao);

    [Fact]
    public void VigenteEm_EscolheAMaisRecenteQueJaComecou()
    {
        var antiga = new TabelaIrrf(
            new DateOnly(2024, 2, 1), "anterior", 189.59m, 564.80m, 0m, 0m,
            [(2259.20m, 0m, 0m), (0m, 0.275m, 896.00m)], Agora);
        var nova = Tabela2026();

        Assert.Same(antiga, TabelaIrrf.VigenteEm([antiga, nova], new DateOnly(2025, 6, 1)));
        Assert.Same(nova, TabelaIrrf.VigenteEm([antiga, nova], new DateOnly(2026, 8, 1)));
        Assert.Null(TabelaIrrf.VigenteEm([antiga, nova], new DateOnly(2024, 1, 31)));
    }

    [Fact]
    public void TabelaSemRedutor_CalculaSemEle()
    {
        // A de 2024 nao tinha redutor. O calculo precisa funcionar assim.
        var antiga = new TabelaIrrf(
            new DateOnly(2024, 2, 1), "anterior", 189.59m, 564.80m, 0m, 0m,
            [(2259.20m, 0m, 0m), (0m, 0.275m, 896.00m)], Agora);

        var a = CalculadoraIrrf.Apurar(5000m, 0m, 0, antiga);

        Assert.False(antiga.TemRedutor);
        Assert.Equal(0m, a.Redutor);
        Assert.True(a.Valor > 0);
    }

    /// <summary>
    /// A tabela real, com o desconto simplificado zerado.
    ///
    /// Serve para exercitar a FAIXA em isolamento: com o simplificado ativo,
    /// escolher a base exata de um limite exigiria resolver a equacao de tras
    /// para frente, e o teste passaria a medir duas coisas de uma vez.
    /// </summary>
    private static TabelaIrrf SemDescontoSimplificado() => new(
        new DateOnly(2026, 1, 1),
        "Lei n. 15.191/2025, usada nos testes sem o desconto simplificado",
        deducaoPorDependente: 189.59m,
        descontoSimplificado: 0m,
        redutorBase: 978.62m,
        redutorCoeficiente: 0.133145m,
        [
            (2428.80m, 0m, 0m),
            (2826.65m, 0.075m, 182.16m),
            (3751.05m, 0.15m, 394.16m),
            (4664.68m, 0.225m, 675.49m),
            (0m, 0.275m, 908.73m),
        ],
        Agora);
}
