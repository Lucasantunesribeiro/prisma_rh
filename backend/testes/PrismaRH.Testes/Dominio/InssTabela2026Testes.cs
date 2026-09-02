using PrismaRH.Dominio.Folha;
using PrismaRH.Dominio.Parametros;

namespace PrismaRH.Testes.Dominio;

/// <summary>
/// A tabela REAL do INSS vigente a partir de 01/01/2026, com as fronteiras
/// exigidas pelo responsavel pelo projeto.
///
/// Fonte: Portaria Interministerial MPS/MF n. 13, de 09/01/2026, Anexo II -
/// tabela de contribuicao dos segurados empregado, empregado domestico e
/// trabalhador avulso. Confirmada na pagina oficial do INSS em gov.br.
///
/// Os valores esperados foram calculados a mao, faixa a faixa, e estao
/// conferidos no comentario de cada teste. Se um deles falhar, a conta mudou -
/// nao o teste.
/// </summary>
public class InssTabela2026Testes
{
    private static readonly DateTimeOffset Agora = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Ate 1.621,00 -> 7,5% | ate 2.902,84 -> 9% | ate 4.354,27 -> 12% |
    /// ate 8.475,55 -> 14%. Teto: 8.475,55.
    /// </summary>
    private static TabelaInss Tabela2026() => new(
        new DateOnly(2026, 1, 1),
        "Portaria Interministerial MPS/MF n. 13, de 09/01/2026, Anexo II",
        [(1621.00m, 0.075m), (2902.84m, 0.09m), (4354.27m, 0.12m), (8475.55m, 0.14m)],
        Agora);

    private static decimal Inss(decimal baseInss) =>
        CalculadoraInss.Apurar(baseInss, Tabela2026()).Valor;

    // ----------------------------------------------------------- fronteiras

    [Fact]
    public void BaseZero() => Assert.Equal(0m, Inss(0m));

    [Fact]
    public void AbaixoDaPrimeiraFaixa()
    {
        // 1.500,00 x 7,5% = 112,50
        Assert.Equal(112.50m, Inss(1500.00m));
    }

    [Fact]
    public void ExatamenteNoLimiteDaFaixa1()
    {
        // 1.621,00 x 7,5% = 121,575 -> 121,58
        Assert.Equal(121.58m, Inss(1621.00m));
    }

    [Fact]
    public void UmCentavoAcimaDaFaixa1()
    {
        // 1.621,00 x 7,5%  = 121,5750
        //     0,01 x 9%    =   0,0009
        //                    121,5759 -> 121,58
        Assert.Equal(121.58m, Inss(1621.01m));
    }

    [Fact]
    public void ExatamenteNoLimiteDaFaixa2()
    {
        // 1.621,00 x 7,5% = 121,5750
        // 1.281,84 x 9%   = 115,3656
        //                   236,9406 -> 236,94
        Assert.Equal(236.94m, Inss(2902.84m));
    }

    [Fact]
    public void UmCentavoAcimaDaFaixa2()
    {
        // 236,9406 + (0,01 x 12% = 0,0012) = 236,9418 -> 236,94
        Assert.Equal(236.94m, Inss(2902.85m));
    }

    [Fact]
    public void ExatamenteNoLimiteDaFaixa3()
    {
        // 121,5750 + 115,3656 + (1.451,43 x 12% = 174,1716) = 411,1122 -> 411,11
        Assert.Equal(411.11m, Inss(4354.27m));
    }

    [Fact]
    public void UmCentavoAcimaDaFaixa3()
    {
        // 411,1122 + (0,01 x 14% = 0,0014) = 411,1136 -> 411,11
        Assert.Equal(411.11m, Inss(4354.28m));
    }

    [Fact]
    public void ExatamenteNoTeto()
    {
        // 121,5750 + 115,3656 + 174,1716 + (4.121,28 x 14% = 576,9792)
        //   = 988,0914 -> 988,09
        Assert.Equal(988.09m, Inss(8475.55m));
    }

    [Fact]
    public void AcimaDoTeto_NaoAumentaAContribuicao()
    {
        Assert.Equal(988.09m, Inss(8475.56m));
        Assert.Equal(988.09m, Inss(20000.00m));
        Assert.Equal(988.09m, Inss(1000000.00m));
    }

    // ------------------------------------------------- progressividade real

    [Fact]
    public void NaQuartaFaixa_NaoAplica14PorCentoSobreTudo()
    {
        // A prova de que a progressao existe. Base 5.000,00:
        //   1.621,00 x 7,5%  = 121,5750
        //   1.281,84 x 9%    = 115,3656
        //   1.451,43 x 12%   = 174,1716
        //     645,73 x 14%   =  90,4022
        //                      501,5144 -> 501,51
        //
        // A conta ERRADA - 5.000,00 x 14% - daria 700,00, quase 200 reais a
        // mais no desconto de quem ganha 5 mil.
        Assert.Equal(501.51m, Inss(5000.00m));
        Assert.NotEqual(700.00m, Inss(5000.00m));
    }

