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
    Ferias = 2
}
