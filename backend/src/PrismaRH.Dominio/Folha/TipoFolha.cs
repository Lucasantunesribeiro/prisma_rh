namespace PrismaRH.Dominio.Folha;

/// <summary>
/// Que processamento esta folha representa.
///
/// Ate a Fase 4E toda folha era mensal, e o tipo nao existia. Ele entra agora
/// porque ferias sao uma folha DIFERENTE, e nao mais uma rubrica dentro da
/// mensal: elas tem competencia propria, sao calculadas a partir das
/// concessoes e nao dos avos do mes, e uma pessoa pode ter as duas no mesmo
/// mes.
///
/// O indice unico de folhas passou a incluir esta coluna pelo mesmo motivo:
/// uma empresa pode ter, em agosto, a folha mensal E a de ferias.
/// </summary>
public enum TipoFolha
{
    /// <summary>A folha do mes: salario proporcional, lancamentos e encargos.</summary>
    Mensal = 1,

    /// <summary>
    /// O pagamento das ferias concedidas na competencia: remuneracao, terco
    /// constitucional e abono pecuniario.
    /// </summary>
    Ferias = 2,

    /// <summary>
    /// O acerto do desligamento: saldo, aviso, ferias e 13o proporcionais e a
    /// indenizacao do FGTS.
    ///
    /// Diferente das outras duas em algo importante: ela nao percorre os
    /// contratos ATIVOS da empresa, e sim os DESLIGADOS na competencia.
    /// </summary>
    Rescisao = 3,

    /// <summary>
    /// A PRIMEIRA parcela, paga entre fevereiro e novembro (Lei 4.749/1965,
    /// art. 2o).
    ///
    /// **Incide FGTS e mais nada.** MOS eSocial S-1.3, item 10.3.4: "o FGTS, ao
    /// contrario da CP e do IRRF, incide sobre a parcela do adiantamento do 13o
    /// salario no mes em que for paga".
    /// </summary>
    DecimoTerceiroAdiantamento = 4,

    /// <summary>
    /// A folha ANUAL do 13o, em dezembro (periodo de apuracao AAAA).
    ///
    /// E aqui que INSS e IRRF sao apurados, sobre o TOTAL - MOS S-1.3, item
    /// 10.3.4: "A apuracao da CP e do IRRF incidentes sobre o 13o salario e
    /// feita apenas na folha de 13o (anual)".
    ///
    /// DOIS tipos, e nao um com campo "parcela": o indice unico que a Fase 4E
    /// compos e (empresa, competencia, tipo), e duas folhas de 13o precisam
    /// conviver na mesma empresa sem colidir.
    /// </summary>
    DecimoTerceiro = 5
}
