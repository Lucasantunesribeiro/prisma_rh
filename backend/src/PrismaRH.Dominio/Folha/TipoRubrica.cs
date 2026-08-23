namespace PrismaRH.Dominio.Folha;

/// <summary>
/// O que a rubrica faz com o liquido.
///
/// Valores explicitos: o numero vai para o banco e para o JSON, e uma
/// reordenacao alfabetica futura nao pode transformar provento em desconto nas
/// folhas ja fechadas.
/// </summary>
public enum TipoRubrica
{
    /// <summary>Soma no liquido. Salario, comissao, adicional.</summary>
    Provento = 1,

    /// <summary>Subtrai do liquido. Vale-transporte, adiantamento, faltas.</summary>
    Desconto = 2,

    /// <summary>Nao mexe no liquido. Aparece no holerite so para informar - base de FGTS, por exemplo.</summary>
    Informativo = 3,
}
