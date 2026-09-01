namespace PrismaRH.Api.Producao;

/// <summary>
/// Os cabeçalhos de segurança da resposta (`CLAUDE.md §24.20`).
///
/// ## Por que uma CSP construída à mão, e não copiada
///
/// O `§24.20` é explícito: *"não inserir configuração aleatória. Uma CSP copiada
/// de exemplo quebra o frontend ou vira `unsafe-inline`, que não protege"*.
///
/// Esta API **não serve HTML** — ela devolve JSON. Isso muda tudo: a CSP que
/// importa para a tela é a da Vercel, e a daqui existe para o caso de alguém
/// abrir uma resposta da API direto no navegador. Por isso ela é a mais
/// restritiva possível, `default-src 'none'`, e não tem uma linha de
/// `unsafe-inline`.
///
/// A CSP do frontend vive no `vercel.json`, onde ela pode ser escrita a partir
/// do que a aplicação realmente carrega.
///
/// ## HSTS só em produção, e o motivo não é preguiça
///
/// `Strict-Transport-Security` faz o navegador **recusar HTTP naquele host por
/// meses**. Em `localhost`, isso trancaria o desenvolvimento de todos os
/// projetos da máquina que usam a mesma porta — e o efeito sobrevive a limpar
/// o cache. Por isso o cabeçalho só sai quando o ambiente não é Development.
/// </summary>
public static class CabecalhosSeguranca
{
    /// <summary>
    /// CSP de uma API que só devolve JSON: **nada pode ser carregado**.
    ///
    /// `frame-ancestors 'none'` é o que fecha clickjacking, e substitui o
    /// antigo `X-Frame-Options` — que continua sendo enviado só por causa de
    /// navegadores velhos.
    /// </summary>
    public const string Politica =
        "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    /// <summary>Um ano, com subdomínios. O valor que os pré-carregadores exigem.</summary>
    public const string Hsts = "max-age=31536000; includeSubDomains";

    public static IApplicationBuilder UsarCabecalhosSeguranca(
        this IApplicationBuilder aplicacao,
        bool producao)
    {
        ArgumentNullException.ThrowIfNull(aplicacao);

        return aplicacao.Use(async (contexto, proximo) =>
        {
            var h = contexto.Response.Headers;

            // Impede o navegador de "adivinhar" o tipo do conteúdo. Sem isto,
            // uma resposta JSON com texto controlado pelo usuário pode ser
            // interpretada como HTML e executar script.
            h["X-Content-Type-Options"] = "nosniff";

            h["Content-Security-Policy"] = Politica;
            h["X-Frame-Options"] = "DENY";

            // Nenhuma URL desta API vaza para outro site. As rotas carregam
            // identificadores; o Referer os levaria junto para onde o usuário
            // navegar depois.
            h["Referrer-Policy"] = "no-referrer";

            // A API não usa câmera, microfone, geolocalização nem pagamento.
            // Declarar isso desliga os recursos mesmo que algum dia um HTML
            // acabe sendo servido daqui por engano.
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";

            // Some com o "Kestrel"/"Microsoft-IIS" da resposta. Não é defesa
            // real — segurança por obscuridade não protege ninguém —, mas
            // também não há motivo para anunciar a pilha para varredura
            // automatizada.
            h.Remove("Server");

            if (producao)
            {
                h["Strict-Transport-Security"] = Hsts;
            }

            await proximo();
        });
    }
}
