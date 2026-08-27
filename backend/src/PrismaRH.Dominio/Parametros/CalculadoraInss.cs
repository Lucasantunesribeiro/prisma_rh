using System.Globalization;
using PrismaRH.Dominio.Folha;

namespace PrismaRH.Dominio.Parametros;

/// <summary>Quanto de INSS, e a conta que chegou nesse valor.</summary>
public sealed record ApuracaoInss(
    decimal BaseInss,
    decimal BaseLimitadaAoTeto,
    decimal Valor,
    Guid IdTabela,
    IReadOnlyList<PassoCalculo> Passos);

/// <summary>
/// O desconto de INSS do segurado, faixa a faixa.
///
/// Progressivo, e nao "aliquota unica sobre o total": cada trecho da base paga
/// a aliquota da sua faixa. Quem ganha acima do teto nao paga 14% sobre tudo -
/// paga 7,5% sobre o primeiro trecho, 9% sobre o seguinte, e assim por diante.
/// Aplicar a aliquota da ultima faixa sobre a base inteira e o erro mais comum
/// de quem implementa INSS pela primeira vez, e ele desconta a mais de todo
/// mundo.
///
/// Funcao pura: sem banco, sem relogio, sem HTTP. O CLAUDE.md secao 10 exige
/// isso do motor, e e o que torna cada faixa testavel isoladamente.
///
/// Esta classe nao conhece nenhum numero legal. Faixas, aliquotas e teto vem
/// da TabelaInss, que por sua vez exige fonte oficial registrada.
/// </summary>
public static class CalculadoraInss
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// Aplica a tabela sobre a base.
    ///
    /// O arredondamento acontece UMA vez, no valor final da rubrica, e nao a
    /// cada faixa. Arredondar faixa a faixa acumularia ate quatro
    /// arredondamentos num desconto so, e o resultado passaria a depender de
    /// quantas faixas a tabela tem naquele ano. E a mesma regra da Fase 3
    /// (CLAUDE.md secao 28): arredonda-se o valor final de cada rubrica.
    /// </summary>
    public static ApuracaoInss Apurar(decimal baseInss, TabelaInss tabela)
    {
        ArgumentNullException.ThrowIfNull(tabela);

        if (baseInss < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(baseInss), baseInss, "Base de INSS nao pode ser negativa.");
        }

        var passos = new List<PassoCalculo>();
        var limitada = Math.Min(baseInss, tabela.Teto);

        if (limitada < baseInss)
        {
            passos.Add(new PassoCalculo(
                "Base limitada ao teto do salario-de-contribuicao",
                $"{Moeda(baseInss)} -> {Moeda(tabela.Teto)}",
                limitada));
        }

        var total = 0m;
        var piso = 0m;

        foreach (var faixa in tabela.Faixas)
        {
            var topo = Math.Min(limitada, faixa.LimiteSuperior);
            var trecho = topo - piso;

            if (trecho <= 0)
            {
                // A base nao alcancou esta faixa. As proximas tambem nao,
                // porque os limites sao crescentes.
                break;
            }

            var parcela = trecho * faixa.Aliquota;
            total += parcela;

            passos.Add(new PassoCalculo(
                $"Faixa {faixa.Ordem}: de {Moeda(piso)} ate {Moeda(topo)}",
                $"{Moeda(trecho)} x {Percentual(faixa.AliquotaPercentual)}",
                Dinheiro.Arredondar(parcela)));

            piso = faixa.LimiteSuperior;
        }

        var valor = Dinheiro.Arredondar(total);

        if (passos.Count > 1)
        {
            passos.Add(new PassoCalculo("Total do INSS", "soma das faixas", valor));
        }

        return new ApuracaoInss(baseInss, limitada, valor, tabela.Id, passos);
    }

    private static string Moeda(decimal valor) => valor.ToString("N2", Brasil);

    private static string Percentual(decimal percentual) =>
        percentual.ToString("0.##", Brasil) + "%";
}
