using PrismaRH.Dominio.Folha;

namespace PrismaRH.Dominio.Analises.Regras;

/// <summary>
/// Holerite com liquido negativo.
///
/// ## Por que importa
///
/// Liquido negativo significa que a pessoa **deve** para a empresa naquele mes.
/// Pode ser legitimo - adiantamento maior que o salario de quem trabalhou
/// poucos dias -, mas nunca deveria passar sem alguem ver: e a diferenca entre
/// um acerto combinado e um desconto indevido.
///
/// ## Por que a tolerancia existe
///
/// Arredondamento de centavos pode produzir um liquido de -R$ 0,01 sem que haja
/// nada errado. O padrao e **zero** - qualquer negativo acusa -, e quem tiver
/// um caso recorrente de centavo pode afrouxar sem precisar desligar a regra.
/// </summary>
public sealed class LiquidoNegativoRegra : IRegraAnalise
{
    public const string ParametroTolerancia = "toleranciaEmReais";

    public CodigoRegra Codigo => CodigoRegra.LiquidoNegativo;

    public int Versao => 1;

    public CategoriaRegra Categoria => CategoriaRegra.Valores;

    public Severidade SeveridadePadrao => Severidade.Alta;

    public string Nome => "Liquido negativo";

    public string Explicacao =>
        "Procura holerite cujo liquido ficou abaixo de zero - a pessoa deve para a empresa.";

    public IReadOnlyList<DefinicaoParametro> Parametros =>
    [
        new(ParametroTolerancia,
            "Tolerancia em reais",
            "Quanto de negativo passa sem acusar. Serve para centavos de arredondamento.",
            TipoParametro.Decimal,
            Padrao: 0m,
            Minimo: 0m,
            Maximo: 1_000m),
    ];

    public IEnumerable<Achado> Executar(ContextoAnalise contexto, ValoresParametros parametros)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(parametros);

        var tolerancia = parametros.Obter(ParametroTolerancia);

        foreach (var holerite in contexto.Holerites.Where(h => h.Liquido < -tolerancia))
        {
            yield return new Achado(
                $"Liquido de {TextoMonetario.Reais(holerite.Liquido)}: os descontos "
                + $"({TextoMonetario.Reais(holerite.TotalDescontos)}) passam dos proventos "
                + $"({TextoMonetario.Reais(holerite.TotalProventos)}).",
                holerite.IdFolhaFuncionario,
                holerite.IdFuncionario,
                holerite.Matricula,
                holerite.NomeFuncionario,
                ValorEsperado: 0m,
                ValorEncontrado: holerite.Liquido,
                Contexto: $"tolerancia={DefinicaoParametro.Formatar(tolerancia)}");
        }
    }
}

/// <summary>
/// Descontos passando de um percentual dos proventos.
///
/// ## Por que importa
///
/// O holerite fecha, o liquido e positivo, e mesmo assim algo esta errado: uma
/// pensao lancada em duplicidade, um vale mal digitado, um adiantamento que
/// nao foi baixado. Sao os casos que <see cref="LiquidoNegativoRegra"/> nao
/// pega justamente porque nao chegam a virar negativo.
///
/// ## Sobre o padrao de 70%
///
/// **Nao e afirmacao legal.** E um padrao de produto, escolhido porque a maior
/// parte dos holerites reais fica bem abaixo disso, e configuravel por
/// organizacao. O `CLAUDE.md secao 29` exige fonte oficial para regra legal, e
/// esta regra nao pretende ser uma: ela nao recusa nada, nao muda calculo e nao
/// cita norma - ela chama alguem para olhar.
/// </summary>
public sealed class DescontoAcimaDoLimiteRegra : IRegraAnalise
{
    public const string ParametroPercentual = "percentualMaximo";

    public CodigoRegra Codigo => CodigoRegra.DescontoAcimaDoLimite;

    public int Versao => 1;

    public CategoriaRegra Categoria => CategoriaRegra.Valores;

    public Severidade SeveridadePadrao => Severidade.Media;

    public string Nome => "Desconto acima do limite";

    public string Explicacao =>
        "Procura holerite em que os descontos passam do percentual configurado sobre os "
        + "proventos.";

