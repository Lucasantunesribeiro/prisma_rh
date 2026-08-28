namespace PrismaRH.Dominio.Parametros;

/// <summary>
/// Os parametros legais que a folha usa para apurar encargos.
///
/// Existe porque a lista cresceu: a Fase 4B trouxe o INSS, a 4C o FGTS e a 4D
/// o IRRF, e cada um virava mais um parametro posicional em cinco assinaturas
/// diferentes - quase sempre nulo, quase sempre na mesma ordem, e uma troca de
/// posicao entre dois deles compilaria sem reclamar.
///
/// Cada campo nulo significa "esta organizacao nao configurou este encargo": a
/// folha calcula sem ele, e o lancamento correspondente nao aparece.
///
/// NAO e uma abstracao especulativa. Vale a pena porque atravessa o motor
/// inteiro hoje, e as fases 4E a 4G acrescentam mais encargos ao mesmo lugar.
/// </summary>
public sealed record ParametrosEncargos(
    ParametrosInss? Inss = null,
    ParametrosFgts? Fgts = null,
    ParametrosIrrf? Irrf = null)
{
    /// <summary>Organizacao sem encargo algum configurado.</summary>
    public static readonly ParametrosEncargos Nenhum = new();
}
