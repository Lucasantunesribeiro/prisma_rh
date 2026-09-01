using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PrismaRH.Testes.Isolamento;

namespace PrismaRH.Testes.Seguranca;

/// <summary>
/// Varredura de IDOR/BOLA (Fase 12).
///
/// ## A ameaça
///
/// *Broken Object Level Authorization* é a falha nº 1 do OWASP API Security, e
/// o `CLAUDE.md §24.6` diz por que ela é a mais provável aqui:
///
/// > *"Saber que `/api/funcionarios/{id}` existe não é vulnerabilidade.
/// > Devolver o funcionário de outra organização é."*
///
/// ## Por que uma varredura, e não testes um a um
///
/// Os testes de isolamento existentes provam recursos específicos. O problema é
/// o **recurso número 43**, criado daqui a dois meses, cuja rota ninguém lembrou
/// de cobrir.
///
/// Esta varredura enumera as rotas `{id:guid}` **da aplicação rodando** e bate
/// em todas com um identificador que não pertence a ninguém. Rota nova entra na
/// varredura sozinha.
///
/// ## O que se exige de cada resposta
///
/// | Resposta | Veredito |
/// |---|---|
/// | **404** | Correto. E é 404, não 403, de propósito: um 403 confirmaria que aquele id existe, e permitiria mapear os dados do vizinho um id por vez (`§24.5`). |
/// | **400** | Aceitável — a rota recusou o formato antes de procurar. |
/// | **200** | ⚠️ Falha. Devolveu algo para um id de ninguém. |
/// | **500** | ⚠️ Falha. Id inexistente é caso comum, não erro do servidor — e um 500 recorrente mascara falha real no monitoramento. |
/// </summary>
[Collection(ColecaoApi.Nome)]
public class VarreduraIdorTestes(BancoPostgresFixture banco)
{
    /// <summary>
    /// Rotas que **não** procuram um recurso por id, e por isso não entram.
    ///
    /// Lista curta e justificada de propósito: cada linha aqui é um pedaço da
    /// varredura que deixa de existir.
    /// </summary>
    private static readonly string[] ForaDaVarredura =
    [
        // Documento OpenAPI: o parametro nao e um recurso de tenant.
        "openapi",
    ];

    private static readonly Regex ParametroGuid =
        new(@"\{[A-Za-z]+:guid\}", RegexOptions.Compiled);

    private static readonly Regex QualquerParametro =
        new(@"\{[^}]+\}", RegexOptions.Compiled);

    private sealed record RotaDeLeitura(string Caminho, string Padrao);

