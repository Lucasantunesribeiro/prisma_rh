using System.Globalization;
using PrismaRH.Dominio.Folha;

namespace PrismaRH.Dominio.DecimoTerceiro;

/// <summary>Uma parcela do 13o: qual rubrica, quanto, e por que.</summary>
public sealed record ParcelaDecimoTerceiro(
    EstrategiaRubrica Estrategia,
    decimal Valor,
    string Referencia,
    IReadOnlyList<PassoCalculo> Passos);

/// <summary>O que o adiantamento do 13o gera de pagamento.</summary>
public sealed record ApuracaoAdiantamento(
    int Ano,
    int Avos,
    decimal SalarioReferencia,
    decimal Valor,
    IReadOnlyList<ParcelaDecimoTerceiro> Parcelas);

/// <summary>
/// O que a folha ANUAL do 13o gera: o total devido, o desconto do adiantamento
/// e a base de FGTS que ainda nao foi tributada.
/// </summary>
public sealed record ApuracaoAnualDecimoTerceiro(
    int Ano,
    int Avos,
    decimal SalarioReferencia,
    decimal Total,
    decimal AdiantamentoJaPago,
    decimal BaseFgtsRestante,
    IReadOnlyList<ParcelaDecimoTerceiro> Parcelas);

/// <summary>
/// O calculo do 13o salario, nas duas parcelas.
///
/// FONTES (CLAUDE.md secao 29):
///
/// - **Lei n. 4.090/1962**, art. 1o: a gratificacao corresponde a 1/12 da
///   remuneracao devida em dezembro, por mes de servico;
/// - **Lei n. 4.749/1965**, art. 1o: pagamento ate 20 de dezembro, compensado
///   o adiantamento; art. 2o: adiantamento entre fevereiro e novembro;
/// - **MOS eSocial S-1.3**, consolidado ate a NO S-1.3 - 10.2026, itens 10.3.4
///   e 10.3.4.1, e item 19 das "Informacoes adicionais" do evento S-1200.
///
/// ## As incidencias, e por que elas nao moram aqui
///
/// O MOS S-1.3, item 10.3.4, diz textualmente:
///
/// > "A apuracao da CP e do IRRF incidentes sobre o 13o salario e feita apenas
/// > na folha de 13o (anual)."
///
/// > "o FGTS, ao contrario da CP e do IRRF, incide sobre a parcela do
/// > adiantamento do 13o salario no mes em que for paga. (...) Assim, o FGTS
/// > incidente sobre a folha do 13o salario e calculado apenas sobre a
/// > diferenca entre o valor da gratificacao natalina e a primeira parcela."
///
/// Disso saem TRES bases diferentes num mesmo holerite anual:
///
/// <code>
/// INSS  -> sobre o TOTAL do 13o
/// IRRF  -> sobre o TOTAL do 13o
/// FGTS  -> apenas sobre o TOTAL MENOS O ADIANTAMENTO
/// </code>
///
/// Incidencia e atributo da RUBRICA (Fase 4A), e uma rubrica tem UMA
/// declaracao. Por isso a folha anual usa tres rubricas e nao uma:
///
/// - o **total**, provento, declarando INSS e IRRF - e nao FGTS;
/// - o **adiantamento ja pago**, desconto, sem incidencia alguma (a invariante
///   da 4A recusa desconto com incidencia, e esta certa: desconto nao reduz
///   base);
/// - a **diferenca**, INFORMATIVA, declarando FGTS. Informativa nao entra no
///   liquido mas COMPOE base - e exatamente para isso que o tipo existe.
///
/// Nenhuma incidencia esta escrita neste arquivo. Quem as declara e o catalogo
/// da organizacao.
///
/// Funcao pura: sem banco, sem relogio, sem HTTP (CLAUDE.md secao 10).
/// </summary>
public static class CalculadoraDecimoTerceiro
{
    // ⚠️ Formato montado a mao, e nao `CultureInfo.GetCultureInfo("pt-BR")`.
    //
    // A Lambda roda em modo globalization-invariant (sem ICU), onde pedir uma
    // cultura por nome LANCA. Como isto era `static readonly`, a excecao subia
    // no primeiro toque na classe e derrubava o calculo inteiro. Ver
    // `FormatoBrasileiro`.
    private static readonly IFormatProvider Brasil = FormatoBrasileiro.Numero;

