using PrismaRH.Api.Producao;
using Microsoft.AspNetCore.Mvc;
using PrismaRH.Aplicacao.Identidade;

namespace PrismaRH.Api.Endpoints;

public sealed record EntrarRequisicao(string Email, string Senha);

public sealed record SessaoResposta(
    string AccessToken,
    DateTimeOffset ExpiraEm,
    UsuarioAutenticado Usuario);

public static class AutenticacaoEndpoints
{
    /// <summary>
    /// Nome do cookie que carrega o refresh token. O valor NUNCA volta no corpo
    /// da resposta: se voltasse, o JavaScript da pagina poderia le-lo e o
    /// httpOnly perderia a razao de existir.
    /// </summary>
    public const string CookieRefresh = "prismarh_refresh";

    /// <summary>Nomes das politicas de limite. Definidas no `Program.cs`.</summary>
    public const string PoliticaLoginPorIp = "login-por-ip";

    public const string PoliticaSessaoPorIp = "sessao-por-ip";

    /// <summary>
    /// O cookie so e enviado nas rotas de autenticacao. Requisicao para
    /// /api/empresas nao carrega o refresh, entao ele nao aparece em log de
    /// proxy nem em erro de outro endpoint.
    /// </summary>
    private const string CaminhoCookie = "/api/autenticacao";

    public static IEndpointRouteBuilder MapearAutenticacao(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/api/autenticacao").WithTags("Autenticacao");

        // Limite POR IP, e nao por e-mail: no login nao ha usuario ainda, e e
        // isso que o atacante esta tentando descobrir. Ver Program.cs.
        grupo.MapPost("/entrar", EntrarAsync)
            .WithSummary("Autentica por e-mail e senha")
            .RequireRateLimiting(PoliticaLoginPorIp)
            .AllowAnonymous();

        grupo.MapPost("/renovar", RenovarAsync)
            .WithSummary("Renova a sessao usando o refresh token do cookie")
            .RequireRateLimiting(PoliticaSessaoPorIp)
            .AllowAnonymous();

        grupo.MapPost("/sair", SairAsync)
            .WithSummary("Revoga o refresh token e limpa o cookie")
            .RequireRateLimiting(PoliticaSessaoPorIp)
            .AllowAnonymous();

        grupo.MapGet("/eu", Eu)
            .WithSummary("Usuario autenticado no token atual")
            .RequireAuthorization();

        return rotas;
    }

    private static async Task<IResult> EntrarAsync(
        [FromBody] EntrarRequisicao requisicao,
        AutenticacaoServico servico,
        HttpContext contexto,
        CancellationToken ct)
    {
        var resultado = await servico.EntrarAsync(requisicao.Email, requisicao.Senha, ct);

        return resultado.Sucesso
            ? Responder(contexto, resultado.Sessao!)
            : Recusar(resultado.Falha!.Value);
    }

    private static async Task<IResult> RenovarAsync(
        AutenticacaoServico servico,
        HttpContext contexto,
        CancellationToken ct)
    {
        if (BarrarPorCsrf(contexto) is { } barrado)
        {
            return barrado;
        }

        contexto.Request.Cookies.TryGetValue(CookieRefresh, out var refresh);

        var resultado = await servico.RenovarAsync(refresh, ct);

        if (resultado.Sucesso)
        {
            return Responder(contexto, resultado.Sessao!);
        }

        // Sessao morta: o cookie precisa sumir do navegador, senao o front
        // fica tentando renovar com um token que nunca mais vai funcionar.
        LimparCookie(contexto);
        return Recusar(resultado.Falha!.Value);
    }

    private static async Task<IResult> SairAsync(
        AutenticacaoServico servico,
        HttpContext contexto,
        CancellationToken ct)
    {
        if (BarrarPorCsrf(contexto) is { } barrado)
        {
            return barrado;
        }

        contexto.Request.Cookies.TryGetValue(CookieRefresh, out var refresh);

        await servico.SairAsync(refresh, ct);
        LimparCookie(contexto);

        return Results.NoContent();
    }

    private static IResult Eu(IContextoUsuario usuario) =>
        Results.Ok(new
        {
            id = usuario.IdUsuario,
            idOrganizacao = usuario.IdOrganizacao,
            perfil = usuario.Perfil.ToString()
        });

    private static IResult Responder(HttpContext contexto, SessaoEmitida sessao)
    {
        GravarCookie(contexto, sessao.RefreshTokenBruto, sessao.RefreshTokenExpiraEm);

        return Results.Ok(new SessaoResposta(
            sessao.AccessToken,
            sessao.AccessTokenExpiraEm,
            sessao.Usuario));
    }

