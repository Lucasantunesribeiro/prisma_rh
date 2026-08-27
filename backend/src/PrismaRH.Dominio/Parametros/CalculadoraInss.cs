using System.Globalization;
using PrismaRH.Dominio.Folha;

namespace PrismaRH.Dominio.Parametros;

/// <summary>Quanto de INSS, e a conta que chegou nesse valor.</summary>
public sealed record ApuracaoInss(
    decimal BaseInss,
    decimal BaseLimitadaAoTeto,
    decimal SomaExata,
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
/// mundo sem que o holerite pare de fechar.
///
/// Funcao pura: sem banco, sem relogio, sem HTTP. O CLAUDE.md secao 10 exige
/// isso do motor, e e o que torna cada faixa testavel isoladamente.
///
/// Esta classe nao conhece nenhum numero legal. Faixas, aliquotas e teto vem
/// da TabelaInss, que por sua vez exige fonte oficial registrada. Trocar a
/// tabela de 2026 pela de 2027 e cadastrar uma vigencia nova - o algoritmo
/// aqui nao muda.
/// </summary>
public static class CalculadoraInss
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// ⚠️ PENDENCIA LEGAL REGISTRADA EM 27/08/2026 - CONFERIR ANTES DE PRODUCAO.
    ///
    /// Nenhuma fonte oficial alcancada declara em QUAL ETAPA a contribuicao do
    /// segurado e arredondada. Foram consultadas, sem sucesso: a pagina da
    /// tabela de contribuicao mensal do INSS (gov.br), que nao menciona
    /// arredondamento; a Portaria Interministerial MPS/MF 13/2026, que traz os
    /// valores mas nao o procedimento; e a Nota Orientativa eSocial 2018.008,
    /// que trata de casas decimais do LEIAUTE, nao do calculo.
    ///
    /// Enquanto nao houver fonte, adota-se o criterio ja registrado do projeto
    /// (CLAUDE.md secao 28, Fase 3): arredonda-se UMA vez, no valor final da
    /// rubrica, com MidpointRounding.AwayFromZero.
    ///
    /// A escolha importa. Na base do teto de 2026 (8.475,55) as parcelas
    /// exatas somam 988,0914, e o resultado publicado seria:
    ///     arredondar so no total ....... 988,09  (adotado)
    ///     arredondar cada faixa ........ 988,10
    ///     truncar cada faixa ........... 988,07
    ///
    /// PARA TROCAR: altere apenas este metodo. O teste
    /// Arredondamento_AplicadoUmaVezNoTotal trava a regra vigente e vai falhar
    /// de proposito, apontando o que precisa ser revisto.
    /// </summary>
    private static decimal ArredondarContribuicao(decimal somaExata) =>
        Dinheiro.Arredondar(somaExata);

    /// <summary>
    /// Aplica a tabela sobre a base e devolve a memoria completa: a base, cada
    /// faixa com o trecho usado e sua aliquota, e o total.
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

        passos.Add(new PassoCalculo(
            "Base de contribuicao",
            limitada < baseInss
                ? $"{Moeda(baseInss)} limitada ao teto de {Moeda(tabela.Teto)}"
                : Moeda(baseInss),
            limitada));

        var somaExata = 0m;
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
            somaExata += parcela;

            passos.Add(new PassoCalculo(
                $"Faixa {faixa.Ordem}: de {Moeda(piso)} a {Moeda(topo)}, aliquota {Percentual(faixa.AliquotaPercentual)}",
                // O valor EXATO vai na expressao porque a coluna de valor e
                // numeric(14,2) e engoliria as casas seguintes. Sem isto, quem
                // somasse as linhas nao chegaria ao total, e a memoria
                // deixaria de explicar o numero que ela mesma exibe.
                $"{Moeda(trecho)} x {Percentual(faixa.AliquotaPercentual)} = {Exato(parcela)}",
                Dinheiro.Arredondar(parcela)));

            piso = faixa.LimiteSuperior;
        }

        var valor = ArredondarContribuicao(somaExata);

        passos.Add(new PassoCalculo(
            "Total do INSS",
            somaExata == valor
                ? $"soma das faixas = {Moeda(valor)}"
                : $"soma exata {Exato(somaExata)} arredondada para {Moeda(valor)}",
            valor));

        return new ApuracaoInss(baseInss, limitada, somaExata, valor, tabela.Id, passos);
    }

    private static string Moeda(decimal valor) => valor.ToString("N2", Brasil);

    /// <summary>Ate quatro casas, sem zeros a direita: 988,0914 e 121,575.</summary>
    private static string Exato(decimal valor) => valor.ToString("0.####", Brasil);

    private static string Percentual(decimal percentual) =>
        percentual.ToString("0.##", Brasil) + "%";
}
