using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using PrismaRH.Dominio.Identidade;
using PrismaRH.Infraestrutura.Identidade;
using PrismaRH.Testes.Isolamento;

namespace PrismaRH.Testes.Seguranca;

/// <summary>
/// Token forjado (Fase 12).
///
/// ## O que estes testes provam
///
/// O Security Gate da Fase 12 abre a lista de testes automatizados com
/// **autenticação: token expirado, assinatura inválida, emissor errado,
/// ausência de token**. Até aqui a suíte provava que o login funciona; não
/// provava que um token **fabricado** não funciona — que é a pergunta do
/// atacante, não a do usuário.
///
/// ## Por que cada caso importa
///
/// | Caso | O que ele fecha |
/// |---|---|
/// | **Assinatura inválida** | Alguém troca o `perfil` dentro do token e reassina com outra chave. Sem `ValidateIssuerSigningKey`, vira Administrador de graça. |
/// | **Emissor errado** | Token legítimo **de outro sistema** que usa a mesma chave. Sem `ValidateIssuer`, ele é aceito aqui. |
/// | **Público errado** | Token emitido por nós para outro destinatário — um serviço parceiro — reapresentado à API. |
/// | **Expirado** | O `ClockSkew = TimeSpan.Zero` existe para que 15 minutos sejam 15, e não 20. Este teste é o que impede alguém "simplificar" isso de volta ao padrão. |
/// | **`alg: none`** | O ataque clássico de JWT: token sem assinatura nenhuma. |
/// | **Ausente / lixo** | O caminho trivial, que costuma ser o que ninguém escreve. |
///
/// Todos apontam para uma rota **de leitura comum** — se o token furasse,
/// furaria ali primeiro.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class TokenForjadoTestes(BancoPostgresFixture banco)
{
    /// <summary>A mesma chave que a `FabricaApiIsolada` injeta no host.</summary>
    private const string ChaveDoHost = "chave-de-teste-de-isolamento-com-mais-de-32-caracteres";

    private const string ChaveDoAtacante = "chave-que-o-atacante-inventou-com-mais-de-32-caracteres";

    private const string Emissor = "prisma-rh";

    /// <summary>Rota de leitura comum: qualquer perfil autenticado passa.</summary>
    private const string RotaProtegida = "/api/empresas";

    private static string Forjar(
        string chave = ChaveDoHost,
        string emissor = Emissor,
        string publico = Emissor,
        TimeSpan? validade = null)
    {
        var credenciais = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(chave)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: emissor,
            audience: publico,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, Guid.CreateVersion7().ToString()),
                new Claim(JwtRegisteredClaimNames.Email, "forjado@atacante.teste"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
                new Claim(GeradorJwt.ClaimOrganizacao, Guid.CreateVersion7().ToString()),

                // ⚠️ O perfil mais alto que existe. Se algum caso passar, ele
                // passa como Administrador da Plataforma - e o teste diz na hora
                // qual barreira nao segurou.
                new Claim(GeradorJwt.ClaimPerfil, Perfil.AdministradorPlataforma.ToString()),
            ],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.Add(validade ?? TimeSpan.FromMinutes(15)),
            signingCredentials: credenciais);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task<HttpStatusCode> TentarAsync(string? token)
    {
        using var fabrica = new FabricaApiIsolada(banco.StringConexao);
        using var cliente = fabrica.CreateClient();

        if (token is not null)
        {
            cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var resposta = await cliente.GetAsync(RotaProtegida);

        return resposta.StatusCode;
    }

    // ------------------------------------------------------------- recusa

    [Fact]
    public async Task SemTokenNaoEntra() =>
        Assert.Equal(HttpStatusCode.Unauthorized, await TentarAsync(null));

    [Theory]
    [InlineData("")]
    [InlineData("nao-e-um-token")]
    [InlineData("a.b.c")]
    public async Task TokenQueNaoEJwtNaoEntra(string lixo) =>
        Assert.Equal(HttpStatusCode.Unauthorized, await TentarAsync(lixo));

    /// <summary>
    /// ⚠️ Assinado com **outra chave**, mas com todos os claims certos e o
    /// perfil mais alto. É o ataque de quem leu o token, entendeu o formato e
    /// reassinou com uma chave própria.
    /// </summary>
    [Fact]
    public async Task AssinaturaComOutraChaveNaoEntra() =>
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            await TentarAsync(Forjar(chave: ChaveDoAtacante)));

    /// <summary>
    /// ⚠️ Chave **certa**, emissor errado. É o token legítimo de outro sistema
    /// que por acaso compartilha o segredo — cenário real quando uma chave é
    /// reaproveitada entre projetos.
    /// </summary>
    [Fact]
    public async Task EmissorErradoNaoEntra() =>
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            await TentarAsync(Forjar(emissor: "outro-sistema")));

    /// <summary>Emitido por nós, mas para outro destinatário.</summary>
    [Fact]
    public async Task PublicoErradoNaoEntra() =>
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            await TentarAsync(Forjar(publico: "outro-servico")));

    /// <summary>
    /// ⚠️ Expirado **há um segundo**.
    ///
    /// Com o `ClockSkew` padrão do .NET — cinco minutos — este token seria
    /// aceito. O `Program.cs` zera a tolerância de propósito, e é este teste que
    /// impede alguém de "simplificar" removendo a linha.
    /// </summary>
    [Fact]
    public async Task TokenExpiradoHaUmSegundoNaoEntra() =>
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            await TentarAsync(Forjar(validade: TimeSpan.FromSeconds(-1))));

    /// <summary>
    /// ⚠️ O ataque clássico de JWT: `alg: none`, sem assinatura.
    ///
    /// Bibliotecas antigas aceitavam. A do .NET não — mas "não deveria
    /// acontecer" não é o mesmo que "está provado que não acontece".
    /// </summary>
    [Fact]
    public async Task TokenSemAssinaturaNaoEntra()
    {
        static string Base64Url(string texto) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(texto))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var cabecalho = Base64Url("""{"alg":"none","typ":"JWT"}""");

        var corpo = Base64Url(
            $$"""
            {"sub":"{{Guid.CreateVersion7()}}","iss":"{{Emissor}}","aud":"{{Emissor}}",
             "perfil":"AdministradorPlataforma","exp":{{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}}}
            """);

        Assert.Equal(HttpStatusCode.Unauthorized, await TentarAsync($"{cabecalho}.{corpo}."));
    }

    // -------------------------------------------------------------- aceita

    /// <summary>
    /// ⚠️ **O controle deste arquivo.**
    ///
    /// Um token forjado com a chave certa, emissor certo e validade certa
    /// **passa pela autenticação** — o que prova que os testes acima falham
    /// pelo motivo que dizem, e não porque a rota está quebrada.
    ///
    /// Ele não devolve dado de ninguém: o `IdOrganizacao` do claim é inventado,
    /// e o filtro global não casa com organização nenhuma. É o `fail closed` do
    /// `§24.5` na prática — autenticar não é o mesmo que alcançar dado.
    /// </summary>
    [Fact]
    public async Task TokenBemFormadoPassaNaAutenticacaoMasNaoAlcancaDadoDeNinguem()
    {
        using var fabrica = new FabricaApiIsolada(banco.StringConexao);
        using var cliente = fabrica.CreateClient();

        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Forjar());

        using var resposta = await cliente.GetAsync(RotaProtegida);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);

        var corpo = await resposta.Content.ReadAsStringAsync();

        // Organizacao inventada nao tem empresa nenhuma.
        Assert.Contains("\"total\":0", corpo.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
    }
}
