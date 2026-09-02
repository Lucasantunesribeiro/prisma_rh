using PrismaRH.Api.Producao;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrismaRH.Infraestrutura.Persistencia;
using PrismaRH.Aplicacao.Identidade;

namespace PrismaRH.Api.Endpoints;

public sealed record EntrarRequisicao(string Email, string Senha);

public sealed record SessaoResposta(
    string AccessToken,
    DateTimeOffset ExpiraEm,
    UsuarioAutenticado Usuario,

    /// <summary>
    /// O par do *double submit*, entregue **no corpo**.
    ///
    /// ## O defeito que isto corrige
    ///
    /// ⚠️ Descoberto em **02/09/2026**, recarregando a pagina em producao: a
    /// sessao caia e voltava para a tela de login. A causa estava um nivel
    /// abaixo do sintoma.
    ///
    /// O frontend lia o token com `document.cookie`. Isso funcionava em
    /// desenvolvimento, onde tela e API vivem em `localhost`, e **nunca
    /// funcionou em producao**, onde a tela esta em `portfolio-prisma-rh.
    /// vercel.app` e a API em `*.lambda-url.us-east-1.on.aws`. `document.
    /// cookie` e **por origem**: a pagina da Vercel jamais enxergou um cookie
    /// gravado pelo dominio da Lambda.
    ///
    /// Consequencia: o cabecalho `X-CSRF-Token` nunca era enviado, `renovar` e
    /// `sair` respondiam **403** — o 403 que aparecia no console —, e um F5
    /// deslogava.
    ///
    /// ## Por que entregar no corpo NAO enfraquece nada
    ///
    /// O que protege o *double submit* nao e o cookie ser legivel: e o site
    /// atacante **nao conseguir descobrir o valor**. Ele continua sem
    /// conseguir:
    ///
    /// | Caminho | Por que falha para o atacante |
    /// |---|---|
    /// | Ler o cookie | *same-origin policy*, e agora o cookie e `HttpOnly` |
    /// | Ler este corpo | o CORS tem **allowlist de origem**; a resposta nao chega a ele |
    /// | Adivinhar | valor aleatorio, comparado em tempo constante |
    ///
    /// E o servidor nao afrouxou: continua exigindo cookie **e** cabecalho
    /// iguais, mais `Origin` na allowlist, com **ausencia = recusa**.
    ///
    /// ⚠️ O cookie passou a ser `HttpOnly`, o que e **mais** restrito que
    /// antes — o JavaScript deixou de precisar le-lo.
    /// </summary>
    string TokenCsrf);

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

        grupo.MapGet("/eu", EuAsync)
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

    /// <summary>
    /// Quem esta autenticado.
    ///
    /// ## O defeito que isto corrige
    ///
    /// ⚠️ Ate 02/09/2026 esta rota devolvia **so** id, organizacao e perfil,
    /// lidos das claims do token. `POST entrar` devolvia o usuario completo, e
    /// esta nao - entao **depois de um F5 o nome e o e-mail sumiam da tela**,
    /// e a barra lateral passava a mostrar apenas "Visualizador".
    ///
    /// A lacuna estava registrada como "depende de decisao do responsavel", e a
    /// decisao era mais simples do que parecia: a rota de restaurar sessao tem
    /// de devolver **o mesmo contrato** da rota de entrar. Duas respostas
    /// diferentes para a mesma pergunta e que era o defeito.
    ///
    /// ## Por que ler o banco, e nao inchar o token
    ///
    /// A alternativa seria carregar nome e e-mail como claims. Nao: o access
    /// token viaja em todo cabecalho e e legivel por quem o tiver — colocar
    /// dado pessoal ali o espalha sem necessidade (`CLAUDE.md §24.13`). Uma
    /// leitura por restauracao de sessao e barata e mantem o dado onde ele mora.
    ///
    /// ⚠️ A consulta passa pelo **filtro global**: o usuario e procurado dentro
    /// da organizacao do proprio token, e nao por id solto.
    /// </summary>
    private static async Task<IResult> EuAsync(
        IContextoUsuario usuario,
        PrismaRhDbContext db,
        CancellationToken ct)
    {
        var encontrado = await db.Usuarios
            .AsNoTracking()
            .Where(u => u.Id == usuario.IdUsuario)
            .Select(u => new { u.Nome, u.Email })
            .FirstOrDefaultAsync(ct);

        return Results.Ok(new
        {
            id = usuario.IdUsuario,
            idOrganizacao = usuario.IdOrganizacao,
            perfil = usuario.Perfil.ToString(),
            nome = encontrado?.Nome,
            email = encontrado?.Email,
        });
    }

    private static IResult Responder(HttpContext contexto, SessaoEmitida sessao)
    {
        var tokenCsrf = GravarCookie(
            contexto, sessao.RefreshTokenBruto, sessao.RefreshTokenExpiraEm);

        return Results.Ok(new SessaoResposta(
            sessao.AccessToken,
            sessao.AccessTokenExpiraEm,
            sessao.Usuario,
            tokenCsrf));
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

    /// <summary>
    /// Grava os dois cookies e **devolve** o token do double submit, para que
    /// ele siga tambem no corpo da resposta. Ver <see cref="SessaoResposta"/>.
    /// </summary>
    private static string GravarCookie(HttpContext contexto, string valor, DateTimeOffset expiraEm)
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
        // ⚠️ `HttpOnly = true` desde 02/09/2026. Antes era `false`, porque o
        // frontend lia o valor com `document.cookie` - o que NUNCA funcionou em
        // producao, onde tela e API estao em dominios diferentes. O valor agora
        // vai no corpo da resposta, entao o script nao precisa mais do cookie,
        // e fecha-lo e ganho liquido. Ver `SessaoResposta.TokenCsrf`.
        var tokenCsrf = GuardaCsrf.GerarToken();

        contexto.Response.Cookies.Append(GuardaCsrf.Cookie, tokenCsrf, new CookieOptions
        {
            HttpOnly = true,
            Secure = seguro,
            SameSite = modo,
            // Path RAIZ, e nao o do refresh: o navegador precisa envia-lo em
            // `renovar` e em `sair`, que ficam em caminhos diferentes.
            Path = "/",
            Expires = expiraEm,
            IsEssential = true
        });

        return tokenCsrf;
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