    [Theory]
    [InlineData(1621.00, 0.075)]
    [InlineData(2902.84, 0.09)]
    [InlineData(4354.27, 0.12)]
    [InlineData(8475.55, 0.14)]
    public void AliquotaEfetiva_ESempreMenorQueADaFaixaAlcancada(decimal limite, decimal aliquotaDaFaixa)
    {
        // Consequencia matematica da progressao: quem esta no topo de uma
        // faixa paga, no conjunto, MENOS que a aliquota daquela faixa - porque
        // so o ultimo trecho pagou aquela aliquota. Se algum dia esta
        // propriedade quebrar, a progressao virou aliquota unica.
        //
        // Compara contra a soma EXATA, e nao contra o valor arredondado: em
        // 1.621,00 a contribuicao exata e 121,575 e o arredondamento para
        // 121,58 faz a efetiva passar de 7,5% por 0,000003. Isso e o
        // arredondamento falando, nao a progressao quebrando.
        var efetiva = CalculadoraInss.Apurar(limite, Tabela2026()).SomaExata / limite;

        Assert.True(
            efetiva <= aliquotaDaFaixa,
            $"Efetiva {efetiva:P4} nao deveria passar de {aliquotaDaFaixa:P2} na base {limite}.");
    }

    [Fact]
    public void ContribuicaoCresceComABase_ateOTeto()
    {
        decimal anterior = -1m;

        foreach (var baseInss in new[] { 0m, 800m, 1621m, 2000m, 2902.84m, 3500m, 4354.27m, 6000m, 8475.55m })
        {
            var atual = Inss(baseInss);
            Assert.True(atual >= anterior, $"Base {baseInss} produziu {atual}, menor que o anterior {anterior}.");
            anterior = atual;
        }
    }

    // ------------------------------------------------------------- memoria

    [Fact]
    public void Memoria_TemBase_UmaLinhaPorFaixaAlcancada_E_Total()
    {
        var apuracao = CalculadoraInss.Apurar(5000.00m, Tabela2026());

        // 1 base + 4 faixas + 1 total
        Assert.Equal(6, apuracao.Passos.Count);
        Assert.Equal("Base de contribuição", apuracao.Passos[0].Descricao);
        Assert.Equal("Total do INSS", apuracao.Passos[^1].Descricao);
        Assert.Equal(501.51m, apuracao.Passos[^1].Valor);
    }

    [Fact]
    public void Memoria_SoMostraAsFaixasQueABaseAlcancou()
    {
        // Base de 1.500 nao chega na segunda faixa: 1 base + 1 faixa + 1 total.
        var apuracao = CalculadoraInss.Apurar(1500.00m, Tabela2026());

        Assert.Equal(3, apuracao.Passos.Count);
        Assert.DoesNotContain(apuracao.Passos, p => p.Descricao.Contains("Faixa 2"));
    }

    [Fact]
    public void Memoria_MostraCadaTrechoEAliquota()
    {
        var apuracao = CalculadoraInss.Apurar(5000.00m, Tabela2026());

        Assert.Equal("1.621,00 x 7,5% = 121,575", apuracao.Passos[1].Expressao);
        Assert.Equal("1.281,84 x 9% = 115,3656", apuracao.Passos[2].Expressao);
        Assert.Equal("1.451,43 x 12% = 174,1716", apuracao.Passos[3].Expressao);
        Assert.Equal("645,73 x 14% = 90,4022", apuracao.Passos[4].Expressao);

        Assert.Contains("de 0,00 a 1.621,00", apuracao.Passos[1].Descricao);
        Assert.Contains("aliquota 7,5%", apuracao.Passos[1].Descricao);
    }

    [Fact]
    public void Memoria_CorrespondeExatamenteAoCalculo()
    {
        // O criterio 6 do responsavel: a memoria tem que bater com a conta.
        // As parcelas exatas estao na expressao das faixas; a soma delas e o
        // SomaExata da apuracao, e o total exibido e essa soma arredondada.
        var apuracao = CalculadoraInss.Apurar(8475.55m, Tabela2026());

        Assert.Equal(988.0914m, apuracao.SomaExata);
        Assert.Equal(988.09m, apuracao.Valor);
        Assert.Equal(apuracao.Valor, apuracao.Passos[^1].Valor);
        Assert.Equal("soma exata 988,0914 arredondada para 988,09", apuracao.Passos[^1].Expressao);
    }

