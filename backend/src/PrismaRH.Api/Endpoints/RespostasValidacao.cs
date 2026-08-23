using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace PrismaRH.Api.Endpoints;

/// <summary>
/// Converte a excecao de invariante do dominio em resposta HTTP de validacao.
///
/// Existe para nao repetir o mesmo try/catch em cada endpoint. As regras
/// continuam no dominio; aqui so se traduz o erro para a borda HTTP.
/// </summary>
public static class RespostasValidacao
{
    public static IResult De(ArgumentException erro) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [erro.ParamName ?? "requisicao"] = [PrimeiraLinha(erro.Message)]
        });

    /// <summary>
    /// Violacao da constraint que impede sobreposicao de vigencias.
    ///
    /// O agregado ja recusa periodos sobrepostos, entao chegar aqui significa
    /// que duas requisicoes passaram pela validacao em C# ao mesmo tempo. E
    /// conflito de concorrencia (409), nao erro do servidor (500).
    /// </summary>
    public static IResult? DeSobreposicao(DbUpdateException erro)
    {
        if (erro.InnerException is not PostgresException postgres)
        {
            return null;
        }

        // 23P01 = exclusion_violation
        if (postgres.SqlState != "23P01")
        {
            return null;
        }

        return Results.Problem(
            title: "Conflito de vigencias",
            detail: "Outra alteracao para este contrato foi gravada ao mesmo tempo. Recarregue e tente de novo.",
            statusCode: StatusCodes.Status409Conflict);
    }

    public static IResult De(InvalidOperationException erro) =>
        Results.Problem(
            title: "Operacao invalida",
            detail: erro.Message,
            statusCode: StatusCodes.Status409Conflict);

    /// <summary>
    /// ArgumentException acrescenta " (Parameter 'x')" a mensagem. Isso e ruido
    /// de implementacao e nao ajuda quem esta preenchendo o formulario.
    /// </summary>
    private static string PrimeiraLinha(string mensagem)
    {
        var corte = mensagem.IndexOf(" (Parameter", StringComparison.Ordinal);
        return corte > 0 ? mensagem[..corte] : mensagem;
    }
}