    private static List<RotaDeLeitura> LeiturasComId(FabricaApiIsolada fabrica)
    {
        var rotas = new List<RotaDeLeitura>();

        foreach (var rota in fabrica.Services
                     .GetRequiredService<EndpointDataSource>()
                     .Endpoints
                     .OfType<RouteEndpoint>())
        {
            var padrao = rota.RoutePattern.RawText ?? string.Empty;

            var metodos = rota.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;

            if (metodos is null || !metodos.Contains("GET"))
            {
                continue;
            }

            if (!padrao.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
                || !ParametroGuid.IsMatch(padrao)
                || ForaDaVarredura.Any(f => padrao.Contains(f, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            // Troca cada {x:guid} por um id novo. Um padrao que sobrar com
            // parametro nao-guid nao da para chamar as cegas: fica de fora.
            var caminho = ParametroGuid.Replace(padrao, _ => Guid.CreateVersion7().ToString());

            if (QualquerParametro.IsMatch(caminho))
            {
                continue;
            }

            rotas.Add(new RotaDeLeitura(caminho, padrao));
        }

        return rotas;
    }

    /// <summary>
    /// ⚠️ **A varredura.**
    ///
    /// Um usuário legítimo, autenticado, pedindo recursos que não são de
    /// ninguém. Nenhum pode responder 200, e nenhum pode explodir.
    /// </summary>
    [Fact]
    public async Task NenhumaRotaDeLeituraDevolveRecursoDeIdDesconhecido()
    {
        using var fabrica = new FabricaApiIsolada(banco.StringConexao);
        using var cliente = await fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);

        var rotas = LeiturasComId(fabrica);

        // Se a varredura parar de achar rotas, ela vira um teste que passa
        // sempre sem exercitar nada.
        Assert.True(rotas.Count >= 8, $"A varredura so encontrou {rotas.Count} rotas.");

        var problemas = new List<string>();

        foreach (var rota in rotas)
        {
            using var resposta = await cliente.GetAsync(rota.Caminho);

            var aceitavel = resposta.StatusCode
                is HttpStatusCode.NotFound
                or HttpStatusCode.BadRequest;

            if (!aceitavel)
            {
                problemas.Add($"{rota.Padrao} -> {(int)resposta.StatusCode}");
            }
        }

        Assert.True(
            problemas.Count == 0,
            "Rota devolveu algo diferente de 404/400 para id de ninguem: "
            + string.Join(" | ", problemas));
    }

    /// <summary>
    /// ⚠️ O mesmo, mas com o perfil **mais fraco** do sistema.
    ///
    /// Um Visualizador de uma organização pedindo id de ninguém não pode receber
    /// 200 nem 500 — e também não pode receber uma resposta **diferente** da do
    /// Administrador, porque a diferença entre 403 e 404 já é informação.
    /// </summary>
    [Fact]
    public async Task OVisualizadorRecebeAMesmaRespostaQueOAdministradorParaIdDeNinguem()
    {
        using var fabrica = new FabricaApiIsolada(banco.StringConexao);
        using var admin = await fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        using var visualizador = await fabrica.ClienteComoAsync(
            BancoPostgresFixture.EmailVisualizadorA);

        var divergentes = new List<string>();

        foreach (var rota in LeiturasComId(fabrica))
        {
            using var doAdmin = await admin.GetAsync(rota.Caminho);
            using var doVisualizador = await visualizador.GetAsync(rota.Caminho);

            // 403 e legitimo: ha rota de leitura restrita a perfil. O que nao
            // pode e o Visualizador receber 200 ou 500 onde o admin recebe 404.
            var aceitavel = doVisualizador.StatusCode == doAdmin.StatusCode
                || doVisualizador.StatusCode == HttpStatusCode.Forbidden;

            if (!aceitavel)
            {
                divergentes.Add(
                    $"{rota.Padrao}: admin={(int)doAdmin.StatusCode} "
                    + $"visualizador={(int)doVisualizador.StatusCode}");
            }
        }

        Assert.True(divergentes.Count == 0, string.Join(" | ", divergentes));
    }

    /// <summary>
    /// ⚠️ Id **malformado** não pode virar 500.
    ///
    /// A restrição `:guid` na rota faz o roteador recusar antes do código — o
    /// resultado esperado é 404 do próprio roteamento. Uma rota que aceitasse
    /// `{id}` solto entregaria a string ao código, e um `Guid.Parse` estouraria.
    /// </summary>
    [Theory]
    [InlineData("nao-e-guid")]
    [InlineData("00000000-0000-0000-0000-00000000000")]
    [InlineData("' OR 1=1 --")]
    public async Task IdMalformadoNaoViraErroDeServidor(string idTorto)
    {
        using var fabrica = new FabricaApiIsolada(banco.StringConexao);
        using var cliente = await fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);

        foreach (var caminho in new[]
                 {
                     $"/api/empresas/{Uri.EscapeDataString(idTorto)}",
                     $"/api/funcionarios/{Uri.EscapeDataString(idTorto)}",
                     $"/api/folhas/{Uri.EscapeDataString(idTorto)}",
                     $"/api/inconsistencias/{Uri.EscapeDataString(idTorto)}",
                 })
        {
            using var resposta = await cliente.GetAsync(caminho);

            Assert.True(
                resposta.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest,
                $"{caminho} devolveu {(int)resposta.StatusCode}");
        }
    }
}
