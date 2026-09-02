using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PrismaRH.Testes.Isolamento;

namespace PrismaRH.Testes.Seguranca;

/// <summary>
/// O par do *double submit* volta **no corpo**, e o cookie é `HttpOnly`.
///
/// ## O defeito que isto corrige
///
/// ⚠️ Descoberto em **02/09/2026**, recarregando a página em produção: a sessão
/// caía para a tela de login, e `POST /renovar` respondia **403**.
///
/// O frontend lia o token com `document.cookie`. Em desenvolvimento tudo é
/// `localhost` e funciona; em produção a tela está em
/// `portfolio-prisma-rh.vercel.app` e a API em `*.lambda-url.…on.aws`, e
/// `document.cookie` é **por origem**. A página nunca enxergou o cookie da
/// API — então o cabeçalho `X-CSRF-Token` nunca era enviado.
///
/// **A guarda CSRF estava correta e a tela é que não conseguia satisfazê-la.**
///
/// ## Por que a suíte não pegou
///
/// Os testes de front rodam em `jsdom`, que é *same-origin*: o cookie escrito
/// pelo teste era lido pelo mesmo código. O ambiente de teste não reproduzia a
/// topologia que produção tem — e essa é a lição, não o defeito em si.
///
/// Este teste fecha o contrato pelo lado que não depende de navegador: o
/// servidor **entrega** o token onde a tela consegue pegá-lo.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class TokenCsrfNoCorpoTestes(BancoPostgresFixture banco)
{
    private async Task<JsonElement> EntrarAsync(HttpClient cliente)
    {
        using var r = await cliente.PostAsJsonAsync(
            "/api/autenticacao/entrar",
            new { email = BancoPostgresFixture.EmailAdminA, senha = BancoPostgresFixture.Senha });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var corpo = await r.Content.ReadFromJsonAsync<JsonElement>();

        // Guarda os cookies da resposta para a conferencia do teste seguinte.
        UltimosCookies = r.Headers.TryGetValues("Set-Cookie", out var v) ? [.. v] : [];

        return corpo;
    }

    private string[] UltimosCookies { get; set; } = [];

    /// <summary>⚠️ **O teste que o defeito exigia.**</summary>
    [Fact]
    public async Task EntrarDevolveOTokenCsrfNoCorpo()
    {
        var fabrica = new FabricaApiIsolada(banco.StringConexao);
        using var cliente = fabrica.CreateClient();

        var corpo = await EntrarAsync(cliente);

        Assert.True(
            corpo.TryGetProperty("tokenCsrf", out var token),
            "Sem `tokenCsrf` no corpo, a tela em outro dominio nao tem como montar "
            + "o cabecalho X-CSRF-Token, e `renovar` responde 403 para sempre.");

        Assert.False(string.IsNullOrWhiteSpace(token.GetString()));
    }

    /// <summary>
    /// O valor do corpo é **o mesmo** do cookie. Se divergissem, o double
    /// submit nunca casaria — e o teste anterior sozinho não perceberia.
    /// </summary>
    [Fact]
    public async Task OTokenDoCorpoEOMesmoDoCookie()
    {
        var fabrica = new FabricaApiIsolada(banco.StringConexao);
        using var cliente = fabrica.CreateClient();

        var corpo = await EntrarAsync(cliente);
        var doCorpo = corpo.GetProperty("tokenCsrf").GetString();

        var doCookie = UltimosCookies
            .FirstOrDefault(c => c.StartsWith("prismarh_csrf=", StringComparison.Ordinal))
            ?.Split(';')[0]["prismarh_csrf=".Length..];

        Assert.Equal(doCookie, doCorpo);
    }

    /// <summary>
    /// ⚠️ O cookie ficou **mais** restrito: como o script não precisa mais
    /// lê-lo, ele passou a ser `HttpOnly`. Este teste impede que alguém o
    /// reabra "para o front conseguir ler" — o front já tem o valor.
    /// </summary>
    [Fact]
    public async Task OCookieCsrfEHttpOnly()
    {
        var fabrica = new FabricaApiIsolada(banco.StringConexao);
        using var cliente = fabrica.CreateClient();

        await EntrarAsync(cliente);

        var cookie = Assert.Single(
            UltimosCookies, c => c.StartsWith("prismarh_csrf=", StringComparison.Ordinal));

        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>O refresh continua inacessível ao script — nada mudou nele.</summary>
    [Fact]
    public async Task OCookieDeRefreshContinuaHttpOnly()
    {
        var fabrica = new FabricaApiIsolada(banco.StringConexao);
        using var cliente = fabrica.CreateClient();

        await EntrarAsync(cliente);

        var cookie = Assert.Single(
            UltimosCookies, c => c.StartsWith("prismarh_refresh=", StringComparison.Ordinal));

        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
    }
}
