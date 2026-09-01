using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PrismaRH.Api.Saude;

/// <summary>
/// Escreve a resposta de /health como JSON, em vez do texto simples padrao do ASP.NET Core.
/// </summary>
public static class EscritorRespostaSaude
{
    public static Task EscreverAsync(HttpContext contexto, HealthReport relatorio)
    {
        var resposta = new RespostaSaude(
            StatusSaude.Traduzir(relatorio.Status),
            [.. relatorio.Entries.Select(entrada => new VerificacaoSaude(
                entrada.Key,
                StatusSaude.Traduzir(entrada.Value.Status),
                entrada.Value.Description))]);

        contexto.Response.ContentType = "application/json; charset=utf-8";
        return contexto.Response.WriteAsJsonAsync(resposta);
    }

    /// <summary>
    /// A versao de producao: **so o status**, sem listar as verificacoes.
    ///
    /// `/health` e rota ANONIMA. A versao detalhada nomeia cada verificacao -
    /// "banco-de-dados" - e isso conta a topologia do sistema para qualquer
    /// varredura automatizada, de graca. O item 10 do Security Gate da Fase 10
    /// e explicito: o health nao revela versao nem detalhe interno.
    ///
    /// Quem precisa do detalhe le o CloudWatch, que tem controle de acesso.
    /// </summary>
    public static Task EscreverMinimoAsync(HttpContext contexto, HealthReport relatorio)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(relatorio);

        contexto.Response.ContentType = "application/json; charset=utf-8";

        return contexto.Response.WriteAsJsonAsync(
            new { status = StatusSaude.Traduzir(relatorio.Status) });
    }
}
