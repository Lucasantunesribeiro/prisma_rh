using System.Globalization;
using PrismaRH.Dominio.Folha;

namespace PrismaRH.Dominio.Ferias;

/// <summary>Uma parcela do pagamento de ferias: qual rubrica, quanto, e por que.</summary>
public sealed record ParcelaFerias(
    EstrategiaRubrica Estrategia,
    decimal Valor,
    string Referencia,
    IReadOnlyList<PassoCalculo> Passos);

/// <summary>Tudo que uma concessao gera de pagamento.</summary>
public sealed record ApuracaoFerias(
    decimal SalarioReferencia,
    int DiasGozo,
    int DiasAbono,
    IReadOnlyList<ParcelaFerias> Parcelas)
{
    public decimal Total => Parcelas.Sum(p => p.Valor);
}

/// <summary>
/// O pagamento das ferias concedidas.
///
/// FONTES (CLAUDE.md secao 29):
///
/// - CLT art. 142: o empregado percebe, durante as ferias, a remuneracao que
///   lhe for devida na DATA DA CONCESSAO - por isso o salario usado e o da
///   vigencia que cobre o inicio do gozo, e nao o da competencia da folha;
/// - CF art. 7o, XVII: gozo de ferias anuais remuneradas com, pelo menos, UM
///   TERCO a mais do que o salario normal;
/// - CLT art. 143: conversao de ate um terco dos dias em abono pecuniario.
///
/// O DIVISOR E 30, e nao os dias do mes: ferias sao contadas em dias corridos
/// sobre o mes comercial, como o salario proporcional da Fase 3. Usar 31 em
/// marco e 28 em fevereiro faria o mesmo funcionario receber valores
/// diferentes pelos mesmos 30 dias de descanso.
///
/// Funcao pura: sem banco, sem relogio, sem HTTP (CLAUDE.md secao 10).
/// Nenhuma incidencia mora aqui - elas sao atributo da RUBRICA (Fase 4A), e
/// quem as declara e o catalogo da organizacao.
/// </summary>
public static class CalculadoraFerias
{
    // ⚠️ Formato montado a mao, e nao `CultureInfo.GetCultureInfo("pt-BR")`.
    //
    // A Lambda roda em modo globalization-invariant (sem ICU), onde pedir uma
    // cultura por nome LANCA. Como isto era `static readonly`, a excecao subia
    // no primeiro toque na classe e derrubava o calculo inteiro. Ver
    // `FormatoBrasileiro`.
    private static readonly IFormatProvider Brasil = FormatoBrasileiro.Numero;

    /// <summary>Dias do mes comercial. O mesmo divisor do salario proporcional.</summary>
    public const int Divisor = 30;

    public static ApuracaoFerias Apurar(decimal salarioReferencia, int diasGozo, int diasAbono)
    {
        if (salarioReferencia < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(salarioReferencia), salarioReferencia, "Salario nao pode ser negativo.");
        }

        if (diasGozo < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(diasGozo), diasGozo, "Dias nao podem ser negativos.");
        }

        if (diasAbono < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(diasAbono), diasAbono, "Dias nao podem ser negativos.");
        }

        var diario = salarioReferencia / Divisor;
        var parcelas = new List<ParcelaFerias>();

        if (diasGozo > 0)
        {
            var ferias = Dinheiro.Arredondar(diario * diasGozo);

            parcelas.Add(new ParcelaFerias(
                EstrategiaRubrica.FeriasGozadas,
                ferias,
                $"{diasGozo}/{Divisor}",
                [
                    new("Salario na data da concessao", Moeda(salarioReferencia), salarioReferencia),
                    new(
                        $"Remuneracao de {diasGozo} dias de ferias",
                        $"{Moeda(salarioReferencia)} / {Divisor} x {diasGozo}",
                        ferias),
                ]));

            // Um terco SOBRE A REMUNERACAO ARREDONDADA, e nao sobre o valor
            // exato: e esse o numero que aparece no holerite e que a pessoa
            // confere. Calcular o terco sobre um valor que ninguem ve tornaria
            // a memoria de calculo impossivel de refazer a mao.
            var terco = Dinheiro.Arredondar(ferias / 3m);

            parcelas.Add(new ParcelaFerias(
                EstrategiaRubrica.TercoFerias,
                terco,
                "1/3",
                [
                    new("Remuneracao das ferias", Moeda(ferias), ferias),
                    new("Um terco constitucional", $"{Moeda(ferias)} / 3", terco),
                ]));
        }

        if (diasAbono > 0)
        {
            var abono = Dinheiro.Arredondar(diario * diasAbono);

            parcelas.Add(new ParcelaFerias(
                EstrategiaRubrica.AbonoPecuniario,
                abono,
                $"{diasAbono}/{Divisor}",
                [
                    new("Salario na data da concessao", Moeda(salarioReferencia), salarioReferencia),
                    new(
                        $"Abono pecuniario de {diasAbono} dias",
                        $"{Moeda(salarioReferencia)} / {Divisor} x {diasAbono}",
                        abono),
                ]));

            var tercoAbono = Dinheiro.Arredondar(abono / 3m);

            parcelas.Add(new ParcelaFerias(
                EstrategiaRubrica.TercoAbono,
                tercoAbono,
                "1/3",
                [
                    new("Abono pecuniario", Moeda(abono), abono),
                    new("Um terco sobre o abono", $"{Moeda(abono)} / 3", tercoAbono),
                ]));
        }

        return new ApuracaoFerias(salarioReferencia, diasGozo, diasAbono, parcelas);
    }

    private static string Moeda(decimal valor) => valor.ToString("N2", Brasil);
}