    /// <summary>Os doze avos. Mesmo divisor de <see cref="AvosDecimoTerceiro"/>.</summary>
    public const int Divisor = AvosDecimoTerceiro.MesesDoAno;

    /// <summary>
    /// A fracao do 13o paga como adiantamento (Lei 4.749/1965, art. 2o).
    ///
    /// A lei diz "metade do salario recebido pelo empregado no mes anterior".
    /// Aqui a metade incide sobre o **13o ja proporcionalizado pelos avos**, e
    /// nao sobre o salario cheio - ver a nota em <see cref="ApurarAdiantamento"/>.
    /// </summary>
    public const decimal FracaoDoAdiantamento = 0.5m;

    /// <summary>
    /// O adiantamento, pago entre fevereiro e novembro.
    ///
    /// **Incide FGTS, e so ele** - MOS S-1.3, item 10.3.4. INSS e IRRF ficam
    /// integralmente para a folha anual.
    ///
    /// ## Por que a metade incide sobre o 13o proporcional, e nao sobre o
    /// salario cheio
    ///
    /// A Lei 4.749/1965 art. 2o manda pagar "metade do salario recebido pelo
    /// empregado no mes anterior". Lida ao pe da letra, quem foi admitido em
    /// outubro receberia meio salario de adiantamento tendo direito a 2/12 do
    /// 13o - um adiantamento MAIOR que a gratificacao inteira.
    ///
    /// Isso nao e ilegal: o MOS S-1.3, item 10.3.4.1, reconhece a hipotese ao
    /// dizer que, quando se paga mais do que o devido, "o que ocorre nesses
    /// casos nao e o pagamento integral e sim um adiantamento superior ao valor
    /// devido". Mas o resultado seria um liquido NEGATIVO na folha anual, e um
    /// produto nao deve ter isso como comportamento padrao.
    ///
    /// Entao o padrao aqui e o conservador: metade do 13o **devido ate o mes do
    /// adiantamento**. As duas leis nao se contradizem - a 4.090 define quanto
    /// se deve, a 4.749 define quando e quanto se antecipa -, e a
    /// proporcionalizacao apenas impede que a antecipacao ultrapasse o direito.
    /// </summary>
    /// <param name="avos">Avos apurados ate o mes do adiantamento.</param>
    public static ApuracaoAdiantamento ApurarAdiantamento(
        int ano, int avos, decimal salarioReferencia)
    {
        Validar(avos, salarioReferencia);

        var devido = Dinheiro.Arredondar(salarioReferencia * avos / Divisor);
        var valor = Dinheiro.Arredondar(devido * FracaoDoAdiantamento);

        var parcelas = valor <= 0m
            ? (IReadOnlyList<ParcelaDecimoTerceiro>)[]
            : [
                new ParcelaDecimoTerceiro(
                    EstrategiaRubrica.DecimoTerceiroAdiantamento,
                    valor,
                    $"{avos}/{Divisor}",
                    [
                        new("Salario de referencia", Moeda(salarioReferencia), salarioReferencia),
                        new(
                            $"13o proporcional a {avos} avos",
                            $"{Moeda(salarioReferencia)} / {Divisor} x {avos}",
                            devido),
                        new("Metade, como adiantamento", $"{Moeda(devido)} / 2", valor),
                    ]),
            ];

        return new ApuracaoAdiantamento(ano, avos, salarioReferencia, valor, parcelas);
    }

