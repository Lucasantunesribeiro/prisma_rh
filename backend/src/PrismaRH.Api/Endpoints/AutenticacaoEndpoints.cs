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

    /// <summary>
    /// O cookie so e enviado nas rotas de autenticacao. Requisicao para
    /// /api/empresas nao carrega o refresh, entao ele nao aparece em log de
    /// proxy nem em erro de outro endpoint.
    /// </summary>
    private const string CaminhoCookie = "/api/autenticacao";

    public static IEndpointRouteBuilder MapearAutenticacao(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/api/autenticacao").WithTags("Autenticacao");

        grupo.MapPost("/entrar", EntrarAsync)
            .WithSummary("Autentica por e-mail e senha")
            .AllowAnonymous();

        grupo.MapPost("/renovar", RenovarAsync)
            .WithSummary("Renova a sessao usando o refresh token do cookie")
            .AllowAnonymous();

        grupo.MapPost("/sair", SairAsync)
            .WithSummary("Revoga o refresh token e limpa o cookie")
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

    private static void GravarCookie(HttpContext contexto, string valor, DateTimeOffset expiraEm) =>
        contexto.Response.Cookies.Append(CookieRefresh, valor, new CookieOptions
        {
            HttpOnly = true,
            Secure = !contexto.RequestServices
                .GetRequiredService<IWebHostEnvironment>()
                .IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            Path = CaminhoCookie,
            Expires = expiraEm,
            IsEssential = true
        });

    private static void LimparCookie(HttpContext contexto) =>
        contexto.Response.Cookies.Delete(CookieRefresh, new CookieOptions
        {
            Path = CaminhoCookie,
            SameSite = SameSiteMode.Lax,
            Secure = !contexto.RequestServices
                .GetRequiredService<IWebHostEnvironment>()
                .IsDevelopment()
        });
}
