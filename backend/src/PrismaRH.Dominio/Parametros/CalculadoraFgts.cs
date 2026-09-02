using System.Globalization;
using PrismaRH.Dominio.Folha;

namespace PrismaRH.Dominio.Parametros;

/// <summary>Quanto de FGTS o empregador deposita, e a conta que chegou nesse valor.</summary>
public sealed record ApuracaoFgts(
    decimal BaseFgts,
    decimal Aliquota,
    decimal Valor,
    Guid IdTabela,
    IReadOnlyList<PassoCalculo> Passos);

/// <summary>
/// O deposito de FGTS do empregador.
///
/// NAO e desconto do funcionario: e obrigacao da empresa, e nao sai do
/// salario de ninguem. Por isso a rubrica e informativa e o valor nao entra no
/// liquido. Trata-lo como desconto tiraria 8% do salario de todo mundo, e o
/// holerite continuaria fechando - erro caro e silencioso.
///
/// Sem teto e sem faixas: incide linearmente sobre a base. Quem ganha acima do
/// teto do INSS continua tendo FGTS sobre a remuneracao inteira.
///
/// Funcao pura: sem banco, sem relogio, sem HTTP (CLAUDE.md secao 10). Nenhum
/// numero legal mora aqui - a aliquota vem de TabelaFgts, que exige fonte
/// oficial registrada.
/// </summary>
public static class CalculadoraFgts
{
    // ⚠️ Formato montado a mao, e nao `CultureInfo.GetCultureInfo("pt-BR")`.
    //
    // A Lambda roda em modo globalization-invariant (sem ICU), onde pedir uma
    // cultura por nome LANCA. Como isto era `static readonly`, a excecao subia
    // no primeiro toque na classe e derrubava o calculo inteiro. Ver
    // `FormatoBrasileiro`.
    private static readonly IFormatProvider Brasil = FormatoBrasileiro.Numero;

    /// <summary>
    /// Arredonda uma vez, no valor final da rubrica, com o mesmo criterio do
    /// resto do projeto (CLAUDE.md secao 28).
    ///
    /// Aqui nao ha a duvida que existe no INSS: o calculo tem uma etapa so,
    /// entao nao existe "arredondar por faixa" para escolher.
    /// </summary>
    public static ApuracaoFgts Apurar(decimal baseFgts, TabelaFgts tabela)
    {
        ArgumentNullException.ThrowIfNull(tabela);

        if (baseFgts < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(baseFgts), baseFgts, "Base de FGTS nao pode ser negativa.");
        }

        var exato = baseFgts * tabela.Aliquota;
        var valor = Dinheiro.Arredondar(exato);

        var passos = new List<PassoCalculo>
        {
            new("Base de cálculo do FGTS", Moeda(baseFgts), baseFgts),
            new(
                $"Deposito do empregador, aliquota {Percentual(tabela.AliquotaPercentual)}",
                exato == valor
                    ? $"{Moeda(baseFgts)} x {Percentual(tabela.AliquotaPercentual)}"
                    : $"{Moeda(baseFgts)} x {Percentual(tabela.AliquotaPercentual)} = {Exato(exato)}, arredondado",
                valor),
        };

        return new ApuracaoFgts(baseFgts, tabela.Aliquota, valor, tabela.Id, passos);
    }

    private static string Moeda(decimal valor) => valor.ToString("N2", Brasil);

    private static string Exato(decimal valor) => valor.ToString("0.####", Brasil);

    private static string Percentual(decimal percentual) => percentual.ToString("0.##", Brasil) + "%";
}

/// <summary>
/// O que a folha precisa para apurar FGTS: a rubrica informativa que recebe o
/// valor e a tabela que valia na competencia.
///
/// Nulo significa "esta organizacao ainda nao configurou FGTS" - a folha
/// calcula normalmente, sem a linha informativa.
/// </summary>
public sealed record ParametrosFgts(Rubrica Rubrica, TabelaFgts Tabela)
{
    public static ParametrosFgts? Montar(
        Rubrica? rubrica,
        IEnumerable<TabelaFgts> tabelas,
        Competencia competencia)
    {
        if (rubrica is null || !rubrica.Ativa)
        {
            return null;
        }

        if (rubrica.Estrategia != EstrategiaRubrica.FgtsMensal)
        {
            throw new ArgumentException(
                $"A rubrica {rubrica.Codigo} nao e a rubrica de FGTS.", nameof(rubrica));
        }

        var tabela = TabelaFgts.VigenteEm(tabelas, competencia.PrimeiroDia);

        return tabela is null ? null : new ParametrosFgts(rubrica, tabela);
    }
}
