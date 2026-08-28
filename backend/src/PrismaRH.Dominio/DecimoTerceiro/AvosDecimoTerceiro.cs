using PrismaRH.Dominio.Contratos;
using PrismaRH.Dominio.Folha;

namespace PrismaRH.Dominio.DecimoTerceiro;

/// <summary>Um mes do ano e se ele conta como avo de 13o.</summary>
public sealed record MesDoAvo(
    int Mes,
    int DiasTrabalhados,
    bool Conta)
{
    /// <summary>Por que este mes contou ou nao, em portugues, para a tela.</summary>
    public string Motivo => DiasTrabalhados == 0
        ? "sem vinculo no mes"
        : Conta
            ? $"{DiasTrabalhados} dias trabalhados"
            : $"so {DiasTrabalhados} dias, menos que {AvosDecimoTerceiro.DiasMinimosDoMes}";
}

/// <summary>Quantos avos de 13o um contrato acumulou num ano.</summary>
public sealed record ApuracaoAvos(
    int Ano,
    int Avos,
    IReadOnlyList<MesDoAvo> Meses)
{
    /// <summary>A fracao do 13o a que o contrato tem direito: avos sobre 12.</summary>
    public string Fracao => $"{Avos}/{AvosDecimoTerceiro.MesesDoAno}";

    public bool AnoCompleto => Avos == AvosDecimoTerceiro.MesesDoAno;
}

/// <summary>
/// Os avos de 13o salario de um contrato.
///
/// FONTE (CLAUDE.md secao 29): **Lei n. 4.090, de 13/07/1962** - a
/// gratificacao corresponde a 1/12 da remuneracao devida em dezembro, POR MES
/// DE SERVICO no ano correspondente, e a **fracao igual ou superior a 15 dias**
/// de trabalho e havida como mes integral.
///
/// NAO HA TABELA no banco, pela mesma razao dos periodos aquisitivos de ferias
/// (ver PeriodosAquisitivos): os avos sao funcao pura da admissao, do
/// desligamento e do calendario. Nada neles alguem altera.
///
/// Funcao pura: sem banco, sem relogio, sem HTTP (CLAUDE.md secao 10).
/// </summary>
public static class AvosDecimoTerceiro
{
    public const int MesesDoAno = 12;

    /// <summary>
    /// A fracao que faz o mes contar inteiro (Lei 4.090/1962, art. 1o, par. 2o).
    ///
    /// Quinze dias, e o teste e "igual ou superior": um mes com exatamente 15
    /// dias de vinculo CONTA. Um erro de &gt;= para &gt; aqui tiraria um avo de
    /// quem foi admitido no dia 17 de um mes de 31 dias.
    /// </summary>
    public const int DiasMinimosDoMes = 15;

    /// <summary>
    /// Apura os avos do contrato no ano, mes a mes.
    ///
    /// Devolve TODOS os doze meses, e nao so os que contam: a tela precisa
    /// mostrar por que fevereiro ficou de fora, e nao apenas omiti-lo.
    ///
    /// AFASTAMENTOS NAO SAO CONSIDERADOS, e nao por esquecimento: o dominio
    /// nao tem afastamento. Um mes em que a pessoa esteve afastada por
    /// doenca alem do 15o dia nao deveria contar, e aqui contaria - a mesma
    /// limitacao ja declarada nas ferias sobre faltas.
    /// </summary>
    public static ApuracaoAvos Apurar(ContratoTrabalho contrato, int ano)
    {
        ArgumentNullException.ThrowIfNull(contrato);

        if (ano < Competencia.AnoMinimo || ano > Competencia.AnoMaximo)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ano), ano,
                $"Ano precisa estar entre {Competencia.AnoMinimo} e {Competencia.AnoMaximo}.");
        }

        var meses = new List<MesDoAvo>(MesesDoAno);

        for (var mes = 1; mes <= MesesDoAno; mes++)
        {
            var competencia = new Competencia(ano, mes);

            // Reusa a mesma funcao que decide quem entra na folha mensal: o
            // trecho do contrato dentro do mes. Duas contas separadas para a
            // mesma pergunta acabariam divergindo.
            var periodo = MotorCalculoFolha.PeriodoNaCompetencia(contrato, competencia);

            var dias = periodo is { } p ? p.Fim.DayNumber - p.Inicio.DayNumber + 1 : 0;

            meses.Add(new MesDoAvo(mes, dias, dias >= DiasMinimosDoMes));
        }

        return new ApuracaoAvos(ano, meses.Count(m => m.Conta), meses);
    }
}