    private static IResult Recusar(FalhaAutenticacao falha) => falha switch
    {
        // Credencial invalida e usuario inexistente devolvem exatamente a mesma
        // coisa. Diferenciar contaria a quem tentar quais e-mails existem.
        FalhaAutenticacao.CredencialInvalida => Results.Problem(
            title: "Credenciais invalidas",
            detail: "E-mail ou senha incorretos.",
            statusCode: StatusCodes.Status401Unauthorized),

        FalhaAutenticacao.UsuarioInativo => Results.Problem(
            title: "Usuario inativo",
            detail: "Este acesso foi desativado. Procure o administrador da sua organizacao.",
            statusCode: StatusCodes.Status403Forbidden),

        FalhaAutenticacao.RefreshReutilizado => Results.Problem(
            title: "Sessao encerrada por seguranca",
            detail: "Um token ja utilizado foi apresentado novamente. Todas as sessoes foram encerradas. Entre novamente.",
            statusCode: StatusCodes.Status401Unauthorized),

        _ => Results.Problem(
            title: "Sessao invalida",
            detail: "Entre novamente.",
            statusCode: StatusCodes.Status401Unauthorized)
    };

    /// <summary>
    /// Em Development o cookie continua `Lax`; fora dele, `None`.
    ///
    /// ## Por que a diferenca existe
    ///
    /// `SameSite=None` **exige** `Secure`, e `Secure` exige HTTPS. Em
    /// `localhost:5173 -> localhost:5080` nao ha HTTPS, e um cookie `None`
    /// sem `Secure` e simplesmente descartado pelo navegador: o
    /// desenvolvimento pararia de funcionar.
    ///
    /// Em `localhost` os dois lados sao o MESMO site, entao `Lax` funciona e
    /// mantem a protecao CSRF de graca. Em producao sao dominios diferentes, e
    /// ai `None` e obrigatorio - com a `GuardaCsrf` no lugar do que o `Lax`
    /// fazia sozinho.
    ///
    /// ⚠️ Isto e uma diferenca deliberada entre ambientes, e ela e testada:
    /// ha teste para o comportamento de producao, e nao so para o local.
    /// </summary>
    private static (bool Seguro, SameSiteMode Modo) ModoDoCookie(HttpContext contexto)
    {
        var producao = !contexto.RequestServices
            .GetRequiredService<IWebHostEnvironment>()
            .IsDevelopment();

        return (producao, producao ? SameSiteMode.None : SameSiteMode.Lax);
    }

    private static void GravarCookie(HttpContext contexto, string valor, DateTimeOffset expiraEm)
    {
        var (seguro, modo) = ModoDoCookie(contexto);

        contexto.Response.Cookies.Append(CookieRefresh, valor, new CookieOptions
        {
            HttpOnly = true,
            Secure = seguro,
            SameSite = modo,
            Path = CaminhoCookie,
            Expires = expiraEm,
            IsEssential = true
        });

        // O par do double submit. Emitido JUNTO com o refresh, e com a mesma
        // validade: um sem o outro nao serve para nada.
        //
        // ⚠️ `HttpOnly = false` de proposito - o frontend precisa LER este
        // valor para repetir no cabecalho. Ele nao autentica ninguem; quem
        // autentica e o refresh, que continua inacessivel ao script.
        contexto.Response.Cookies.Append(GuardaCsrf.Cookie, GuardaCsrf.GerarToken(), new CookieOptions
        {
            HttpOnly = false,
            Secure = seguro,
            SameSite = modo,
            // Path RAIZ, e nao o do refresh: o JavaScript da tela precisa
            // enxergar este cookie de qualquer pagina para montar o cabecalho.
            Path = "/",
            Expires = expiraEm,
            IsEssential = true
        });
    }

    private static void LimparCookie(HttpContext contexto)
    {
        var (seguro, modo) = ModoDoCookie(contexto);

        contexto.Response.Cookies.Delete(CookieRefresh, new CookieOptions
        {
            Path = CaminhoCookie,
            SameSite = modo,
            Secure = seguro
        });

        contexto.Response.Cookies.Delete(GuardaCsrf.Cookie, new CookieOptions
        {
            Path = "/",
            SameSite = modo,
            Secure = seguro
        });
    }

    /// <summary>
    /// Barra a requisicao quando ela depende do cookie e nao passa na guarda.
    ///
    /// Aplicada em `renovar` e `sair` - as duas unicas rotas que o navegador
    /// autentica **pelo cookie**. As demais usam o header `Authorization`, que
    /// o navegador nao anexa sozinho e por isso nao sao vulneraveis a CSRF.
    /// </summary>
    private static IResult? BarrarPorCsrf(HttpContext contexto)
    {
        var origens = contexto.RequestServices
            .GetRequiredService<IConfiguration>()
            .GetSection("Cors:OrigensPermitidas")
            .Get<string[]>() ?? [];

        // Em Development o cookie e `Lax`, que ja fecha o CSRF sozinho, e
        // exigir o token quebraria ferramenta de linha de comando no
        // desenvolvimento. Fora de Development, a guarda vale sempre.
        if (contexto.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment())
        {
            return null;
        }

        var recusa = GuardaCsrf.Conferir(contexto, origens);

        return recusa == RecusaCsrf.Aceita
            ? null
            : Results.Problem(
                title: "Requisicao recusada",
                detail: GuardaCsrf.Explicar(recusa),
                statusCode: StatusCodes.Status403Forbidden);
    }
}
