using System.Security.Cryptography;

namespace PrismaRH.Api.Producao;

/// <summary>Por que a requisição foi barrada. Vocabulário fechado.</summary>
public enum RecusaCsrf
{
    Aceita = 0,
    SemCookie = 1,
    SemCabecalho = 2,
    NaoConferem = 3,
    OrigemAusente = 4,
    OrigemNaoPermitida = 5,
}

/// <summary>
/// A defesa contra CSRF das rotas que dependem do cookie de refresh.
///
/// ## Por que ela passou a ser necessária
///
/// Até a Fase 9 o cookie usava `SameSite=Lax`, e isso fechava o CSRF **de
/// graça**: o navegador simplesmente não envia um cookie `Lax` num `POST` vindo
/// de outro site, e as duas rotas expostas (`renovar` e `sair`) são `POST`.
///
/// Em produção o frontend fica na Vercel e a API na AWS — **domínios
/// registráveis diferentes, portanto cross-site**. Com `Lax`, o navegador para
/// de enviar o cookie e a sessão morre a cada recarga.
///
/// A correção é `SameSite=None; Secure`. E ela **reabre exatamente o CSRF que o
/// `Lax` fechava**: agora qualquer site pode disparar um `POST` para
/// `/api/autenticacao/renovar` e o navegador anexa o cookie sozinho.
///
/// Trocar por reflexo na pressa do deploy substituiria uma falha visível — a
/// sessão que não sobrevive ao F5 — por uma silenciosa. Por isso esta classe
/// existe, e por isso ela tem teste próprio.
///
/// ## Duas barreiras, e a razão de serem duas
///
/// **1. Double submit cookie.** Um segundo cookie, este **legível por
/// JavaScript**, carrega um valor aleatório. O frontend lê e repete no
/// cabeçalho `X-CSRF-Token`. O servidor exige que os dois batam.
///
/// Funciona porque a *same-origin policy* impede um site atacante de **ler** o
/// cookie do nosso domínio — ele consegue fazer o navegador enviá-lo, mas não
/// consegue descobrir o valor para repetir no cabeçalho. Enviar sem saber o
/// conteúdo não basta.
///
/// **2. Validação de `Origin`.** O `Origin` é preenchido pelo navegador e
/// **não pode ser forjado por JavaScript de página**. Ele barra a requisição
/// antes mesmo do token.
///
/// Nenhuma das duas sozinha bastaria: o double submit cai se houver um XSS em
/// qualquer subdomínio que possa escrever cookie no domínio pai; a validação de
/// `Origin` cai se algum cliente legítimo não o enviar. Juntas, cada uma cobre
/// o buraco da outra — que é o `defense in depth` do `CLAUDE.md §24.2`.
///
/// ## A comparação é em tempo constante
///
/// `CryptographicOperations.FixedTimeEquals`. Comparar com `==` vazaria, pelo
/// **tempo de resposta**, quantos caracteres iniciais o atacante acertou — a
/// mesma classe de canal lateral que o login já fecha com o hash falso
/// (`§24.3`).
/// </summary>
public static class GuardaCsrf
{
    /// <summary>
    /// O cookie do token anti-CSRF. **Não é `HttpOnly`**, e isso é a
    /// funcionalidade: o frontend precisa lê-lo para repetir no cabeçalho.
    ///
    /// Ele não é segredo de sessão — não autentica ninguém. Quem autentica é o
    /// refresh, que continua `HttpOnly` e inacessível ao script.
    /// </summary>
    public const string Cookie = "prismarh_csrf";

    public const string Cabecalho = "X-CSRF-Token";

    /// <summary>32 bytes de aleatoriedade criptográfica, em base64url.</summary>
    public static string GerarToken() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// Confere uma requisição que depende do cookie.
    ///
    /// A ordem importa: `Origin` primeiro, porque ele é o mais barato de
    /// conferir e o mais difícil de forjar.
    /// </summary>
    public static RecusaCsrf Conferir(HttpContext contexto, IReadOnlyCollection<string> origensPermitidas)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(origensPermitidas);

        var origem = contexto.Request.Headers.Origin.ToString();

        if (string.IsNullOrWhiteSpace(origem))
        {
            // ⚠️ Origem ausente é RECUSA, e não "provavelmente é o app".
            //
            // Todo navegador moderno envia `Origin` em requisição cross-site
            // com credenciais. Aceitar a ausência criaria a brecha exata que
            // um cliente não-navegador usaria - e o `CLAUDE.md §24.2` manda
            // falhar fechado.
            return RecusaCsrf.OrigemAusente;
        }

        // Comparação EXATA, e nunca por sufixo. `endsWith(".vercel.app")`
        // aprovaria `ataque.vercel.app`, que qualquer um cria de graça.
        if (!origensPermitidas.Contains(origem, StringComparer.OrdinalIgnoreCase))
        {
            return RecusaCsrf.OrigemNaoPermitida;
        }

        var doCookie = contexto.Request.Cookies[Cookie];

        if (string.IsNullOrEmpty(doCookie))
        {
            return RecusaCsrf.SemCookie;
        }

        var doCabecalho = contexto.Request.Headers[Cabecalho].ToString();

        if (string.IsNullOrEmpty(doCabecalho))
        {
            return RecusaCsrf.SemCabecalho;
        }

        return Iguais(doCookie, doCabecalho) ? RecusaCsrf.Aceita : RecusaCsrf.NaoConferem;
    }

    /// <summary>
    /// Comparação em tempo constante.
    ///
    /// O comprimento diferente sai cedo de propósito: ele não é segredo, e
    /// tratar tamanhos distintos como iguais complicaria sem ganho.
    /// </summary>
    private static bool Iguais(string a, string b)
    {
        var x = System.Text.Encoding.UTF8.GetBytes(a);
        var y = System.Text.Encoding.UTF8.GetBytes(b);

        return x.Length == y.Length && CryptographicOperations.FixedTimeEquals(x, y);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Mensagem para o cliente. Diz o que fazer, sem ensinar a burlar.</summary>
    public static string Explicar(RecusaCsrf recusa) => recusa switch
    {
        RecusaCsrf.OrigemAusente or RecusaCsrf.OrigemNaoPermitida =>
            "Requisicao recusada: origem nao autorizada.",
        _ => "Requisicao recusada: token de seguranca ausente ou invalido. Recarregue a pagina.",
    };
}
