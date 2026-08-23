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
}
