using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace PrismaRH.Testes.Isolamento;

/// <summary>
/// ⚠️ **A prova que autoriza publicar a credencial da demonstração.**
///
/// ## Por que este teste existe
///
/// A tela de login do portfólio expõe, de propósito, o e-mail e a senha da
/// conta `visualizador@prisma.exemplo`, para que um recrutador entre sem
/// pedir acesso a ninguém. Qualquer `VITE_*` termina público no bundle — a
/// senha da demonstração é, na prática, publicada.
///
/// Isso só é aceitável enquanto uma frase for verdadeira: **esta conta não
/// escreve nada.** Este teste é o que sustenta a frase. Se ele cair, a
/// credencial pública deixou de ser segura e o botão da demonstração precisa
/// sair do ar antes de qualquer outra coisa.
///
/// ## Por que a lista de rotas é DESCOBERTA, e não escrita à mão
///
/// Uma lista escrita à mão prova o passado. Ela nasce completa e envelhece na
/// primeira rota nova — e a rota nova é justamente a que ninguém lembrou de
/// conferir.
///
/// Aqui as rotas vêm do `EndpointDataSource`, que é a mesma fonte que o
/// roteamento usa em produção. **Endpoint de escrita criado amanhã entra neste
/// teste sozinho** e, se ficar aberto ao Visualizador, o teste quebra sem que
/// ninguém precise se lembrar dele.
///
/// ## Por que 403 é determinístico aqui
///
/// A autorização roda **antes** do binding do corpo. Um `POST` sem corpo
/// nenhum, com token de Visualizador, nunca chega ao código da rota: ou a
/// política recusa com 403, ou a rota está aberta a este perfil — que é
/// exatamente o que se quer detectar. Por isso o teste não monta payload
/// válido para 60 rotas: o corpo é irrelevante para a pergunta.
///
/// ## O que a lista de exceções significa
///
/// Só as rotas **anônimas de autenticação**. Elas são abertas por desenho
/// (`entrar`, `renovar`, `sair`) e não tocam dado de tenant. Toda outra rota
/// de escrita precisa recusar.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class VisualizadorSomenteLeituraTestes(BancoPostgresFixture banco)
{
    private static readonly string[] MetodosDeEscrita = ["POST", "PUT", "PATCH", "DELETE"];

    /// <summary>Rotas anônimas por desenho. Não tocam dado de organização.</summary>
    private static readonly string[] AnonimasPorDesenho =
    [
        "api/autenticacao/entrar",
        "api/autenticacao/renovar",
        "api/autenticacao/sair",
    ];

    /// <summary>
    /// ⚠️ **`POST` que não escreve.** As duas rotas abaixo usam `POST` porque
    /// carregam uma pergunta no corpo, e não porque alteram dado.
    ///
    /// A camada de IA é **de leitura** por decisão permanente (`CLAUDE.md
    /// §37.4`): *"Nenhum caminho de código iniciado por resposta de modelo pode
    /// terminar em escrita no banco."* Um resumo executivo é para **quem lê** a
    /// folha, e o Visualizador lê.
    ///
    /// Ficam nomeadas uma a uma, e não por prefixo `api/assistente`, de
    /// propósito: `POST /api/assistente/inconsistencias/{id}/explicacao` exige
    /// `ProcessarFolha` e **recusa** o Visualizador. Um prefixo teria
    /// engolido essa distinção — e teria engolido qualquer rota de assistente
    /// criada depois.
    ///
    /// O controle de custo delas é o limite por organização, verificado no
    /// teste seguinte.
    /// </summary>
    private static readonly string[] LeiturasComCorpo =
    [
        "api/assistente/folhas/{id:guid}/resumo",
        "api/assistente/consultas",
    ];

    /// <summary>
    /// Troca `{id:guid}` por um GUID e `{codigo}` por um texto qualquer.
    ///
    /// O valor não importa: nenhum recurso precisa existir para a autorização
    /// decidir. Um GUID inexistente que devolve 403 prova mais que um id real —
    /// mostra que a recusa não dependeu de o dado estar lá.
    /// </summary>
    private static string Concretizar(string padrao)
    {
        var partes = padrao.Trim('/').Split('/');

        for (var i = 0; i < partes.Length; i++)
        {
            if (!partes[i].StartsWith('{'))
            {
                continue;
            }

            partes[i] = partes[i].Contains("guid", StringComparison.OrdinalIgnoreCase)
                ? Guid.CreateVersion7().ToString()
                : "valor";
        }

        return "/" + string.Join('/', partes);
    }

    [Fact]
    public async Task NenhumaRotaDeEscritaAceitaOVisualizador()
    {
        var fabrica = new FabricaApiIsolada(banco.StringConexao);
        var cliente = await fabrica.ClienteComoAsync(BancoPostgresFixture.EmailVisualizadorA);

        var fontes = fabrica.Services.GetRequiredService<IEnumerable<EndpointDataSource>>();

        var rotas = fontes
            .SelectMany(f => f.Endpoints)
            .OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Where(m => MetodosDeEscrita.Contains(m))
                .Select(m => (
                    Metodo: m,
                    // `RawText` as vezes traz a barra inicial e as vezes nao,
                    // conforme o grupo. Normalizar aqui evita um filtro que
                    // silenciosamente nao casa com nada - foi o que aconteceu na
                    // primeira versao, e so a guarda de contagem revelou.
                    Padrao: (e.RoutePattern.RawText ?? string.Empty).Trim('/'))))
            .Where(r => r.Padrao.StartsWith("api/", StringComparison.Ordinal))
            .Where(r => !AnonimasPorDesenho.Contains(r.Padrao))
            .Where(r => !LeiturasComCorpo.Contains(r.Padrao))
            .Distinct()
            .OrderBy(r => r.Padrao, StringComparer.Ordinal)
            .ToList();

        // Se a descoberta falhar em silencio, o teste passaria sem testar nada.
        Assert.True(rotas.Count >= 20, $"Descobri so {rotas.Count} rotas de escrita.");

        var permitidas = new List<string>();

        foreach (var (metodo, padrao) in rotas)
        {
            using var requisicao = new HttpRequestMessage(
                new HttpMethod(metodo), Concretizar(padrao));

            using var resposta = await cliente.SendAsync(requisicao);

            if (resposta.StatusCode != HttpStatusCode.Forbidden)
            {
                permitidas.Add($"{metodo} {padrao} -> {(int)resposta.StatusCode}");
            }
        }

        Assert.True(
            permitidas.Count == 0,
            "⚠️ A conta publica da demonstracao alcanca escrita. Tire o botao do ar "
            + "antes de qualquer outra coisa:\n  " + string.Join("\n  ", permitidas));
    }

    /// <summary>
    /// O outro lado: ela **precisa** conseguir ler, senão a demonstração não
    /// demonstra nada.
    /// </summary>
    [Theory]
    [InlineData("/api/empresas")]
    [InlineData("/api/funcionarios")]
    [InlineData("/api/folhas")]
    [InlineData("/api/rubricas")]
    [InlineData("/api/inconsistencias")]
    [InlineData("/api/auditoria")]
    [InlineData("/api/painel")]
    public async Task OVisualizadorLeOQueADemonstracaoPrecisaMostrar(string rota)
    {
        var fabrica = new FabricaApiIsolada(banco.StringConexao);
        var cliente = await fabrica.ClienteComoAsync(BancoPostgresFixture.EmailVisualizadorA);

        using var resposta = await cliente.GetAsync(rota);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    /// <summary>
    /// As duas leituras-com-corpo custam dinheiro por chamada, e a conta da
    /// demonstração é pública. Sem limite, o botão de demonstração seria um
    /// botão de gastar (`CLAUDE.md §37.9`, *custo abusivo*).
    ///
    /// Este teste trava a existência do limite; os números dele são
    /// responsabilidade do `OrcamentoIa`.
    /// </summary>
    [Fact]
    public void ToraRotaDeIaQueCustaTemLimiteDeclarado()
    {
        var fabrica = new FabricaApiIsolada(banco.StringConexao);

        var fontes = fabrica.Services.GetRequiredService<IEnumerable<EndpointDataSource>>();

        var semLimite = fontes
            .SelectMany(f => f.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(e => (e.RoutePattern.RawText ?? string.Empty)
                .Trim('/')
                .StartsWith("api/assistente", StringComparison.Ordinal))
            .Where(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Any(m => MetodosDeEscrita.Contains(m)))
            .Where(e => e.Metadata.GetMetadata<
                Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>() is null)
            .Select(e => e.RoutePattern.RawText)
            .ToList();

        Assert.True(
            semLimite.Count == 0,
            "Rota de IA que gasta token sem limite: " + string.Join(", ", semLimite));
    }
}
