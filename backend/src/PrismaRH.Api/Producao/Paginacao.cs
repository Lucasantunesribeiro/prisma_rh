using Microsoft.EntityFrameworkCore;

namespace PrismaRH.Api.Producao;

/// <summary>
/// Uma página de resultados. O formato que o sistema já usa em empresas,
/// funcionários e trabalhos — repetido aqui para que toda listagem responda
/// igual.
/// </summary>
public sealed record Pagina<T>(int Total, int PaginaAtual, int Tamanho, IReadOnlyList<T> Itens);

/// <summary>
/// Paginação com teto rígido (`CLAUDE.md §24.18` e a pendência `§24.19 item 3`).
///
/// ## O que a pendência dizia
///
/// Registrada em 27/08/2026: *"`GET /api/folhas`, `/api/rubricas`,
/// `/api/cargos`, os estabelecimentos, os holerites e os lançamentos devolvem
/// tudo."* Sem impacto em `localhost`; com volume real, é vetor de exaustão —
/// e num sistema multiempresa o custo cai sobre todos os tenants, não só sobre
/// quem pediu.
///
/// ## Duas defesas diferentes, para dois casos diferentes
///
/// - **`PaginarAsync`** — listagens que crescem sem limite natural: folhas,
///   rubricas, cargos, estabelecimentos. Devolvem `Pagina&lt;T&gt;`, com o
///   total para o frontend saber quantas páginas existem.
/// - **`ComTeto`** — sub-recursos limitados por natureza: dependentes de um
///   contrato, faixas de uma tabela legal. Aqui paginar seria cerimônia; o que
///   falta é apenas um limite superior, e mudar o contrato de resposta
///   quebraria a tela sem ganho real.
///
/// ## Ordenação determinística é requisito, e não estética
///
/// Sem `ORDER BY` estável, `OFFSET`/`LIMIT` no PostgreSQL pode **repetir ou
/// pular linhas** entre páginas: o planejador não promete ordem, e duas
/// execuções da mesma consulta podem devolver a mesma linha na página 1 e na
/// 2. Quem chama passa a ordenação, e ela precisa terminar num campo único.
/// </summary>
public static class Paginacao
{
    /// <summary>Padrão quando o cliente não pede tamanho. Cabe numa tela.</summary>
    public const int TamanhoPadrao = 50;

    /// <summary>
    /// Teto rígido. Pedir mais que isso **não** é erro: o valor é reduzido em
    /// silêncio.
    ///
    /// Recusar com 400 pareceria mais correto e seria pior na prática — um
    /// cliente que pede 10.000 quer os dados, não uma aula. Ele recebe 200 e
    /// segue paginando.
    /// </summary>
    public const int TamanhoMaximo = 200;

    /// <summary>Teto para sub-recursos que não são paginados.</summary>
    public const int TetoSubRecurso = 500;

    /// <summary>
    /// Normaliza o que veio da query string.
    ///
    /// Página zero, negativa ou absurda vira 1; tamanho fora da faixa é
    /// grampeado. Nada aqui estoura: valor inválido em paginação é digitação
    /// errada, não ataque, e derrubar a listagem por causa disso seria
    /// transformar um detalhe em erro.
    /// </summary>
    public static (int Pagina, int Tamanho) Normalizar(int? pagina, int? tamanho) => (
        pagina is null or < 1 ? 1 : pagina.Value,
        tamanho is null ? TamanhoPadrao : Math.Clamp(tamanho.Value, 1, TamanhoMaximo));

    /// <summary>
    /// Conta e devolve uma página.
    ///
    /// A contagem é uma consulta separada de propósito: trazer o total junto
    /// com as linhas exigiria uma janela sobre o conjunto inteiro, que é mais
    /// caro do que dois `SELECT` simples nos volumes deste sistema.
    /// </summary>
    public static async Task<Pagina<TSaida>> PaginarAsync<TEntrada, TSaida>(
        IQueryable<TEntrada> consulta,
        Func<TEntrada, TSaida> converter,
        int? pagina,
        int? tamanho,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(consulta);
        ArgumentNullException.ThrowIfNull(converter);

        var (p, t) = Normalizar(pagina, tamanho);

        var total = await consulta.CountAsync(ct);

        var itens = await consulta
            .Skip((p - 1) * t)
            .Take(t)
            .ToListAsync(ct);

        return new Pagina<TSaida>(total, p, t, [.. itens.Select(converter)]);
    }

    /// <summary>
    /// Teto sem paginação, para sub-recurso limitado por natureza.
    ///
    /// Mantém o contrato de resposta como lista, e impede que um caso
    /// patológico — mil dependentes num contrato — traga tudo para a memória.
    /// </summary>
    public static IQueryable<T> ComTeto<T>(this IQueryable<T> consulta) =>
        consulta.Take(TetoSubRecurso);
}
