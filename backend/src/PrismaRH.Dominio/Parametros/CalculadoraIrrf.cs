using System.Globalization;
using PrismaRH.Dominio.Folha;

namespace PrismaRH.Dominio.Parametros;

/// <summary>Como o desconto foi apurado: pelas deducoes legais ou pelo simplificado.</summary>
public enum FormaDeducaoIrrf
{
    Legal = 1,
    Simplificada = 2
}

/// <summary>O IRRF apurado e tudo que explica o numero.</summary>
public sealed record ApuracaoIrrf(
    decimal RendimentosTributaveis,
    decimal DeducaoInss,
    int QuantidadeDependentes,
    decimal DeducaoDependentes,
    FormaDeducaoIrrf Forma,
    decimal BaseCalculo,
    decimal ImpostoDaTabela,
    decimal Redutor,
    decimal Valor,
    Guid IdTabela,
    IReadOnlyList<PassoCalculo> Passos);

/// <summary>
/// O imposto de renda retido na fonte, mensal.
///
/// Tres coisas o separam do INSS, e cada uma ja causou erro em sistema de
/// folha:
///
/// 1. NAO e soma trecho a trecho. Aplica-se UMA aliquota - a da faixa onde a
///    base caiu - sobre a base inteira, e desconta-se a parcela a deduzir.
/// 2. A base NAO e a remuneracao: e a remuneracao menos as deducoes. E ha
///    duas formas de deduzir, que nao se somam - vale a mais vantajosa.
/// 3. Existe REDUTOR (Lei 15.270/2025), aplicado sobre o imposto ja apurado e
///    calculado a partir dos rendimentos BRUTOS, nao da base.
///
/// Funcao pura: sem banco, sem relogio, sem HTTP (CLAUDE.md secao 10). Nenhum
/// numero legal mora aqui - todos vem de TabelaIrrf, que exige fonte oficial.
/// </summary>
public static class CalculadoraIrrf
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// Apura o IRRF do mes.
    ///
    /// ORDEM, conforme os exemplos oficiais da Receita Federal para a Lei
    /// 15.270/2025 (os cinco estao reproduzidos nos testes):
    ///
    ///   base legal        = rendimentos - INSS - (dependentes x deducao)
    ///   base simplificada = rendimentos - desconto simplificado
    ///   base              = a MENOR das duas
    ///   imposto           = base x aliquota da faixa - parcela a deduzir
    ///   redutor           = base do redutor - coeficiente x RENDIMENTOS
    ///   IRRF              = imposto - redutor, nunca abaixo de zero
    ///
    /// Dois detalhes que os exemplos oficiais deixam explicitos e que seriam
    /// facilmente errados:
    ///
    /// - o redutor incide sobre os RENDIMENTOS BRUTOS, nao sobre a base;
    /// - o redutor e LIMITADO ao imposto apurado. Ele zera o imposto, nunca
    ///   gera restituicao.
    ///
    /// ARREDONDAMENTO: uma vez, no valor final, pelo criterio do projeto
    /// (CLAUDE.md secao 28). Os cinco exemplos oficiais sao reproduzidos
    /// exatamente assim - ver a nota na Fase 4D do ROADMAP.
    /// </summary>
    public static ApuracaoIrrf Apurar(
        decimal rendimentosTributaveis,
        decimal deducaoInss,
        int quantidadeDependentes,
        TabelaIrrf tabela)
    {
        ArgumentNullException.ThrowIfNull(tabela);

        if (rendimentosTributaveis < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rendimentosTributaveis), rendimentosTributaveis,
                "Rendimentos tributaveis nao podem ser negativos.");
        }

        if (deducaoInss < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deducaoInss), deducaoInss, "Deducao de INSS nao pode ser negativa.");
        }

        if (quantidadeDependentes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantidadeDependentes), quantidadeDependentes,
                "Quantidade de dependentes nao pode ser negativa.");
        }

        var passos = new List<PassoCalculo>
        {
            new("Rendimentos tributaveis", Moeda(rendimentosTributaveis), rendimentosTributaveis),
        };

        var deducaoDependentes = quantidadeDependentes * tabela.DeducaoPorDependente;
        var deducoesLegais = deducaoInss + deducaoDependentes;

        var baseLegal = Math.Max(0m, rendimentosTributaveis - deducoesLegais);
        var baseSimplificada = Math.Max(0m, rendimentosTributaveis - tabela.DescontoSimplificado);

        // O contribuinte usa a forma mais vantajosa, e mais vantajosa e a que
        // resulta na MENOR base. Empate fica com a legal: ela e a regra, e a
        // simplificada e a alternativa.
        var simplificadaVence = baseSimplificada < baseLegal;

        var forma = simplificadaVence ? FormaDeducaoIrrf.Simplificada : FormaDeducaoIrrf.Legal;
        var baseCalculo = simplificadaVence ? baseSimplificada : baseLegal;

        if (simplificadaVence)
        {
            passos.Add(new(
                "Desconto simplificado, que substitui as deducoes legais",
                $"{Moeda(rendimentosTributaveis)} - {Moeda(tabela.DescontoSimplificado)}",
                baseSimplificada));
        }
        else
        {
            passos.Add(new("Deducao do INSS", $"- {Moeda(deducaoInss)}", deducaoInss));

            if (quantidadeDependentes > 0)
            {
                passos.Add(new(
                    $"Deducao por {quantidadeDependentes} dependente(s)",
                    $"{quantidadeDependentes} x {Moeda(tabela.DeducaoPorDependente)}",
                    deducaoDependentes));
            }
        }

        passos.Add(new("Base de calculo do IRRF", Moeda(baseCalculo), baseCalculo));

        var faixa = FaixaDe(baseCalculo, tabela);
        var impostoExato = 0m;

        if (faixa is null || faixa.Aliquota == 0)
        {
            passos.Add(new(
                "Base dentro da faixa de isencao",
                $"{Moeda(baseCalculo)} ate {Moeda(tabela.LimiteIsencao)}",
                0m));
        }
        else
        {
            // Nunca negativo: a parcela a deduzir e calibrada para a faixa, e
            // uma base no piso dela produz zero, nao imposto a favor.
            impostoExato = Math.Max(0m, (baseCalculo * faixa.Aliquota) - faixa.ParcelaADeduzir);

            passos.Add(new(
                $"Aliquota de {Percentual(faixa.AliquotaPercentual)} menos a parcela a deduzir",
                $"{Moeda(baseCalculo)} x {Percentual(faixa.AliquotaPercentual)} - {Moeda(faixa.ParcelaADeduzir)}",
                Dinheiro.Arredondar(impostoExato)));
        }

        var redutorExato = 0m;

        if (tabela.TemRedutor && impostoExato > 0)
        {
            var bruto = tabela.RedutorBase - (tabela.RedutorCoeficiente * rendimentosTributaveis);

            // Duas travas, ambas dos exemplos oficiais: o redutor nao e
            // negativo (acima do limite ele simplesmente nao existe) e nao
            // passa do imposto (ele zera, nao restitui).
            redutorExato = Math.Clamp(bruto, 0m, impostoExato);

            if (redutorExato > 0)
            {
                passos.Add(new(
                    "Redutor do imposto",
                    bruto > impostoExato
                        ? $"{Moeda(tabela.RedutorBase)} - {Coeficiente(tabela.RedutorCoeficiente)} x {Moeda(rendimentosTributaveis)} = {Exato(bruto)}, limitado ao imposto"
                        : $"{Moeda(tabela.RedutorBase)} - {Coeficiente(tabela.RedutorCoeficiente)} x {Moeda(rendimentosTributaveis)}",
                    Dinheiro.Arredondar(redutorExato)));
            }
        }

        var valor = Dinheiro.Arredondar(impostoExato - redutorExato);

        passos.Add(new(
            "Total do IRRF",
            redutorExato > 0
                ? $"{Exato(impostoExato)} - {Exato(redutorExato)}"
                : Exato(impostoExato),
            valor));

        return new ApuracaoIrrf(
            rendimentosTributaveis,
            deducaoInss,
            quantidadeDependentes,
            deducaoDependentes,
            forma,
            baseCalculo,
            Dinheiro.Arredondar(impostoExato),
            Dinheiro.Arredondar(redutorExato),
            valor,
            tabela.Id,
            passos);
    }

    /// <summary>
    /// A faixa em que a base caiu. A comparacao e <c>&lt;=</c> porque o limite
    /// PERTENCE a faixa: uma base de exatamente 2.428,80 e isenta.
    /// </summary>
    private static FaixaIrrf? FaixaDe(decimal baseCalculo, TabelaIrrf tabela) =>
        tabela.Faixas.FirstOrDefault(f => f.Alcanca(baseCalculo));

    private static string Moeda(decimal valor) => valor.ToString("N2", Brasil);

    private static string Exato(decimal valor) => valor.ToString("0.####", Brasil);

    private static string Percentual(decimal percentual) => percentual.ToString("0.##", Brasil) + "%";

    private static string Coeficiente(decimal valor) => valor.ToString("0.######", Brasil);
}

/// <summary>
/// O que a folha precisa para apurar IRRF: a rubrica de desconto e a tabela
/// que valia na competencia.
///
/// Nulo significa "esta organizacao ainda nao configurou IRRF" - a folha
/// calcula sem o desconto.
/// </summary>
public sealed record ParametrosIrrf(Rubrica Rubrica, TabelaIrrf Tabela)
{
    public static ParametrosIrrf? Montar(
        Rubrica? rubrica,
        IEnumerable<TabelaIrrf> tabelas,
        Competencia competencia)
    {
        if (rubrica is null || !rubrica.Ativa)
        {
            return null;
        }

        if (rubrica.Estrategia != EstrategiaRubrica.IrrfMensal)
        {
            throw new ArgumentException(
                $"A rubrica {rubrica.Codigo} nao e a rubrica de IRRF.", nameof(rubrica));
        }

        var tabela = TabelaIrrf.VigenteEm(tabelas, competencia.PrimeiroDia);

        return tabela is null ? null : new ParametrosIrrf(rubrica, tabela);
    }
}
