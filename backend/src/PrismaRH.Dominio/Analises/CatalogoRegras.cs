using PrismaRH.Dominio.Analises.Regras;

namespace PrismaRH.Dominio.Analises;

/// <summary>
/// Todas as regras que existem. Lista fechada, no codigo.
///
/// ## Por que uma lista estatica, e nao injecao de dependencia
///
/// A tentacao seria varrer o assembly e registrar tudo o que implementa
/// <see cref="IRegraAnalise"/>. Ficaria "elegante" e seria pior por duas razoes
/// concretas:
///
/// 1. **O catalogo deixaria de ser revisavel.** Hoje, acrescentar uma regra ao
///    sistema e uma linha visivel neste arquivo, num diff que alguem le. Com
///    varredura, uma classe nova em qualquer canto do projeto passaria a rodar
///    sobre folha de pagamento sem aparecer em lugar nenhum;
/// 2. **A ordem deixaria de ser deterministica.** A ordem de reflexao nao e
///    garantida entre versoes do runtime, e o criterio de aceite da fase e
///    execucao **reproduzivel**.
///
/// O `CLAUDE.md secao 20` diz para nao criar abstracao sem uso real. Um
/// container para seis objetos sem estado e exatamente isso.
/// </summary>
public static class CatalogoRegras
{
    /// <summary>
    /// A ordem daqui e a ordem de execucao e a de exibicao.
    ///
    /// Contrato e ausencia primeiro: sao os achados que mudam quem esta na
    /// folha. Depois os de valor, que so fazem sentido sobre quem deveria mesmo
    /// estar la.
    /// </summary>
    public static readonly IReadOnlyList<IRegraAnalise> Todas =
    [
        new DesligadoNaFolhaRegra(),
        new AusenteDaFolhaRegra(),
        new LiquidoNegativoRegra(),
        new RubricaDuplicadaRegra(),
        new DescontoAcimaDoLimiteRegra(),
        new VariacaoSalarialRegra(),
    ];

    private static readonly IReadOnlyDictionary<CodigoRegra, IRegraAnalise> PorCodigo =
        Todas.ToDictionary(r => r.Codigo);

    /// <summary>
    /// A regra de um codigo, ou nulo.
    ///
    /// Devolve nulo em vez de lancar porque o codigo pode ter vindo do banco -
    /// uma configuracao gravada por uma versao do sistema que conhecia uma
    /// regra que esta nao conhece mais. Nesse caso a configuracao e ignorada,
    /// e nao derruba a execucao inteira.
    /// </summary>
    public static IRegraAnalise? De(CodigoRegra codigo) =>
        PorCodigo.TryGetValue(codigo, out var regra) ? regra : null;

    /// <summary>Existe regra para este codigo?</summary>
    public static bool Conhece(CodigoRegra codigo) => PorCodigo.ContainsKey(codigo);
}
