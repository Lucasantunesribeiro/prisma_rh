using PrismaRH.Dominio.Folha;

namespace PrismaRH.Dominio.Analises.Regras;

/// <summary>
/// Salario de referencia variando alem da tolerancia entre uma competencia e a
/// anterior.
///
/// ## Por que importa
///
/// E a regra que pega o erro de digitacao em salario - o 3.500 que virou 35.000
/// -, a vigencia cadastrada com a data errada e o reajuste aplicado na pessoa
/// errada. Nenhuma dessas coisas produz holerite invalido: elas produzem
/// holerite **certo para um salario errado**, e nenhuma outra regra as veria.
///
/// ## O que ela compara, e o que deliberadamente NAO compara
///
/// Compara o **salario de referencia** - o contratual congelado no holerite -,
/// e nao o liquido nem os proventos. O liquido varia todo mes por motivo
/// legitimo: hora extra, falta, ferias, adiantamento. Compara-lo daria alarme
/// em quase todo mundo, e regra que acusa todo mundo nao acusa ninguem.
///
/// ## Sem folha anterior, sem achado
///
/// Quem foi admitido neste mes nao tem com o que comparar. Tratar a ausencia
/// como zero produziria "variacao de 100%" em cada admissao - o oposto de util,
/// justo no mes em que a folha tem mais gente nova.
/// </summary>
public sealed class VariacaoSalarialRegra : IRegraAnalise
{
    public const string ParametroTolerancia = "percentualTolerancia";

    public CodigoRegra Codigo => CodigoRegra.VariacaoSalarial;

    public int Versao => 1;

    public CategoriaRegra Categoria => CategoriaRegra.Salario;

    public Severidade SeveridadePadrao => Severidade.Media;

    public string Nome => "Variacao salarial fora da tolerancia";

    public string Explicacao =>
        "Compara o salario de referencia com o da competencia anterior e acusa quando a "
        + "diferenca passa do percentual configurado, para cima ou para baixo.";

    public IReadOnlyList<DefinicaoParametro> Parametros =>
    [
        new(ParametroTolerancia,
            "Tolerancia de variacao",
            "Diferenca aceita entre uma competencia e a anterior, para mais ou para menos.",
            TipoParametro.Percentual,
            Padrao: 30m,
            Minimo: 1m,
            Maximo: 100m),
    ];

    public IEnumerable<Achado> Executar(ContextoAnalise contexto, ValoresParametros parametros)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(parametros);

        var tolerancia = parametros.Obter(ParametroTolerancia);

        foreach (var holerite in contexto.Holerites)
        {
            if (!contexto.SalarioNaFolhaAnterior.TryGetValue(holerite.IdContrato, out var anterior)
                || anterior <= 0m)
            {
                continue;
            }

            var atual = holerite.SalarioReferencia;
            var variacao = Dinheiro.Arredondar(Math.Abs(atual - anterior) / anterior * 100m);

            if (variacao <= tolerancia)
            {
                continue;
            }

            var sentido = atual > anterior ? "subiu" : "caiu";

            yield return new Achado(
                $"O salario de referencia {sentido} de {TextoMonetario.Reais(anterior)} para "
                + $"{TextoMonetario.Reais(atual)} - {TextoMonetario.Percentual(variacao)}, "
                + $"acima da tolerancia de {TextoMonetario.Percentual(tolerancia)}.",
                holerite.IdFolhaFuncionario,
                holerite.IdFuncionario,
                holerite.Matricula,
                holerite.NomeFuncionario,
                ValorEsperado: anterior,
                ValorEncontrado: atual,
                Contexto: $"variacao={DefinicaoParametro.Formatar(variacao)}");
        }
    }
}