    public IReadOnlyList<DefinicaoParametro> Parametros =>
    [
        new(ParametroPercentual,
            "Percentual maximo de desconto",
            "Acima disso, o holerite entra no relatorio. Padrao de produto, nao regra legal.",
            TipoParametro.Percentual,
            Padrao: 70m,
            Minimo: 1m,
            Maximo: 100m),
    ];

    public IEnumerable<Achado> Executar(ContextoAnalise contexto, ValoresParametros parametros)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(parametros);

        var percentual = parametros.Obter(ParametroPercentual);

        foreach (var holerite in contexto.Holerites)
        {
            // Sem proventos nao ha percentual: dividir por zero nao e "100%",
            // e o caso ja e coberto pelo liquido negativo.
            if (holerite.TotalProventos <= 0m)
            {
                continue;
            }

            var teto = Dinheiro.Arredondar(holerite.TotalProventos * percentual / 100m);

            if (holerite.TotalDescontos <= teto)
            {
                continue;
            }

            var praticado = Dinheiro.Arredondar(
                holerite.TotalDescontos / holerite.TotalProventos * 100m);

            yield return new Achado(
                $"Descontos de {TextoMonetario.Reais(holerite.TotalDescontos)} sobre proventos de "
                + $"{TextoMonetario.Reais(holerite.TotalProventos)} - "
                + $"{TextoMonetario.Percentual(praticado)}, acima do limite de "
                + $"{TextoMonetario.Percentual(percentual)}.",
                holerite.IdFolhaFuncionario,
                holerite.IdFuncionario,
                holerite.Matricula,
                holerite.NomeFuncionario,
                ValorEsperado: teto,
                ValorEncontrado: holerite.TotalDescontos,
                Contexto: $"percentualPraticado={DefinicaoParametro.Formatar(praticado)}");
        }
    }
}

/// <summary>
/// A mesma rubrica lancada mais de uma vez no mesmo holerite.
///
/// ## Por que so os lancamentos MANUAIS
///
/// O motor de calculo repete rubrica de proposito e com frequencia: duas
/// concessoes de ferias no mesmo mes geram duas linhas da mesma rubrica, e as
/// parcelas de 13o tambem. Acusar o que o proprio sistema produziu seria acusar
/// o comportamento correto.
///
/// A duplicata que interessa e a **digitada duas vezes** - a hora extra lancada
/// pela manha e de novo a tarde, porque a primeira nao apareceu na tela.
/// </summary>
public sealed class RubricaDuplicadaRegra : IRegraAnalise
{
    public CodigoRegra Codigo => CodigoRegra.RubricaDuplicada;

    public int Versao => 1;

    public CategoriaRegra Categoria => CategoriaRegra.Duplicidade;

    public Severidade SeveridadePadrao => Severidade.Media;

    public string Nome => "Rubrica lancada em duplicidade";

    public string Explicacao =>
        "Procura a mesma rubrica lancada manualmente mais de uma vez no mesmo holerite. "
        + "Lancamentos gerados pelo calculo sao ignorados, porque repetir e legitimo neles.";

    public IReadOnlyList<DefinicaoParametro> Parametros => [];

    public IEnumerable<Achado> Executar(ContextoAnalise contexto, ValoresParametros parametros)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        foreach (var holerite in contexto.Holerites)
        {
            var repetidas = holerite.Lancamentos
                .Where(l => l.Origem == OrigemLancamento.Manual)
                .GroupBy(l => l.CodigoRubrica, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1);

            foreach (var grupo in repetidas)
            {
                var total = Dinheiro.Arredondar(grupo.Sum(l => l.Valor));
                var primeiro = grupo.First();

                yield return new Achado(
                    $"A rubrica {primeiro.CodigoRubrica} ({primeiro.NomeRubrica}) foi lancada "
                    + $"{grupo.Count()} vezes a mao, somando {TextoMonetario.Reais(total)}.",
                    holerite.IdFolhaFuncionario,
                    holerite.IdFuncionario,
                    holerite.Matricula,
                    holerite.NomeFuncionario,
                    ValorEncontrado: total,
                    Contexto: $"rubrica={primeiro.CodigoRubrica};ocorrencias={grupo.Count()}");
            }
        }
    }
}
