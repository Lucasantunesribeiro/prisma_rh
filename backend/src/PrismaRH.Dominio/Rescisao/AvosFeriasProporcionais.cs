using PrismaRH.Dominio.Contratos;
using PrismaRH.Dominio.Ferias;
using PrismaRH.Dominio.Folha;

namespace PrismaRH.Dominio.Rescisao;

/// <summary>Um mes do periodo aquisitivo incompleto e se ele vira avo.</summary>
public sealed record MesProporcional(
    DateOnly Inicio,
    DateOnly Fim,
    int Dias,
    bool Conta)
{
    public string Motivo => Conta
        ? $"{Dias} dias"
        : $"so {Dias} dias, nao passa de {AvosFeriasProporcionais.DiasMinimosDoMes}";
}

/// <summary>Os avos de ferias proporcionais do periodo aquisitivo incompleto.</summary>
public sealed record ApuracaoFeriasProporcionais(
    DateOnly InicioPeriodo,
    DateOnly FimPeriodo,
    int Avos,
    IReadOnlyList<MesProporcional> Meses)
{
    public string Fracao => $"{Avos}/{AvosFeriasProporcionais.MesesDoPeriodo}";
}

/// <summary>
/// Ferias PROPORCIONAIS: os avos do periodo aquisitivo que ficou incompleto
/// quando o contrato terminou.
///
/// FONTES (CLAUDE.md secao 29):
///
/// - **CLT art. 146, paragrafo unico**: na cessacao do contrato apos 12 meses
///   de servico, o empregado tem direito a remuneracao relativa ao periodo
///   incompleto, **na proporcao de 1/12 por mes de servico ou FRACAO SUPERIOR
///   A 14 DIAS**;
/// - **CLT art. 147** e **Sumula 171 do TST**: salvo na hipotese de dispensa
///   por JUSTA CAUSA, a extincao do contrato sujeita o empregador ao pagamento
///   das ferias proporcionais, ainda que incompleto o periodo aquisitivo.
///
/// ATENCAO a diferenca em relacao ao 13o (Lei 4.090/1962), que usa fracao
/// **igual ou superior a 15 dias**. Numericamente da no mesmo - "superior a
/// 14" e "igual ou superior a 15" sao a mesma coisa em dias inteiros -, mas os
/// textos sao de leis diferentes, e reusar a constante de uma na outra
/// esconderia que sao duas normas. Se uma mudar, a outra nao muda junto.
///
/// QUEM tem direito depende do MOTIVO do desligamento, e essa decisao NAO mora
/// aqui: ver MatrizVerbasRescisorias. Esta classe responde apenas "quantos
/// avos correram".
///
/// Funcao pura: sem banco, sem relogio, sem HTTP.
/// </summary>
public static class AvosFeriasProporcionais
{
    public const int MesesDoPeriodo = 12;

    /// <summary>
    /// A fracao precisa SUPERAR este numero (art. 146, paragrafo unico).
    ///
    /// Ou seja: 15 dias contam, 14 nao. O teste e &gt;, e nao &gt;=, porque o
    /// texto diz "superior a 14".
    /// </summary>
    public const int DiasMinimosDoMes = 14;

    /// <summary>
    /// Apura os avos do periodo aquisitivo INCOMPLETO na data do desligamento.
    ///
    /// Os periodos COMPLETOS nao entram aqui: eles sao ferias vencidas, e
    /// PeriodosAquisitivos ja os conhece. Esta apuracao cobre so o pedaco que
    /// ficou pela metade.
    ///
    /// Os "meses" sao contados a partir do INICIO DO PERIODO AQUISITIVO, e nao
    /// do calendario: quem tem periodo comecando em 15/03 tem meses de 15 a 14.
    /// Contar por mes-calendario partiria o primeiro mes em dois pedacos e
    /// poderia inventar ou perder um avo.
    /// </summary>
    public static ApuracaoFeriasProporcionais? Apurar(ContratoTrabalho contrato, DateOnly desligamento)
    {
        ArgumentNullException.ThrowIfNull(contrato);

        var periodo = PeriodosAquisitivos.EmAndamento(contrato, desligamento);

        if (periodo is null)
        {
            // Sem periodo em andamento: ou o contrato terminou exatamente no
            // fim de um periodo, ou a data e anterior a admissao. Nos dois
            // casos nao ha proporcional a apurar.
            return null;
        }

        var meses = new List<MesProporcional>();

        for (var i = 0; i < MesesDoPeriodo; i++)
        {
            var inicio = periodo.Inicio.AddMonths(i);

            if (inicio > desligamento)
            {
                break;
            }

            // O mes vai ate a vespera do mesmo dia do mes seguinte - ou ate o
            // desligamento, se ele vier antes.
            var fimDoMes = periodo.Inicio.AddMonths(i + 1).AddDays(-1);
            var fim = fimDoMes < desligamento ? fimDoMes : desligamento;

            var dias = fim.DayNumber - inicio.DayNumber + 1;

            meses.Add(new MesProporcional(inicio, fim, dias, dias > DiasMinimosDoMes));
        }

        return new ApuracaoFeriasProporcionais(
            periodo.Inicio,
            periodo.Fim,
            meses.Count(m => m.Conta),
            meses);
    }
}
