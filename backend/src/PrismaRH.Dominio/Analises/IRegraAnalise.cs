namespace PrismaRH.Dominio.Analises;

/// <summary>
/// Um achado: o que a regra encontrou, em quem, e com quais numeros.
///
/// Os tres valores monetarios sao **opcionais** de proposito. "Este desligado
/// nao deveria estar na folha" nao tem valor esperado - forcar um zero ali
/// faria a tela mostrar "esperado: R$ 0,00", que e informacao falsa com cara de
/// informacao.
/// </summary>
public sealed record Achado(
    string Descricao,
    Guid? IdFolhaFuncionario = null,
    Guid? IdFuncionario = null,
    string? Matricula = null,
    string? NomeFuncionario = null,
    decimal? ValorEsperado = null,
    decimal? ValorEncontrado = null,
    string? Contexto = null)
{
    /// <summary>
    /// A diferenca, quando os dois lados existem.
    ///
    /// Derivada, e nao informada: se cada regra calculasse a propria, uma delas
    /// acabaria invertendo o sinal.
    /// </summary>
    public decimal? Diferenca => ValorEsperado is { } esperado && ValorEncontrado is { } encontrado
        ? encontrado - esperado
        : null;
}

/// <summary>
/// Uma regra oficial do Prisma RH.
///
/// ## O contrato, e o que ele proibe
///
/// A regra e **codigo do sistema**, escrito, revisado e testado - nunca texto
/// do usuario. O `CLAUDE.md secao 11` e o Security Gate da Fase 6 dizem a mesma
/// coisa: o usuario configura parametro, e nao escreve regra.
///
/// Nao existe caminho daqui para SQL, para expressao interpretada nem para
/// processo do sistema. O que o usuario controla e: se a regra roda, com qual
/// severidade, e com quais numeros - dentro da faixa que a propria regra
/// declarou.
///
/// ## Versao
///
/// <see cref="Versao"/> e um numero que sobe **quando a logica muda**, e ele e
/// congelado em cada resultado gravado. E o mesmo mecanismo de
/// `LancamentoFolha`, pela mesma razao (`CLAUDE.md secao 4.3`): um resultado de
/// agosto precisa continuar dizendo qual versao da regra o produziu, mesmo
/// depois de a regra mudar.
///
/// **Nao ha tabela `VersaoRegra`.** O `ROADMAP.md` a previa como estrutura
/// possivel, mas ela guardaria uma copia de um numero que ja vive no codigo,
/// junto da logica que ele versiona - e a copia seria a que envelhece. O
/// `ROADMAP.md secao 0` proibe estrutura sem uso real, e o `CLAUDE.md secao 20`
/// proibe abstracao sem necessidade demonstrada.
/// </summary>
public interface IRegraAnalise
{
    CodigoRegra Codigo { get; }

    /// <summary>Sobe quando a LOGICA muda. Congelado em cada resultado.</summary>
    int Versao { get; }

    CategoriaRegra Categoria { get; }

    /// <summary>A severidade de fabrica. A organizacao pode mudar.</summary>
    Severidade SeveridadePadrao { get; }

    /// <summary>Nome curto, para a tela.</summary>
    string Nome { get; }

    /// <summary>O que ela procura, e por que aquilo importa.</summary>
    string Explicacao { get; }

    /// <summary>Os parametros que ela aceita. Lista vazia e legitimo.</summary>
    IReadOnlyList<DefinicaoParametro> Parametros { get; }

    /// <summary>
    /// Roda sobre o retrato da folha.
    ///
    /// Funcao pura: mesmo retrato, mesmos achados, sempre. E o que sustenta o
    /// criterio de aceite "execucao reproduzivel".
    /// </summary>
    IEnumerable<Achado> Executar(ContextoAnalise contexto, ValoresParametros parametros);
}
