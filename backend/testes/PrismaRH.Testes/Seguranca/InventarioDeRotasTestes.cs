using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PrismaRH.Testes.Isolamento;

namespace PrismaRH.Testes.Seguranca;

/// <summary>
/// O inventário de rotas (Fase 12).
///
/// ## Por que este teste existe
///
/// `CLAUDE.md §24.4`: **"Negar por padrão. Rota sem política declarada é erro de
/// implementação, não rota liberada."**
///
/// O Security Gate da Fase 12, item 10, pede *"inventário final de rotas
/// anônimas"*. Um inventário escrito num documento envelhece na primeira rota
/// nova. Este é executável: ele lê o `EndpointDataSource` **da aplicação
/// rodando**, e não o código-fonte.
///
/// A diferença importa. Um `grep` por `RequireAuthorization` acha a chamada;
/// não acha a rota onde alguém esqueceu de chamá-la — que é exatamente o caso
/// perigoso.
///
/// ## O que ele trava
///
/// 1. **rota nova sem política** falha o teste;
/// 2. **rota nova anônima** falha o teste, a menos que entre na lista abaixo —
///    e entrar na lista é um diff que alguém lê.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class InventarioDeRotasTestes(BancoPostgresFixture banco)
{
    /// <summary>
    /// ⚠️ **As únicas rotas anônimas do produto.** Cada uma com o motivo.
    ///
    /// Esta lista é o inventário do item 10 do gate. Acrescentar uma linha aqui
    /// é uma decisão visível em revisão — que é o ponto.
    /// </summary>
    private static readonly Dictionary<string, string> AnonimasPermitidas = new()
    {
        ["POST /api/autenticacao/entrar"] =
            "Login. Nao ha usuario ainda - e o que ele esta tentando provar. "
            + "Protegido por limite de 10/min por IP e por resposta de tempo constante.",

        ["POST /api/autenticacao/renovar"] =
            "Renovacao pelo refresh em cookie httpOnly. O portador do cookie E a "
            + "credencial; exigir o access token aqui impediria renovar o que expirou.",

        ["POST /api/autenticacao/sair"] =
            "Encerrar sessao nunca deve falhar por token expirado - senao o "
            + "refresh sobrevive a uma tentativa legitima de sair.",

        ["/health"] =
            "Sonda de disponibilidade. Fora de Development a resposta e minima, "
            + "sem nomear verificacao nenhuma: numa rota anonima, dizer que ha um "
            + "banco e que ele responde e informacao gratuita para quem sonda.",
    };

    private static IEnumerable<RouteEndpoint> Rotas(FabricaApiIsolada fabrica) =>
        fabrica.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>();

    private static string Assinatura(RouteEndpoint rota)
    {
        var metodos = rota.Metadata
            .GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?
            .HttpMethods;

        var caminho = "/" + rota.RoutePattern.RawText?.TrimStart('/');

        return metodos is { Count: > 0 }
            ? $"{string.Join(",", metodos)} {caminho}"
            : caminho;
    }

    /// <summary>
    /// ⚠️ **O teste central desta fase.**
    ///
    /// Rota que declara `AllowAnonymous` é a única que o mundo alcança sem
    /// credencial. A lista acima é o inventário do item 10 do gate, e este
    /// teste é o que impede a lista de ficar desatualizada.
    /// </summary>
    [Fact]
    public void NenhumaRotaEAnonimaAlemDoInventario()
    {
        using var fabrica = new FabricaApiIsolada(banco.StringConexao);

        var forasteiras = Rotas(fabrica)
            .Where(r => r.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(Assinatura)
            .Where(a => !AnonimasPermitidas.Keys.Any(
                k => a.Equals(k, StringComparison.OrdinalIgnoreCase)
                    || a.EndsWith(k, StringComparison.OrdinalIgnoreCase)))
            .Order()
            .ToList();

        Assert.True(
            forasteiras.Count == 0,
            "Rota anonima fora do inventario: " + string.Join(" | ", forasteiras));
    }

    /// <summary>
    /// ⚠️ Toda rota de negócio declara política **explícita**.
    ///
    /// A `FallbackPolicy` garante que uma rota esquecida devolva 401 em vez de
    /// abrir — mas ela só exige **usuário autenticado**, não o perfil certo.
    /// Uma rota de negócio protegida apenas pelo fallback estaria aberta a
    /// qualquer usuário logado, inclusive Visualizador.
    ///
    /// Por isso as duas defesas: o fallback é a rede, este teste é o piso.
    /// </summary>
    [Fact]
    public void TodaRotaDeNegocioDeclaraPoliticaExplicita()
    {
        using var fabrica = new FabricaApiIsolada(banco.StringConexao);

        var semPolitica = Rotas(fabrica)
            .Where(r => (r.RoutePattern.RawText ?? string.Empty)
                .StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            .Where(r => r.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .Where(r => r.Metadata.GetMetadata<IAuthorizeData>() is null)
            .Select(Assinatura)
            .Order()
            .ToList();

        Assert.True(
            semPolitica.Count == 0,
            "Rota de negocio sem politica explicita - so o fallback a protege: "
            + string.Join(" | ", semPolitica));
    }

    /// <summary>
    /// O documento OpenAPI descreve as 85 rotas, com os esquemas de entrada.
    /// Numa rota pública, isso é o mapa do sistema entregue a qualquer
    /// varredura — por isso ele **só existe em Development**, e mesmo lá exige
    /// autenticação por causa da `FallbackPolicy`.
    /// </summary>
    [Fact]
    public void ODocumentoOpenApiNaoEAnonimo()
    {
        using var fabrica = new FabricaApiIsolada(banco.StringConexao);

        var openapi = Rotas(fabrica)
            .Where(r => (r.RoutePattern.RawText ?? string.Empty)
                .Contains("openapi", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.All(openapi, r =>
            Assert.Null(r.Metadata.GetMetadata<IAllowAnonymous>()));
    }

    /// <summary>
    /// O caminho inverso: uma rota que **saiu** do produto não pode continuar
    /// na lista de anônimas. Inventário que só cresce vira lista de exceções que
    /// ninguém confere.
    /// </summary>
    [Fact]
    public void OInventarioDeAnonimasNaoTemLinhaMorta()
    {
        using var fabrica = new FabricaApiIsolada(banco.StringConexao);

        var existentes = Rotas(fabrica).Select(Assinatura).ToList();

        foreach (var permitida in AnonimasPermitidas.Keys)
        {
            Assert.True(
                existentes.Any(a => a.Equals(permitida, StringComparison.OrdinalIgnoreCase)
                    || a.EndsWith(permitida, StringComparison.OrdinalIgnoreCase)),
                $"'{permitida}' esta no inventario de anonimas mas nao existe mais.");
        }
    }

    /// <summary>
    /// Toda rota anônima precisa de **motivo escrito**. Um inventário de
    /// caminhos sem justificativa não responde a pergunta que ele existe para
    /// responder: *por que isto é público?*
    /// </summary>
    [Fact]
    public void TodaAnonimaTemMotivoEscrito()
    {
        foreach (var (rota, motivo) in AnonimasPermitidas)
        {
            Assert.True(
                motivo.Length > 60,
                $"'{rota}' precisa de um motivo de verdade, nao de um rotulo.");
        }
    }

    /// <summary>
    /// ⚠️ **Falha fechada.**
    ///
    /// Sem `FallbackPolicy`, uma rota que esquecesse `RequireAuthorization`
    /// nasceria **anônima** — o oposto do `CLAUDE.md §24.4`. O teste acima
    /// pegaria isso, mas só na execução da suíte; a política de fallback pega
    /// em produção, que é onde importa.
    ///
    /// As duas defesas convivem de propósito: a política protege o sistema, o
    /// teste protege a política de ser removida.
    /// </summary>
    [Fact]
    public void ExisteFallbackPolicyExigindoUsuarioAutenticado()
    {
        using var fabrica = new FabricaApiIsolada(banco.StringConexao);

        var opcoes = fabrica.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthorizationOptions>>()
            .Value;

        Assert.NotNull(opcoes.FallbackPolicy);

        Assert.Contains(
            opcoes.FallbackPolicy!.Requirements,
            r => r is DenyAnonymousAuthorizationRequirement);
    }
}
