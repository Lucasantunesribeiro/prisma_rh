using System.Net.Http.Json;

namespace PrismaRH.Testes.Isolamento;

/// <summary>
/// Lê uma listagem paginada e devolve só os itens.
///
/// ## Por que existe
///
/// A Fase 10 paginou `/api/folhas`, `/api/rubricas` e `/api/cargos` — as três
/// listagens que crescem sem limite natural, e que o `CLAUDE.md §24.19 item 3`
/// nomeava como vetor de exaustão.
///
/// A resposta deixou de ser um array e passou a ser
/// `{ total, paginaAtual, tamanho, itens }`. Este ajudante evita repetir a
/// desembalagem em vinte e três chamadas de teste, e deixa o teste falando do
/// que ele quer verificar em vez do formato do envelope.
///
/// ⚠️ Ele pede **o teto de página**. Um teste que verifica "a rubrica X está na
/// lista" quebraria de forma confusa se a rubrica caísse na página 2 — e a
/// causa real (paginação) não apareceria na mensagem de falha.
/// </summary>
public sealed record PaginaHttp<T>(int Total, int PaginaAtual, int Tamanho, List<T> Itens);

public static class LeituraPaginada
{
    /// <summary>Tamanho pedido nos testes: o teto do servidor.</summary>
    public const int TetoDoServidor = 200;

    public static async Task<List<T>> PaginaDe<T>(this HttpClient cliente, string rota)
    {
        ArgumentNullException.ThrowIfNull(cliente);
        ArgumentNullException.ThrowIfNull(rota);

        var separador = rota.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var url = $"{rota}{separador}tamanho={TetoDoServidor}";

        var pagina = await cliente.GetFromJsonAsync<PaginaHttp<T>>(url);

        return pagina?.Itens ?? [];
    }
}
