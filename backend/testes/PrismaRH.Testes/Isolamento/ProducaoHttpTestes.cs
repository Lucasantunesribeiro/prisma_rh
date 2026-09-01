using Microsoft.AspNetCore.Http;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using PrismaRH.Api.Producao;

namespace PrismaRH.Testes.Isolamento;

/// <summary>
/// O endurecimento da Fase 10, contra a API de verdade.
///
/// ## O que estes testes fecham
///
/// Quatro pendências que estavam registradas no `CLAUDE.md §24.19` desde
/// agosto, todas marcadas como **bloqueantes antes do primeiro deploy
/// público**: falta de rate limiting, listagens sem paginação, entrada
/// malformada devolvendo 500, e a ausência de cabeçalhos de segurança.
///
/// ⚠️ A fábrica sobe em **Development**. Isso limita o que dá para provar aqui:
/// o CSRF e o `SameSite=None` só valem fora de Development, e por isso a
/// verificação deles é feita contra a **produção publicada**, no navegador. O
/// que este arquivo cobre é a lógica pura da guarda mais o resto do gate.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class ProducaoHttpTestes(BancoPostgresFixture banco) : IDisposable
{
    private readonly FabricaApiIsolada _fabrica = new(banco.StringConexao);

    public void Dispose()
    {
        _fabrica.Dispose();
        GC.SuppressFinalize(this);
    }

    // ------------------------------------------- entrada malformada -> 400

    /// <summary>
    /// ⚠️ `CLAUDE.md §24.19 item 4`, aberta em 27/08/2026 na Fase 4D.
    ///
    /// Um enum desconhecido no corpo devolvia **500**. Não havia vazamento — o
    /// valor é rejeitado antes do domínio —, mas o cliente não distinguia "eu
    /// mandei errado" de "o servidor caiu", e um 500 recorrente mascara falha
    /// real no monitoramento.
    /// </summary>
    [Theory]
    [InlineData("{ isso nao e json")]
    [InlineData("{\"email\":")]
    [InlineData("[1,2,3]")]
    [InlineData("\"so um texto\"")]
    public async Task CorpoMalformadoDevolve400ENao500(string corpo)
    {
        using var cliente = _fabrica.CreateClient();

        using var resposta = await cliente.PostAsync(
            "/api/autenticacao/entrar",
            new StringContent(corpo, Encoding.UTF8, "application/json"),
            TestContext());

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    /// <summary>
    /// O detalhe do parser **não** volta para o cliente.
    ///
    /// A mensagem do `System.Text.Json` costuma incluir um trecho do JSON — que
    /// é entrada não confiável e pode conter dado pessoal. O cliente recebe o
    /// motivo em vocabulário próprio; o detalhe fica no log do servidor.
    /// </summary>
    [Fact]
    public async Task ARespostaDe400NaoDevolveDetalheDoParser()
    {
        using var cliente = _fabrica.CreateClient();

        using var resposta = await cliente.PostAsync(
            "/api/autenticacao/entrar",
            new StringContent("{\"email\": \"joao@exemplo.com\", quebrado", Encoding.UTF8, "application/json"),
            TestContext());

        var corpo = await resposta.Content.ReadAsStringAsync(TestContext());

        Assert.DoesNotContain("System.Text.Json", corpo, StringComparison.Ordinal);
        Assert.DoesNotContain("LineNumber", corpo, StringComparison.Ordinal);
        Assert.DoesNotContain("joao@exemplo.com", corpo, StringComparison.Ordinal);
        Assert.DoesNotContain("at ", corpo, StringComparison.Ordinal); // sem stack trace
    }

    // ------------------------------------------------ cabecalhos de seguranca

    [Fact]
    public async Task ARespostaTrazOsCabecalhosDeSeguranca()
    {
        using var cliente = _fabrica.CreateClient();

        using var resposta = await cliente.GetAsync("/health", TestContext());
        var h = resposta.Headers;

        Assert.Equal("nosniff", h.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", h.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", h.GetValues("Referrer-Policy").Single());

        var csp = h.GetValues("Content-Security-Policy").Single();

        Assert.Contains("default-src 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);

        // ⚠️ `unsafe-inline` numa CSP é o jeito mais comum de ter o cabeçalho
        // e nenhuma proteção. Esta API só devolve JSON: não há um único motivo
        // para permitir script.
        Assert.DoesNotContain("unsafe-inline", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-eval", csp, StringComparison.Ordinal);
    }

    /// <summary>
    /// HSTS **não** sai em Development, e isso é proposital: ele faria o
    /// navegador recusar HTTP em `localhost` por um ano, trancando o
    /// desenvolvimento de todos os projetos da máquina na mesma porta — e o
    /// efeito sobrevive a limpar o cache.
    /// </summary>
    [Fact]
    public async Task HstsNaoSaiEmDesenvolvimento()
    {
        using var cliente = _fabrica.CreateClient();

        using var resposta = await cliente.GetAsync("/health", TestContext());

        Assert.False(resposta.Headers.Contains("Strict-Transport-Security"));
    }

    // -------------------------------------------------------- rate limiting

    /// <summary>
    /// ⚠️ `CLAUDE.md §24.19 item 1`: nada impedia milhares de tentativas por
    /// minuto contra o login. Força bruta e credential stuffing estavam
    /// abertos.
    ///
    /// O limite é **por IP**, e não por e-mail — no login não há usuário ainda,
    /// e é isso que o atacante está tentando descobrir. Por e-mail, um script
    /// varreria mil endereços sem estourar limite nenhum.
    /// </summary>
    [Fact]
    public async Task LoginRepetidoBateNoLimitePorIp()
    {
        using var cliente = _fabrica.CreateClient();

        var recusadas = 0;

        for (var tentativa = 0; tentativa < 20; tentativa++)
        {
            using var resposta = await cliente.PostAsJsonAsync(
                "/api/autenticacao/entrar",
                new { email = $"ninguem{tentativa}@exemplo.com", senha = "errada" },
                TestContext());

            if (resposta.StatusCode == HttpStatusCode.TooManyRequests)
            {
                recusadas++;
            }
        }

        // 10 por minuto: as 10 primeiras passam (com 401), as outras 10 batem.
        Assert.Equal(10, recusadas);
    }

    // ------------------------------------------------------------ paginacao

    /// <summary>
    /// `CLAUDE.md §24.19 item 3`. Sem teto, uma listagem que cresce é vetor de
    /// exaustão — e num sistema multiempresa o custo cai sobre todos os
    /// tenants, não só sobre quem pediu.
    /// </summary>
    [Theory]
    [InlineData("/api/rubricas")]
    [InlineData("/api/cargos")]
    [InlineData("/api/folhas")]
    public async Task ListagemDevolveEnvelopePaginado(string rota)
    {
        using var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);

        var pagina = await cliente.GetFromJsonAsync<PaginaHttp<object>>(rota, TestContext());

        Assert.NotNull(pagina);
        Assert.True(pagina.Tamanho > 0);
        Assert.True(pagina.PaginaAtual >= 1);
    }

    /// <summary>
    /// Pedir mil não é erro: o valor é reduzido ao teto. Recusar com 400
    /// pareceria mais correto e seria pior — um cliente que pede 1000 quer os
    /// dados, não uma aula.
    /// </summary>
    [Fact]
    public async Task PedirTamanhoAbsurdoEGrampeadoNoTeto()
    {
        using var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);

        var pagina = await cliente.GetFromJsonAsync<PaginaHttp<object>>(
            "/api/rubricas?tamanho=100000", TestContext());

        Assert.Equal(Paginacao.TamanhoMaximo, pagina!.Tamanho);
    }

    [Theory]
    [InlineData("pagina=0")]
    [InlineData("pagina=-5")]
    [InlineData("tamanho=0")]
    [InlineData("tamanho=-1")]
    public async Task ValorInvalidoDePaginacaoEhNormalizadoEmVezDeEstourar(string consulta)
    {
        using var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);

        using var resposta = await cliente.GetAsync($"/api/rubricas?{consulta}", TestContext());

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    // ---------------------------------------------------------------- CSRF

    /// <summary>
    /// A lógica da guarda, exercitada direto — sem depender do ambiente.
    ///
    /// ⚠️ **Origem ausente é recusa.** Todo navegador moderno envia `Origin` em
    /// requisição cross-site com credenciais; aceitar a ausência criaria a
    /// brecha exata que um cliente não-navegador usaria.
    /// </summary>
    [Fact]
    public void OrigemAusenteERecusada()
    {
        var contexto = new DefaultHttpContext();

        Assert.Equal(RecusaCsrf.OrigemAusente, GuardaCsrf.Conferir(contexto, ["https://app.exemplo"]));
    }

    /// <summary>
    /// Comparação **exata**. `endsWith(".vercel.app")` aprovaria
    /// `ataque.vercel.app`, que qualquer um cria de graça.
    /// </summary>
    [Theory]
    [InlineData("https://ataque.exemplo")]
    [InlineData("https://app.exemplo.ataque.com")]
    [InlineData("http://app.exemplo")]
    [InlineData("null")]
    public void OrigemNaoPermitidaERecusada(string origem)
    {
        var contexto = new DefaultHttpContext();
        contexto.Request.Headers.Origin = origem;

        Assert.Equal(
            RecusaCsrf.OrigemNaoPermitida,
            GuardaCsrf.Conferir(contexto, ["https://app.exemplo"]));
    }

    [Fact]
    public void OrigemCertaSemCookieERecusada()
    {
        var contexto = ComOrigem();

        Assert.Equal(RecusaCsrf.SemCookie, GuardaCsrf.Conferir(contexto, ["https://app.exemplo"]));
    }

    [Fact]
    public void CookieSemCabecalhoERecusado()
    {
        var contexto = ComOrigem();
        contexto.Request.Headers.Cookie = $"{GuardaCsrf.Cookie}=abc123";

        Assert.Equal(RecusaCsrf.SemCabecalho, GuardaCsrf.Conferir(contexto, ["https://app.exemplo"]));
    }

    /// <summary>
    /// ⚠️ O ataque que o double submit impede.
    ///
    /// O site malicioso **consegue** fazer o navegador enviar o cookie — é o
    /// que define CSRF. O que ele não consegue é **ler** o valor para repetir
    /// no cabeçalho: a same-origin policy o impede. Um palpite não passa.
    /// </summary>
    [Fact]
    public void CabecalhoQueNaoBateComOCookieERecusado()
    {
        var contexto = ComOrigem();
        contexto.Request.Headers.Cookie = $"{GuardaCsrf.Cookie}=valor-verdadeiro";
        contexto.Request.Headers[GuardaCsrf.Cabecalho] = "palpite-do-atacante";

        Assert.Equal(RecusaCsrf.NaoConferem, GuardaCsrf.Conferir(contexto, ["https://app.exemplo"]));
    }

    [Fact]
    public void CookieECabecalhoIguaisSaoAceitos()
    {
        var token = GuardaCsrf.GerarToken();
        var contexto = ComOrigem();

        contexto.Request.Headers.Cookie = $"{GuardaCsrf.Cookie}={token}";
        contexto.Request.Headers[GuardaCsrf.Cabecalho] = token;

        Assert.Equal(RecusaCsrf.Aceita, GuardaCsrf.Conferir(contexto, ["https://app.exemplo"]));
    }

    /// <summary>
    /// Token com 32 bytes de aleatoriedade criptográfica. Dois nunca se
    /// repetem — se repetissem, o atacante poderia reusar um valor visto.
    /// </summary>
    [Fact]
    public void CadaTokenGeradoEDiferente()
    {
        var tokens = Enumerable.Range(0, 200).Select(_ => GuardaCsrf.GerarToken()).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(200, tokens.Count);
        Assert.All(tokens, t => Assert.True(t.Length >= 40));
    }

    private static DefaultHttpContext ComOrigem()
    {
        var contexto = new DefaultHttpContext();
        contexto.Request.Headers.Origin = "https://app.exemplo";
        return contexto;
    }

    private static CancellationToken TestContext() => CancellationToken.None;
}
