using System.Net;
using System.Net.Sockets;

namespace PrismaRH.Infraestrutura.Integracoes;

/// <summary>Destino recusado pela guarda. Nunca vira requisicao.</summary>
public sealed class DestinoRecusadoException(string message) : Exception(message);

/// <summary>
/// A defesa de SSRF (Server-Side Request Forgery).
///
/// ## A ameaca, em uma frase
///
/// Quando o servidor faz uma requisicao, ele a faz de DENTRO da rede - com o
/// alcance e as credenciais que o navegador do atacante nao tem. Fazer o
/// servidor buscar `http://169.254.169.254/` numa nuvem devolve a credencial da
/// instancia. E o item 1 do Security Gate da Fase 8.
///
/// ## Por que a guarda existe se o usuario nao digita URL nenhuma
///
/// Nesta integracao o usuario informa **quatorze digitos**, e a URL e montada
/// pelo sistema a partir de uma allowlist fixa em codigo. O vetor classico -
/// "campo de URL" - simplesmente nao existe aqui.
///
/// A guarda continua valendo por dois motivos concretos:
///
/// 1. **Redirect.** O parceiro responde 302 e escolhe o proximo destino. Quem
///    valida so a primeira URL nao protege coisa alguma - por isso
///    `ConferirAsync` roda de novo a cada salto, e nao apenas na largada.
/// 2. **DNS.** O host da allowlist e resolvido por um servidor que o Prisma RH
///    nao controla. Se aquele nome passar a apontar para `127.0.0.1` ou para a
///    faixa da nuvem, a allowlist de host sozinha aprovaria a chamada. Por isso
///    o que se confere no fim e o **IP**, nao o nome.
///
/// ## O resolvedor e injetado de proposito
///
/// Para o teste conseguir dizer "este host resolve para 169.254.169.254" sem
/// depender de DNS de verdade. Testar defesa de rede contra a rede real da uma
/// suite que falha no aviao e passa no escritorio - o que nao prova nada nas
/// duas vezes.
/// </summary>
public sealed class GuardaDestino
{
    /// <summary>
    /// Quantos redirects a chamada aceita antes de desistir.
    ///
    /// Tres e folga para o parceiro trocar de dominio ou normalizar barra
    /// final. Cadeia maior nao e servico HTTP normal: e alguem tentando cansar
    /// a validacao ou consumir o timeout.
    /// </summary>
    public const int MaximoRedirects = 3;

    /// <summary>
    /// A allowlist. **Fixa em codigo, e nao em configuracao.**
    ///
    /// Configuracao seria mais "flexivel" e a flexibilidade aqui e o defeito:
    /// um `appsettings` editavel transforma a unica barreira de destino num
    /// campo que alguem preenche com pressa. Trocar de parceiro e alterar
    /// codigo, com revisao - que e exatamente o peso que a decisao tem.
    /// </summary>
    private static readonly string[] HostsPermitidos = ["brasilapi.com.br"];

    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolver;

    public GuardaDestino(Func<string, CancellationToken, Task<IPAddress[]>>? resolver = null) =>
        _resolver = resolver ?? ((host, ct) => Dns.GetHostAddressesAsync(host, ct));