    [Fact]
    public void Memoria_RegistraOCorteNoTeto()
    {
        var apuracao = CalculadoraInss.Apurar(20000.00m, Tabela2026());

        Assert.Equal("20.000,00 limitada ao teto de 8.475,55", apuracao.Passos[0].Expressao);
        Assert.Equal(8475.55m, apuracao.Passos[0].Valor);
        Assert.Equal(20000.00m, apuracao.BaseInss);
    }

    [Fact]
    public void Memoria_CabeNoBanco()
    {
        // A coluna e varchar(200): uma descricao truncada esconderia a faixa.
        var apuracao = CalculadoraInss.Apurar(8475.55m, Tabela2026());

        Assert.All(apuracao.Passos, p =>
        {
            Assert.True(p.Descricao.Length <= LinhaMemoriaCalculo.TamanhoMaximoDescricao, p.Descricao);
            Assert.True(p.Expressao.Length <= LinhaMemoriaCalculo.TamanhoMaximoExpressao, p.Expressao);
        });
    }

    // ------------------------------------------------------ arredondamento

    [Fact]
    public void Arredondamento_AplicadoUmaVezNoTotal()
    {
        // ⚠️ ESTE TESTE TRAVA A REGRA PENDENTE DE CONFIRMACAO LEGAL.
        //
        // No teto, as parcelas exatas somam 988,0914. Arredondando so no fim
        // da 988,09; arredondando cada faixa daria 988,10; truncando cada
        // faixa daria 988,07. Ver o comentario em
        // CalculadoraInss.ArredondarContribuicao.
        //
        // Se a fonte oficial disser outra coisa, este teste falha de proposito
        // e aponta exatamente o que revisar.
        var apuracao = CalculadoraInss.Apurar(8475.55m, Tabela2026());

        Assert.Equal(988.09m, apuracao.Valor);

        var somandoFaixasArredondadas = apuracao.Passos
            .Where(p => p.Descricao.StartsWith("Faixa "))
            .Sum(p => p.Valor);

        Assert.Equal(988.10m, somandoFaixasArredondadas);
        Assert.NotEqual(somandoFaixasArredondadas, apuracao.Valor);
    }

    // ---------------------------------------------------------- vigencia

    [Fact]
    public void OutraVigencia_MudaOResultado_SemTocarNoAlgoritmo()
    {
        // Criterio 4 do responsavel. A tabela de 2027 e ficticia aqui: o que
        // se prova e que basta cadastrar uma vigencia nova.
        var tabela2027 = new TabelaInss(
            new DateOnly(2027, 1, 1),
            "Tabela ficticia de 2027 - so para provar a troca de vigencia",
            [(1700.00m, 0.075m), (3000.00m, 0.09m), (4500.00m, 0.12m), (9000.00m, 0.14m)],
            Agora);

        var em2026 = CalculadoraInss.Apurar(5000.00m, Tabela2026()).Valor;
        var em2027 = CalculadoraInss.Apurar(5000.00m, tabela2027).Valor;

        Assert.NotEqual(em2026, em2027);

        // 1.700,00 x 7,5% = 127,50
        // 1.300,00 x 9%   = 117,00
        // 1.500,00 x 12%  = 180,00
        //   500,00 x 14%  =  70,00
        //                   494,50
        Assert.Equal(494.50m, em2027);
    }

    [Fact]
    public void FolhaHistorica_UsaATabelaDaPropriaCompetencia()
    {
        // Criterio 5 do responsavel.
        var tabela2025 = new TabelaInss(
            new DateOnly(2025, 1, 1), "Tabela ficticia de 2025",
            [(1500.00m, 0.075m), (2700.00m, 0.09m), (4000.00m, 0.12m), (8000.00m, 0.14m)], Agora);

        var tabelas = new[] { Tabela2026(), tabela2025 };

        var dezembro2025 = TabelaInss.VigenteEm(tabelas, new Competencia(2025, 12).PrimeiroDia);
        var janeiro2026 = TabelaInss.VigenteEm(tabelas, new Competencia(2026, 1).PrimeiroDia);
        var agosto2026 = TabelaInss.VigenteEm(tabelas, new Competencia(2026, 8).PrimeiroDia);

        Assert.Same(tabela2025, dezembro2025);
        Assert.Equal(8475.55m, janeiro2026!.Teto);
        Assert.Equal(8475.55m, agosto2026!.Teto);

        // E recalcular a folha de dezembro/2025 hoje continua usando a tabela
        // de 2025: o passado nao e reescrito pela tabela nova.
        Assert.Equal(8000.00m, dezembro2025!.Teto);
    }

    [Fact]
    public void Tabela2026_TemTetoEFonteRegistrada()
    {
        var tabela = Tabela2026();

        Assert.Equal(8475.55m, tabela.Teto);
        Assert.Equal(4, tabela.Faixas.Count);
        Assert.Contains("Portaria Interministerial", tabela.Fonte);
        Assert.Equal(new DateOnly(2026, 1, 1), tabela.VigenciaInicio);
    }
}
