using System.Net;
using PrismaRH.Infraestrutura.Integracoes;

namespace PrismaRH.Testes.Integracoes;

/// <summary>
/// A defesa de SSRF da Fase 8.
///
/// ## Nenhum destes testes toca a rede
///
/// O resolvedor de DNS e injetado, entao o teste diz "este host resolve para
/// 169.254.169.254" sem precisar que isso seja verdade em lugar nenhum. Testar
/// defesa de rede contra a rede real da uma suite que falha no aviao e passa no
/// escritorio - o que nao prova nada nas duas vezes.
/// </summary>
public sealed class GuardaDestinoTestes
{
    private const string Permitido = "https://brasilapi.com.br/api/cnpj/v1/11222333000181";

    private static GuardaDestino ComDns(params string[] enderecos) =>
        new((_, _) => Task.FromResult(enderecos.Select(IPAddress.Parse).ToArray()));

    private static Task Conferir(GuardaDestino guarda, string url) =>
        guarda.ConferirAsync(new Uri(url), CancellationToken.None);

    // ------------------------------------------------------------- o caminho feliz

    [Fact]
    public async Task AceitaOHostDaAllowlistQuandoResolveParaEnderecoPublico()
    {
        var guarda = ComDns("104.18.0.1");

        await Conferir(guarda, Permitido);
    }

    // --------------------------------------------------------------- pela URL

    [Theory]
    // Em HTTP o CNPJ trafega em claro e a resposta pode ser trocada no caminho.
    [InlineData("http://brasilapi.com.br/api/cnpj/v1/1", "https")]
    // O classico: Host e o do atacante, mas parece a allowlist para quem le
    // rapido. Barrado pelo userinfo, que vem ANTES da allowlist - se a URL
    // chegasse ate la, `Uri.Host` ja seria "servidor-do-atacante.com" e a
    // allowlist tambem recusaria. Duas cercas, e a de fora pega primeiro.
    [InlineData("https://brasilapi.com.br@servidor-do-atacante.com/x", "usuario embutido")]
    // Sufixo colado no nome permitido. `endsWith` cairia; comparacao exata nao.
    [InlineData("https://brasilapi.com.br.servidor-do-atacante.com/x", "allowlist")]
    // Subdominio nao esta na allowlist, e a allowlist e de nomes exatos.
    [InlineData("https://interno.brasilapi.com.br/x", "allowlist")]
    [InlineData("https://brasilapi.com.br:8443/x", "Porta")]
    [InlineData("file:///c:/windows/win.ini", "https")]
    public async Task RecusaAntesDeResolverNome(string url, string trecho)
    {
        // DNS que aprovaria qualquer coisa: prova que a recusa veio da URL, e
        // nao por sorte na resolucao.
        var guarda = ComDns("104.18.0.1");

        var erro = await Assert.ThrowsAsync<DestinoRecusadoException>(() => Conferir(guarda, url));

        Assert.Contains(trecho, erro.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ----------------------------------------------------------------- pelo IP

    [Theory]
    // ⚠️ O metadata service de AWS, Azure e GCP. Uma requisicao para ca devolve
    // credencial da instancia em texto puro. E o alvo numero um de SSRF.
    [InlineData("169.254.169.254")]
    [InlineData("127.0.0.1")]
    [InlineData("127.1.2.3")]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.254")]
    [InlineData("192.168.1.1")]
    [InlineData("0.0.0.0")]
    [InlineData("100.64.0.1")]
    [InlineData("192.0.0.1")]
    [InlineData("198.18.0.1")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fd00::1")]
    [InlineData("fe80::1")]
    public async Task RecusaQuandoOHostPermitidoResolveParaEnderecoInterno(string endereco)
    {
        var guarda = ComDns(endereco);

        var erro = await Assert.ThrowsAsync<DestinoRecusadoException>(() => Conferir(guarda, Permitido));

        Assert.Contains("nao roteavel", erro.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ⚠️ O desvio que a maioria das implementacoes deixa passar.
    ///
    /// `::ffff:169.254.169.254` e IPv6 valido, as rotinas de IPv6 o consideram
    /// global - e a pilha de rede conecta nele como se fosse IPv4. Quem confere
    /// as faixas por familia sem desembrulhar antes aprova o metadata service
    /// achando que aprovou um endereco publico.
    /// </summary>
    [Theory]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.0.0.1")]
    public async Task RecusaEnderecoInternoDisfarcadoDeIPv6(string endereco)
    {
        var guarda = ComDns(endereco);

        await Assert.ThrowsAsync<DestinoRecusadoException>(() => Conferir(guarda, Permitido));
    }

    /// <summary>
    /// Um nome pode resolver para varios enderecos, e quem conecta escolhe qual
    /// usar. Conferir so o primeiro seria conferir na sorte - e o atacante que
    /// controla o DNS escolhe a ordem.
    /// </summary>
    [Fact]
    public async Task RecusaQuandoUmDosEnderecosEInterno()
    {
        var guarda = ComDns("104.18.0.1", "169.254.169.254");

        await Assert.ThrowsAsync<DestinoRecusadoException>(() => Conferir(guarda, Permitido));
    }

    [Fact]
    public async Task RecusaQuandoONomeNaoResolveParaNada()
    {
        var guarda = ComDns();

        var erro = await Assert.ThrowsAsync<DestinoRecusadoException>(() => Conferir(guarda, Permitido));

        Assert.Contains("nao resolveu", erro.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------- a tabela de faixas em si

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("104.18.0.1")]
    [InlineData("1.1.1.1")]
    [InlineData("2606:4700::1")]
    public void EnderecoPublicoNaoEProibido(string endereco) =>
        Assert.False(GuardaDestino.EnderecoProibido(IPAddress.Parse(endereco)));

    /// <summary>
    /// 172.15 e 172.32 estao **fora** da faixa privada, que e 172.16 a 172.31.
    /// Uma leitura apressada bloquearia o `172.` inteiro e derrubaria destino
    /// publico legitimo.
    /// </summary>
    [Theory]
    [InlineData("172.15.0.1")]
    [InlineData("172.32.0.1")]
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.1")]
    [InlineData("169.253.0.1")]
    public void ABordaDaFaixaPrivadaNaoEBloqueadaPorEngano(string endereco) =>
        Assert.False(GuardaDestino.EnderecoProibido(IPAddress.Parse(endereco)));
}
