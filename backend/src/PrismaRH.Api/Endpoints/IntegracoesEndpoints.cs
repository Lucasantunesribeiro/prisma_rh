using Microsoft.EntityFrameworkCore;
using PrismaRH.Api.Identidade;
using PrismaRH.Aplicacao.Comum;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Dominio.Auditoria;
using PrismaRH.Dominio.Empresas;
using PrismaRH.Infraestrutura.Auditoria;
using PrismaRH.Infraestrutura.Integracoes;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Api.Endpoints;

/// <summary>
/// O que o cliente envia: quatorze digitos, e nada mais.
///
/// Nao ha campo de URL, de host nem de provedor. O destino e decidido pelo
/// servidor, a partir de uma allowlist fixa em codigo - e por isso o vetor
/// classico de SSRF nao existe nesta rota.
/// </summary>
public sealed record ConsultarCnpjRequisicao(string Cnpj);

public sealed record DadosDaReceita(
    string RazaoSocial,
    string? NomeFantasia,
    string SituacaoCadastral,
    bool AtivaNaReceita);

public sealed record ConsultaCnpjResposta(
    string Situacao,
    string Mensagem,
    DadosDaReceita? Dados,
    bool JaCadastrada);

/// <summary>
/// Consulta de empresa por CNPJ na BrasilAPI (Fase 8).
///
/// ## Por que `POST` para uma leitura
///
/// Duas razoes, e nenhuma delas e purismo REST:
///
/// 1. **O CNPJ nao entra na URL.** URL vai para log de acesso, historico de
///    navegador, Referer e painel de proxy - lugares com retencao e acesso
///    diferentes dos do banco (`CLAUDE.md secao 24.13`). No corpo, ele fica
///    onde a autorizacao alcanca.
/// 2. **A chamada tem efeito.** Ela sai da nossa rede, conta na cota de um
///    servico gratuito de terceiro e gera evento de auditoria. `GET` promete
///    que nada disso acontece.
///
/// ## O que esta rota nao faz
///
/// Nao cria empresa. Nao altera empresa. **Nao escreve nada** - alem do proprio
/// registro de auditoria de que a pergunta foi feita. O que ela devolve vai
/// para a tela, e a pessoa decide. `CLAUDE.md secao 1`: o Prisma RH nao depende
/// de outro sistema para funcionar, e com a BrasilAPI fora do ar o cadastro
/// manual continua igual.
/// </summary>
public static class IntegracoesEndpoints
{
    /// <summary>
    /// Nome da politica de limite. O limite e **por organizacao**, e nao por IP:
    /// `CLAUDE.md secao 24.18` - nenhuma organizacao pode consumir a cota de um
    /// servico compartilhado e deixar as outras sem. Por IP nao serviria: num
    /// escritorio de BPO, todo mundo sai pelo mesmo endereco.
    /// </summary>
    public const string PoliticaLimite = "consulta-cnpj";

    public static IEndpointRouteBuilder MapearIntegracoes(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/api/integracoes").WithTags("Integracoes");

        grupo.MapPost("/cnpj/consultas", ConsultarAsync)
            .WithSummary("Consulta razao social e nome fantasia pelo CNPJ, na Receita Federal")
            // Quem cadastra empresa. A consulta so serve ao formulario de
            // empresa, entao nao ha motivo para dar a mais ninguem
            // (`CLAUDE.md secao 24.4`, menor privilegio).
            .RequireAuthorization(PoliticasAutorizacao.AdministrarEmpresas)
            .RequireRateLimiting(PoliticaLimite);

        return rotas;
    }

    private static async Task<IResult> ConsultarAsync(
        ConsultarCnpjRequisicao requisicao,
        PrismaRhDbContext db,
        ConsultaCnpjBrasilApi parceiro,
        CacheConsultaCnpj cache,
        IContextoUsuario usuario,
        IRelogio relogio,
        CancellationToken ct)
    {
        // A conferencia dos digitos acontece AQUI, antes de qualquer chamada
        // externa. Nao e so cortesia com a cota alheia: e o que garante que so
        // um `Cnpj` valido - quatorze digitos - chega perto da montagem da URL.
        if (!Cnpj.TentarCriar(requisicao?.Cnpj, out var cnpj))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["cnpj"] = ["CNPJ invalido."],
            });
        }

        // Identificador desta consulta. Vai para o log tecnico e para a trilha
        // de auditoria, e e o que costura as duas: partindo do evento se acha a
        // linha do log, e vice-versa.
        var correlacao = Guid.CreateVersion7();

        var (resultado, doCache) = await cache.ObterAsync(
            cnpj,
            () => parceiro.ConsultarAsync(cnpj, correlacao, ct));

        // Sob o filtro global: responde "ja existe NESTA organizacao", e nunca
        // "existe em alguma". A empresa da vizinha nao aparece nem como "ja
        // cadastrada" - isso, sozinho, ja contaria quem sao os clientes dela.
        var jaCadastrada = await db.Empresas.AnyAsync(e => e.Cnpj == cnpj, ct);

        db.Registrar(
            usuario,
            relogio,
            AcaoAuditada.CnpjConsultado,
            EntidadeAuditada.ConsultaCnpj,
            correlacao,
            doCache
                ? $"Consulta de CNPJ {cnpj.Formatado} respondida pelo cache: {Rotulo(resultado.Situacao)}."
                : $"Consulta de CNPJ {cnpj.Formatado} na Receita Federal: {Rotulo(resultado.Situacao)}.",
            $"cnpj={cnpj.Valor};situacao={resultado.Situacao};origem={(doCache ? "cache" : "brasilapi")}");

        await db.SaveChangesAsync(ct);

        return Results.Ok(new ConsultaCnpjResposta(
            resultado.Situacao.ToString(),
            resultado.Mensagem,
            resultado.Empresa is null
                ? null
                : new DadosDaReceita(
                    resultado.Empresa.RazaoSocial,
                    resultado.Empresa.NomeFantasia,
                    resultado.Empresa.SituacaoCadastral,
                    string.Equals(resultado.Empresa.SituacaoCadastral, "ATIVA", StringComparison.OrdinalIgnoreCase)),
            jaCadastrada));
    }

    private static string Rotulo(SituacaoConsulta situacao) => situacao switch
    {
        SituacaoConsulta.Encontrada => "encontrado",
        SituacaoConsulta.NaoEncontrada => "nao encontrado",
        SituacaoConsulta.Recusada => "recusado pela Receita",
        _ => "provedor indisponivel",
    };
}