    /// <summary>
    /// A folha ANUAL do 13o, em dezembro.
    ///
    /// Reproduz o roteiro do MOS S-1.3, item 10.3.4.1: "lancar como vencimento
    /// o valor total do 13o devido (...) e como descontos: o valor do
    /// adiantamento do 13o pago (...) e o valor da contribuicao previdenciaria".
    ///
    /// O INSS e o IRRF nao sao lancados aqui: eles saem das rubricas de encargo
    /// da propria folha, sobre a base que estas parcelas compuserem. E a mesma
    /// mecanica da folha mensal, e por isso o 13o nao precisa de calculadora de
    /// imposto propria.
    ///
    /// <paramref name="adiantamentoJaPago"/> vem das folhas de adiantamento do
    /// MESMO ano - estado derivado, nao um campo que alguem digita.
    /// </summary>
    public static ApuracaoAnualDecimoTerceiro ApurarAnual(
        int ano, int avos, decimal salarioReferencia, decimal adiantamentoJaPago)
    {
        Validar(avos, salarioReferencia);

        if (adiantamentoJaPago < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(adiantamentoJaPago), adiantamentoJaPago,
                "Adiantamento ja pago nao pode ser negativo.");
        }

        var total = Dinheiro.Arredondar(salarioReferencia * avos / Divisor);

        // A base de FGTS que sobra. Nunca negativa: se o adiantamento superou o
        // total - hipotese que o MOS 10.3.4.1 admite -, o FGTS daquele excesso
        // ja foi recolhido na competencia do adiantamento, e nada resta aqui.
        // Uma base negativa devolveria FGTS, o que o Fundo nao faz.
        var restante = Math.Max(0m, total - adiantamentoJaPago);

        var parcelas = new List<ParcelaDecimoTerceiro>();

        if (total > 0m)
        {
            parcelas.Add(new ParcelaDecimoTerceiro(
                EstrategiaRubrica.DecimoTerceiroTotal,
                total,
                $"{avos}/{Divisor}",
                [
                    new("Salario de dezembro", Moeda(salarioReferencia), salarioReferencia),
                    new(
                        $"13o integral a {avos} avos",
                        $"{Moeda(salarioReferencia)} / {Divisor} x {avos}",
                        total),
                ]));
        }

        if (adiantamentoJaPago > 0m)
        {
            parcelas.Add(new ParcelaDecimoTerceiro(
                EstrategiaRubrica.DecimoTerceiroAdiantamentoDescontado,
                adiantamentoJaPago,
                "1a parcela",
                [
                    new("13o devido no ano", Moeda(total), total),
                    new(
                        "Adiantamento ja pago, a compensar",
                        Moeda(adiantamentoJaPago),
                        adiantamentoJaPago),
                ]));
        }

        if (restante > 0m)
        {
            parcelas.Add(new ParcelaDecimoTerceiro(
                EstrategiaRubrica.DecimoTerceiroBaseFgts,
                restante,
                "diferenca",
                [
                    new("13o devido no ano", Moeda(total), total),
                    new("Adiantamento ja tributado pelo FGTS", Moeda(adiantamentoJaPago), adiantamentoJaPago),
                    new(
                        "Base de FGTS ainda nao tributada",
                        $"{Moeda(total)} - {Moeda(adiantamentoJaPago)}",
                        restante),
                ]));
        }

        return new ApuracaoAnualDecimoTerceiro(
            ano, avos, salarioReferencia, total, adiantamentoJaPago, restante, parcelas);
    }

    private static void Validar(int avos, decimal salarioReferencia)
    {
        if (avos < 0 || avos > Divisor)
        {
            throw new ArgumentOutOfRangeException(
                nameof(avos), avos, $"Avos precisam estar entre 0 e {Divisor}.");
        }

        if (salarioReferencia < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(salarioReferencia), salarioReferencia, "Salario nao pode ser negativo.");
        }
    }

    private static string Moeda(decimal valor) => valor.ToString("N2", Brasil);
}
