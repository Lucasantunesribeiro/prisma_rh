using System.Globalization;

using PrismaRH.Dominio.Contratos;

namespace PrismaRH.Dominio.Folha;

/// <summary>Um passo da memoria de calculo, antes de virar linha persistida.</summary>
public sealed record PassoCalculo(string Descricao, string Expressao, decimal Valor);

/// <summary>O que o motor apurou de salario-base para um contrato numa competencia.</summary>
public sealed record ApuracaoSalarioBase(
    int Avos,
    int Divisor,
    decimal SalarioReferencia,
    Guid IdVigenciaReferencia,
    decimal Valor,
    IReadOnlyList<PassoCalculo> Passos);

/// <summary>
/// O calculo do salario-base proporcional. Funcoes puras: entram objetos de
/// dominio, sai um numero e a explicacao dele.
///
/// Nao acessa banco, nao conhece HTTP e nao depende de relogio - o CLAUDE.md
/// secao 10 exige um motor deterministico e testavel, e nada disso e testavel
/// se o resultado depender de quando a funcao rodou.
/// </summary>
public static class MotorCalculoFolha
{
    /// <summary>
    /// O mes da folha vale 30 avos, tenha 28, 30 ou 31 dias.
    ///
    /// Nao e uma escolha estetica: a CLT artigo 64 fixa o divisor 30 para o
    /// salario mensal. E por isso que quem trabalha fevereiro inteiro recebe o
    /// salario cheio, e quem trabalha agosto inteiro tambem - o dia 31 nao
    /// gera um trigesimo primeiro avo.
    /// </summary>
    public const int DivisorMensal = 30;

    /// <summary>
    /// A memoria de calculo e lida por gente, em portugues. Fixar a cultura
    /// aqui evita que o mesmo calculo escreva "3.900,00" na maquina do
    /// desenvolvedor e "3,900.00" no servidor - a folha ficaria correta e a
    /// explicacao dela, ilegivel.
    /// </summary>
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// O trecho da competencia em que o contrato esteve vigente, ou nulo se
    /// nao houve nenhum.
    ///
    /// E aqui que mora a regra de elegibilidade aprovada: entra na folha quem
    /// teve vinculo em QUALQUER dia do mes. Admitido dia 20 entra; desligado
    /// dia 10 tambem, porque trabalhou dez dias e esses dias sao devidos.
    /// </summary>
    public static (DateOnly Inicio, DateOnly Fim)? PeriodoNaCompetencia(
        ContratoTrabalho contrato, Competencia competencia)
    {
        ArgumentNullException.ThrowIfNull(contrato);

        return PeriodoNaCompetencia(
            contrato.DataAdmissao, contrato.DataDesligamento, competencia);
    }

    /// <summary>
    /// A mesma conta, com o fim do vinculo informado por fora.
    ///
    /// Existe por causa da PROJECAO do aviso previo indenizado: a CLT art. 487
    /// par. 1o manda contar o aviso como tempo de servico, entao os avos vao
    /// ate uma data POSTERIOR ao desligamento gravado no contrato. Sem esta
    /// sobrecarga, o calculo pararia na data de saida e a pessoa perderia o
    /// avo que a lei lhe da.
    ///
    /// Sobrecarga, e nao copia da formula: duas implementacoes da mesma
    /// pergunta acabariam divergindo.
    /// </summary>
    public static (DateOnly Inicio, DateOnly Fim)? PeriodoNaCompetencia(
        DateOnly admissao, DateOnly? fimDoVinculo, Competencia competencia)
    {
        var inicio = admissao > competencia.PrimeiroDia ? admissao : competencia.PrimeiroDia;

        var fim = fimDoVinculo is { } saida && saida < competencia.UltimoDia
            ? saida
            : competencia.UltimoDia;

        return inicio > fim ? null : (inicio, fim);
    }

    public static bool Elegivel(ContratoTrabalho contrato, Competencia competencia) =>
        PeriodoNaCompetencia(contrato, competencia) is not null;

