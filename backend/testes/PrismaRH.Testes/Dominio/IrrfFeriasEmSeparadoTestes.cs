using PrismaRH.Dominio.Folha;
using PrismaRH.Dominio.Parametros;

namespace PrismaRH.Testes.Dominio;

/// <summary>
/// IRRF de férias apurado **em separado** da folha mensal.
///
/// ## Por que este arquivo existe
///
/// O `CLAUDE.md §24.19 item 5` registrava, desde a Fase 4E, uma suposta pendência:
/// *"IRRF apurado por folha, sem somar rendimentos do mesmo mês"*. A hipótese
/// era que a tabela progressiva exigia somar férias e mensal, e que apurar em
/// separado retinha menos que o devido.
///
/// **A hipótese estava errada, e a fonte oficial diz o contrário.**
///
/// ### IN RFB 1.500/2014, art. 29
///
/// > *"Art. 29. No caso de pagamento de férias, inclusive as pagas em dobro
/// > (...) a base de cálculo corresponde ao salário relativo ao mês de férias,
/// > acrescido, conforme o caso, de 1/3 (um terço) do seu valor.*
/// >
/// > *§ 1º O cálculo do imposto deve ser efetuado **em separado de qualquer
/// > outro rendimento pago no mês**, inclusive no caso de férias indenizadas,
/// > ainda que proporcionais, pagas em rescisão de contrato de trabalho.*
/// >
/// > *§ 4º Na DAA, as férias devem ser tributadas em conjunto com os demais
/// > rendimentos."*
///
/// O mesmo texto está no **MAFON da Receita Federal**, seção FÉRIAS: *"deve ser
/// tributado no mês de seu pagamento e em separado de qualquer outro rendimento
/// pago no mês"*.
///
/// ### De onde veio a confusão
///
/// A regra geral **existe** e está no mesmo manual, no código 0561:
///
/// > *"O imposto será retido por ocasião de cada pagamento e se, no mês, houver
/// > mais de um pagamento, a qualquer título, pela mesma fonte pagadora,
/// > aplicar-se-á a alíquota correspondente à soma dos rendimentos pagos."*
///
/// A pendência aplicou essa regra geral às férias. Mas o art. 29 é **norma
/// especial**, e norma especial afasta a geral no caso que ela regula. Não há
/// conflito entre as duas — há especialidade.
///
/// ### E o "prejuízo" que a pendência apontava?
///
/// Ele existe, é conhecido, e **é o desenho legal**: apurar em separado retém
/// menos do que a soma reteria, e o § 4º manda somar tudo **na declaração
/// anual**. A retenção é antecipação, não o imposto final.
///
/// ## O que estes testes travam
///
/// Que ninguém "corrija" isto no futuro somando as duas folhas. Um sistema de
/// folha que soma férias com a mensal para reter IRRF está **errado**, e erra
/// contra o contribuinte.
/// </summary>
public sealed class IrrfFeriasEmSeparadoTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Tabela de 2026 — a mesma dos demais testes de IRRF, repetida aqui de
    /// propósito. Se a semeadura mudar, este teste precisa falhar em vez de
    /// acompanhar em silêncio.
    /// </summary>
    private static TabelaIrrf Tabela() => new(
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
            (0m, 0.275m, 908.73m),
        ],
        Agora);

    /// <summary>
    /// ⚠️ **O teste que prova que a separação não é detalhe — ela muda o valor.**
    ///
    /// Se os dois cálculos dessem o mesmo resultado, a regra do art. 29 não teria
    /// consequência e o teste não provaria nada.
    /// </summary>
    [Fact]
    public void ApurarEmSeparadoRetemMenosQueApurarSobreASoma()
    {
        var tabela = Tabela();

        const decimal Mensal = 4_000m;
        const decimal Ferias = 5_333.33m;   // 4.000 + 1/3

        var separadoMensal = CalculadoraIrrf.Apurar(Mensal, 0m, 0, tabela).Valor;
        var separadoFerias = CalculadoraIrrf.Apurar(Ferias, 0m, 0, tabela).Valor;

        var somado = CalculadoraIrrf.Apurar(Mensal + Ferias, 0m, 0, tabela).Valor;

        // ⚠️ A diferenca e o efeito da tabela PROGRESSIVA. Ela e conhecida, e o
        // art. 29 § 4o a resolve na declaracao anual - nao na retencao.
        Assert.True(
            separadoMensal + separadoFerias < somado,
            $"separado={separadoMensal + separadoFerias} somado={somado}");
    }

    /// <summary>
    /// O terço constitucional **compõe** a base de férias — é o próprio caput do
    /// art. 29: *"acrescido, conforme o caso, de 1/3 (um terço) do seu valor"*.
    /// </summary>
    [Fact]
    public void OTercoConstitucionalEntraNaBaseDeFerias()
    {
        var tabela = Tabela();

        // ⚠️ Bases ACIMA do alcance do redutor da Lei 15.270/2025. Abaixo dele
        // o imposto e zero nos dois casos, e o teste passaria sem provar nada.
        var semTerco = CalculadoraIrrf.Apurar(6_000m, 0m, 0, tabela).Valor;
        var comTerco = CalculadoraIrrf.Apurar(8_000m, 0m, 0, tabela).Valor;

        Assert.True(comTerco > semTerco, $"semTerco={semTerco} comTerco={comTerco}");
    }

    /// <summary>
    /// ⚠️ **Cada apuração é independente**, e é isso que "em separado"
    /// significa: o resultado de uma não pode depender da outra.
    ///
    /// A `CalculadoraIrrf` é função pura sobre a base que recebe — não há estado
    /// compartilhado, cache nem acumulador entre chamadas. Uma implementação que
    /// somasse rendimentos do mês precisaria de estado, e este teste falharia.
    /// </summary>
    [Fact]
    public void ApuracaoDeUmaFolhaNaoDependeDaOutra()
    {
        var tabela = Tabela();

        var sozinha = CalculadoraIrrf.Apurar(4_000m, 0m, 0, tabela).Valor;

        // A mesma base, apurada depois de outra apuracao "do mesmo mes".
        CalculadoraIrrf.Apurar(5_333.33m, 0m, 0, tabela);
        var depoisDeOutra = CalculadoraIrrf.Apurar(4_000m, 0m, 0, tabela).Valor;

        Assert.Equal(sozinha, depoisDeOutra);
    }

    /// <summary>
    /// A dedução por dependente vale nas **duas** apurações do mesmo mês.
    ///
    /// Art. 29 § 3º: *"Na determinação da base de cálculo podem ser efetuadas as
    /// deduções previstas no art. 52, desde que correspondentes às férias."*
    ///
    /// ⚠️ **Ponto declarado, não silencioso.** Fontes secundárias especializadas
    /// afirmam que a dedução é aplicada *"sem prejuízo dessa mesma dedução na
    /// determinação da base de cálculo do imposto incidente sobre os salários
    /// pagos no mesmo mês"*. Não encontrei essa frase em fonte primária — o
    /// texto oficial que tenho diz apenas "correspondentes às férias".
    ///
    /// O comportamento atual segue as fontes disponíveis. Mudá-lo seria decidir
    /// regra fiscal por interpretação, que o `CLAUDE.md §29` proíbe.
    /// </summary>
    [Fact]
    public void ADeducaoPorDependenteSeAplicaEmCadaApuracao()
    {
        var tabela = Tabela();

        // ⚠️ Com INSS na conta, de proposito. A calculadora escolhe entre as
        // DEDUCOES LEGAIS e o DESCONTO SIMPLIFICADO o que for mais favoravel -
        // e sem INSS, o simplificado (R$ 607,20) vence dois dependentes
        // (R$ 379,18), fazendo a deducao por dependente nao mudar nada.
        //
        // Isso nao e defeito: e o art. 29 § 5o, incluido pela IN RFB 2.141/2023,
        // que manda usar o simplificado "caso seja mais benefico ao
        // contribuinte". Foi este teste falhando que revelou o comportamento.
        const decimal Inss = 700m;

        var semDependente = CalculadoraIrrf.Apurar(6_000m, Inss, 0, tabela).Valor;
        var comDependente = CalculadoraIrrf.Apurar(6_000m, Inss, 2, tabela).Valor;

        Assert.True(
            comDependente < semDependente,
            $"sem={semDependente} com={comDependente}");
    }

    /// <summary>
    /// ⚠️ **Para quem ganha pouco, a pergunta toda é irrelevante.**
    ///
    /// O redutor da Lei 15.270/2025 zera o imposto de quem recebe até cerca de
    /// R$ 5.000. Nessa faixa, separar ou somar dá o mesmo resultado — zero —, e
    /// o "prejuízo" que a pendência apontava não existe.
    ///
    /// Este teste está aqui porque foi ele que fez os dois anteriores falharem
    /// na primeira execução: as bases escolhidas estavam abaixo do redutor, e
    /// o imposto era zero dos dois lados.
    /// </summary>
    [Fact]
    public void AbaixoDoRedutorAsDuasApuracoesDaoZero()
    {
        var tabela = Tabela();

        Assert.Equal(0m, CalculadoraIrrf.Apurar(3_000m, 0m, 0, tabela).Valor);
        Assert.Equal(0m, CalculadoraIrrf.Apurar(4_000m, 0m, 0, tabela).Valor);
        Assert.Equal(0m, CalculadoraIrrf.Apurar(4_000m, 0m, 2, tabela).Valor);
    }

    /// <summary>
    /// O tipo de folha existe e é distinto — é ele que permite as duas folhas
    /// conviverem na mesma competência, e por isso o IRRF sair separado.
    /// </summary>
    [Fact]
    public void MensalEFeriasSaoTiposDeFolhaDIFERENTES()
    {
        Assert.NotEqual(TipoFolha.Mensal, TipoFolha.Ferias);
    }
}