    /// <summary>
    /// Recusa por excecao se o destino nao passar. Chamada antes da primeira
    /// requisicao e de novo a cada redirect.
    /// </summary>
    public async Task ConferirAsync(Uri destino, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(destino);

        if (!destino.IsAbsoluteUri)
        {
            throw new DestinoRecusadoException("Destino precisa ser URL absoluta.");
        }

        // Somente HTTPS. Em HTTP o CNPJ consultado trafega em claro e a resposta
        // pode ser trocada no caminho por quem estiver na rede.
        if (!string.Equals(destino.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            throw new DestinoRecusadoException($"Somente https e aceito. Recebido: {destino.Scheme}.");
        }

        // `https://brasilapi.com.br@servidor-do-atacante/` tem Host = o do
        // atacante, mas parece a allowlist para quem le rapido. Recusar o
        // userinfo inteiro elimina a classe.
        if (!string.IsNullOrEmpty(destino.UserInfo))
        {
            throw new DestinoRecusadoException("URL com usuario embutido nao e aceita.");
        }

        if (!destino.IsDefaultPort)
        {
            throw new DestinoRecusadoException($"Porta {destino.Port} nao e aceita.");
        }

        // `Uri.Host` ja vem normalizado e em punycode - a comparacao ordinal e
        // contra o nome canonico, nao contra o que veio escrito.
        if (!HostsPermitidos.Contains(destino.Host, StringComparer.OrdinalIgnoreCase))
        {
            throw new DestinoRecusadoException($"Host '{destino.Host}' esta fora da allowlist.");
        }

        var enderecos = await _resolver(destino.Host, cancelamento);

        if (enderecos.Length == 0)
        {
            throw new DestinoRecusadoException($"Host '{destino.Host}' nao resolveu para endereco algum.");
        }

        // TODOS precisam ser publicos, e nao apenas o primeiro. Um host que
        // resolve para dois enderecos deixaria o cliente HTTP escolher qual
        // usar - conferir so um seria conferir na sorte.
        foreach (var endereco in enderecos)
        {
            if (EnderecoProibido(endereco))
            {
                throw new DestinoRecusadoException(
                    $"Host '{destino.Host}' resolveu para endereco nao roteavel ou interno.");
            }
        }
    }

    /// <summary>
    /// Endereco que a aplicacao nunca deve alcancar por iniciativa de terceiro.
    ///
    /// A lista nao e decorativa: `169.254.169.254` e o *metadata service* de
    /// AWS, Azure e GCP, e uma requisicao para la devolve **credencial da
    /// instancia** em texto puro. As faixas privadas alcancam banco, fila e
    /// painel administrativo que nunca deveriam receber trafego vindo de fora.
    /// </summary>
    public static bool EnderecoProibido(IPAddress endereco)
    {
        ArgumentNullException.ThrowIfNull(endereco);

        // ⚠️ O bypass classico: `::ffff:169.254.169.254` e um IPv6 valido que
        // as rotinas de IPv6 consideram global, e que a pilha de rede conecta
        // como se fosse IPv4. Desembrulhar ANTES de decidir e o que fecha isso.
        if (endereco.IsIPv4MappedToIPv6)
        {
            endereco = endereco.MapToIPv4();
        }

        return endereco.AddressFamily switch
        {
            AddressFamily.InterNetwork => ProibidoV4(endereco),
            AddressFamily.InterNetworkV6 => ProibidoV6(endereco),

            // Familia que nao e IP nao tem como ser conferida. Falha fechada.
            _ => true,
        };
    }

    private static bool ProibidoV4(IPAddress endereco)
    {
        var b = endereco.GetAddressBytes();

        return b[0] switch
        {
            0 => true,                                  // 0.0.0.0/8 - "esta rede"
            10 => true,                                 // 10.0.0.0/8 - privada
            127 => true,                                // 127.0.0.0/8 - loopback
            169 when b[1] == 254 => true,               // 169.254.0.0/16 - link-local E METADATA
            172 when b[1] >= 16 && b[1] <= 31 => true,  // 172.16.0.0/12 - privada
            192 when b[1] == 168 => true,               // 192.168.0.0/16 - privada
            192 when b[1] == 0 && b[2] == 0 => true,    // 192.0.0.0/24 - protocolos IETF
            100 when b[1] >= 64 && b[1] <= 127 => true, // 100.64.0.0/10 - CGNAT
            198 when b[1] is 18 or 19 => true,          // 198.18.0.0/15 - benchmark
            >= 224 => true,                             // multicast e reservado, ate 255.255.255.255
            _ => false,
        };
    }

    private static bool ProibidoV6(IPAddress endereco)
    {
        if (IPAddress.IPv6Loopback.Equals(endereco) || IPAddress.IPv6Any.Equals(endereco))
        {
            return true;
        }

        if (endereco.IsIPv6LinkLocal || endereco.IsIPv6SiteLocal || endereco.IsIPv6Multicast)
        {
            return true;
        }

        var b = endereco.GetAddressBytes();

        // fc00::/7 - unique local, o equivalente IPv6 das faixas privadas.
        // `IsIPv6SiteLocal` cobre apenas fec0::/10, que foi DEPRECIADO - quem
        // confia so nele deixa a faixa que as redes usam de verdade passar.
        return (b[0] & 0xFE) == 0xFC;
    }
}
