using Microsoft.EntityFrameworkCore;
using PrismaRH.Api.Identidade;
using PrismaRH.Aplicacao.Comum;
using PrismaRH.Dominio.DecimoTerceiro;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Api.Endpoints;

public sealed record MesDoAvoResposta(int Mes, int DiasTrabalhados, bool Conta, string Motivo)
{
    public static MesDoAvoResposta De(MesDoAvo m) =>
        new(m.Mes, m.DiasTrabalhados, m.Conta, m.Motivo);
}

public sealed record AvosResposta(
    Guid IdContrato,
    string Matricula,
    DateOnly DataAdmissao,
    DateOnly? DataDesligamento,
    int Ano,
    int Avos,
    string Fracao,
    bool AnoCompleto,
    IReadOnlyList<MesDoAvoResposta> Meses);

/// <summary>
/// Os avos de 13o salario de um contrato num ano.
///
/// SOMENTE LEITURA, e isso e a natureza da etapa: os avos sao derivados da
/// admissao, do desligamento e do calendario (ver AvosDecimoTerceiro). Nao ha
/// nada para gravar.
///
/// O PAGAMENTO do 13o ainda nao existe: ele depende de uma decisao sobre em
/// que momento INSS e IRRF incidem, que este projeto ainda nao tem apoiada em
/// fonte oficial inequivoca.
///
/// Rota ANINHADA no contrato, como as ferias: o dado e resolvido pelo pai, que
/// ja passa pelo filtro global (CLAUDE.md secao 24.6).
/// </summary>
public static class DecimoTerceiroEndpoints
{
    public static IEndpointRouteBuilder MapearDecimoTerceiro(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/api/contratos/{idContrato:guid}/decimo-terceiro")
            .WithTags("Decimo terceiro");

        grupo.MapGet("/avos", ObterAvosAsync)
            .WithSummary("Avos de 13o do contrato no ano, mes a mes")
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        return rotas;
    }

    private static async Task<IResult> ObterAvosAsync(
        Guid idContrato,
        int? ano,
        PrismaRhDbContext db,
        IRelogio relogio,
        CancellationToken ct)
    {
        // Passa pelo filtro global: contrato de outra organizacao nao existe
        // daqui, e a resposta e 404 - nunca 403, que confirmaria o id.
        var contrato = await db.ContratosTrabalho
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == idContrato, ct);

        if (contrato is null)
        {
            return Results.NotFound();
        }

        // O ano entra por parametro para a tela poder olhar o ano passado -
        // util para conferir o 13o que ja foi pago. Sem ele, o ano corrente.
        var alvo = ano ?? relogio.Agora.Year;

        ApuracaoAvos apuracao;

        try
        {
            apuracao = AvosDecimoTerceiro.Apurar(contrato, alvo);
        }
        catch (ArgumentOutOfRangeException erro)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["ano"] = [erro.Message.Split(" (Parameter")[0]]
            });
        }

        return Results.Ok(new AvosResposta(
            contrato.Id,
            contrato.Matricula,
            contrato.DataAdmissao,
            contrato.DataDesligamento,
            apuracao.Ano,
            apuracao.Avos,
            apuracao.Fracao,
            apuracao.AnoCompleto,
            [.. apuracao.Meses.Select(MesDoAvoResposta.De)]));
    }
}