    /// <summary>
    /// Quantos avos um trecho da competencia vale.
    ///
    /// Mes inteiro sao 30 avos, sempre. Trecho parcial conta os dias corridos
    /// pelo numero do dia, ignorando o dia 31 - que no mes comercial nao
    /// existe. Um vinculo de um dia unico vale no minimo 1 avo, senao quem
    /// fosse admitido em 31 de agosto trabalharia de graca.
    /// </summary>
    public static int AvosDoPeriodo(DateOnly inicio, DateOnly fim, Competencia competencia)
    {
        if (inicio > fim)
        {
            return 0;
        }

        if (inicio == competencia.PrimeiroDia && fim == competencia.UltimoDia)
        {
            return DivisorMensal;
        }

        var primeiro = Math.Min(inicio.Day, DivisorMensal);
        var ultimo = Math.Min(fim.Day, DivisorMensal);

        return Math.Max(1, ultimo - primeiro + 1);
    }

    /// <summary>
    /// Apura o salario-base do contrato na competencia, repartido por vigencia.
    ///
    /// Um aumento no dia 15 nao pode pagar o mes inteiro pelo salario novo nem
    /// pelo antigo: cada vigencia recebe os avos que lhe cabem. E o que impede
    /// o reajuste de reescrever, na pratica, a primeira metade do mes.
    ///
    /// Devolve nulo quando o contrato nao teve vinculo na competencia.
    /// </summary>
    public static ApuracaoSalarioBase? Apurar(ContratoTrabalho contrato, Competencia competencia)
    {
        ArgumentNullException.ThrowIfNull(contrato);

        if (PeriodoNaCompetencia(contrato, competencia) is not { } periodo)
        {
            return null;
        }

        var (inicio, fim) = periodo;

        var avosTotais = AvosDoPeriodo(inicio, fim, competencia);

        var trechos = contrato.Vigencias
            .Where(v => v.ValidoDe <= fim && (v.ValidoAte is null || v.ValidoAte.Value >= inicio))
            .OrderBy(v => v.ValidoDe)
            .Select(v => new
            {
                Vigencia = v,
                Inicio = v.ValidoDe > inicio ? v.ValidoDe : inicio,
                Fim = v.ValidoAte is { } ate && ate < fim ? ate : fim,
            })
            .Select(t => new { t.Vigencia, t.Inicio, t.Fim, Avos = AvosDoPeriodo(t.Inicio, t.Fim, competencia) })
            .ToList();

        if (trechos.Count == 0)
        {
            // O agregado cria a vigencia junto com o contrato, entao chegar
            // aqui significa que o contrato foi carregado sem as vigencias.
            // Calcular zero em silencio esconderia o erro dentro de uma folha
            // com valor plausivel.
            throw new InvalidOperationException(
                $"Contrato {contrato.Matricula} nao tem vigencia cobrindo {competencia}. "
                + "As vigencias foram carregadas junto com o contrato?");
        }

        // Somar os avos de cada trecho nem sempre da o total do mes: em
        // fevereiro, dois trechos de 14 dias somam 28, e o mes vale 30. A
        // diferenca vai para o ultimo trecho, que e o salario vigente no fim
        // do periodo - o mesmo criterio que a folha usa para o mes cheio.
        var avos = trechos.Select(t => t.Avos).ToArray();
        avos[^1] += avosTotais - avos.Sum();

        var passos = new List<PassoCalculo>();
        var total = 0m;

        for (var i = 0; i < trechos.Count; i++)
        {
            var trecho = trechos[i];
            var parcela = Dinheiro.Arredondar(trecho.Vigencia.Salario * avos[i] / DivisorMensal);
            total += parcela;

            passos.Add(new PassoCalculo(
                trechos.Count == 1
                    ? $"Salario vigente desde {trecho.Vigencia.ValidoDe:dd/MM/yyyy}"
                    : $"Vigencia de {trecho.Inicio:dd/MM} a {trecho.Fim:dd/MM}",
                string.Create(Brasil, $"{trecho.Vigencia.Salario:N2} x {avos[i]}/{DivisorMensal}"),
                parcela));
        }

        if (trechos.Count > 1)
        {
            passos.Add(new PassoCalculo(
                "Soma das vigencias do mes",
                string.Join(" + ", passos.Select(p => p.Valor.ToString("N2", Brasil))),
                total));
        }

        var referencia = trechos[^1].Vigencia;

        return new ApuracaoSalarioBase(
            avosTotais, DivisorMensal, referencia.Salario, referencia.Id, total, passos);
    }
}
