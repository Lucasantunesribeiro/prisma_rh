using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrismaRH.Dominio.Identidade;
using PrismaRH.Infraestrutura.Persistencia;
using PrismaRH.Testes.Isolamento;

namespace PrismaRH.Testes.Seguranca;

/// <summary>
/// O bloqueio progressivo por conta, pela HTTP e contra PostgreSQL real.
///
/// `BloqueioProgressivoTestes` prova a política isolada. Estes provam o que só o
/// sistema inteiro responde: **a resposta muda?** Porque se ela mudar, o
/// bloqueio deixa de ser defesa e vira oráculo — um jeito de descobrir quais
/// e-mails existem.
///
/// ⚠️ O limite por IP da Fase 10 continua valendo em cima disto. Os testes
/// abaixo ficam **abaixo** das 10 tentativas/min daquele limite de propósito:
/// se esbarrassem nele, passariam pelo motivo errado.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class BloqueioPorContaHttpTestes(BancoPostgresFixture banco)
{
    private const string SenhaErrada = "isto-nao-e-a-senha-de-ninguem";

    private static Task<HttpResponseMessage> EntrarAsync(HttpClient cliente, string email, string senha) =>
        cliente.PostAsJsonAsync("/api/autenticacao/entrar", new { email, senha });

    /// <summary>
    /// Um usuário só desta classe, para as falhas de um teste não bloquearem a
    /// conta que outro teste usa.
    /// </summary>
    private async Task<string> UsuarioProprioAsync(FabricaApiIsolada fabrica, string marca)
    {
        var email = $"bloqueio-{marca}-{Guid.CreateVersion7():N}@teste.com";

        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PrismaRhDbContext>();
        var hasheador = escopo.ServiceProvider.GetRequiredService<
            PrismaRH.Aplicacao.Identidade.IHasheadorSenha>();

        var usuario = new Usuario(
            banco.IdOrganizacaoA,
            "Pessoa do teste de bloqueio",
            email,
            hasheador.Gerar(BancoPostgresFixture.Senha),
            Perfil.Visualizador,
            DateTimeOffset.UtcNow);

        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();

        return email;
    }

    /// <summary>
    /// Lê a resposta com o `traceId` removido.
    ///
    /// ⚠️ O `traceId` do `ProblemDetails` é **único por requisição** por
    /// construção — ele identifica a chamada, não o estado da conta. Compará-lo
    /// faria o teste falhar sempre, e por um motivo que não é o que ele quer
    /// provar.
    ///
    /// Tudo o mais do corpo continua sendo comparado byte a byte.
    /// </summary>
    private static async Task<(HttpStatusCode Status, string Corpo)> LerAsync(HttpResponseMessage r)
    {
        var corpo = await r.Content.ReadAsStringAsync();

        return (r.StatusCode, SemTraceId.Replace(corpo, "\"traceId\":\"<por-requisicao>\""));
    }

    private static readonly System.Text.RegularExpressions.Regex SemTraceId =
        new("""
        "traceId"\s*:\s*"[^"]*"
        """.Trim(), System.Text.RegularExpressions.RegexOptions.Compiled);

    // ------------------------------------------------------------- bloqueia

    /// <summary>
    /// Depois das falhas toleradas, **nem a senha certa entra** — a conta está
    /// esperando. É o que corta o credential stuffing distribuído, que o limite
    /// por IP não vê: mil máquinas, dez tentativas cada, nenhuma perto do limite.
    /// </summary>
    [Fact]
    public async Task DepoisDasFalhasToleradasAtASenhaCertaERecusada()
    {
        using var fabrica = new FabricaApiIsolada(banco.StringConexao);
        using var cliente = fabrica.CreateClient();

        var email = await UsuarioProprioAsync(fabrica, "trava");

        for (var i = 0; i <= PoliticaBloqueioConta.FalhasToleradas; i++)
        {
            using var falha = await EntrarAsync(cliente, email, SenhaErrada);
            Assert.Equal(HttpStatusCode.Unauthorized, falha.StatusCode);
        }

        using var comSenhaCerta = await EntrarAsync(cliente, email, BancoPostgresFixture.Senha);

        Assert.Equal(HttpStatusCode.Unauthorized, comSenhaCerta.StatusCode);
    }

    /// <summary>
    /// ⚠️ **O teste que impede o bloqueio de virar oráculo.**
    ///
    /// Três situações, uma resposta só: e-mail que não existe, senha errada em
    /// conta livre, e senha errada em conta bloqueada. Se qualquer uma delas se
    /// distinguisse, o atacante saberia quais e-mails estão cadastrados — e o
    /// `CLAUDE.md §24.3` fecha exatamente essa porta.
    /// </summary>
    [Fact]
    public async Task ContaBloqueadaEEmailInexistenteRespondemIGUAL()
    {
        using var fabrica = new FabricaApiIsolada(banco.StringConexao);
        using var cliente = fabrica.CreateClient();

        var email = await UsuarioProprioAsync(fabrica, "oraculo");

        using var contaLivre = await EntrarAsync(cliente, email, SenhaErrada);
        var livre = await LerAsync(contaLivre);

        for (var i = 0; i < PoliticaBloqueioConta.FalhasToleradas; i++)
        {
            (await EntrarAsync(cliente, email, SenhaErrada)).Dispose();
        }

        using var contaBloqueada = await EntrarAsync(cliente, email, SenhaErrada);
        var bloqueada = await LerAsync(contaBloqueada);

        using var inexistente = await EntrarAsync(
            cliente, $"ninguem-{Guid.CreateVersion7():N}@teste.com", SenhaErrada);
        var fantasma = await LerAsync(inexistente);

        Assert.Equal(livre.Status, bloqueada.Status);
        Assert.Equal(livre.Status, fantasma.Status);

        Assert.Equal(livre.Corpo, bloqueada.Corpo);
        Assert.Equal(livre.Corpo, fantasma.Corpo);

        // E o corpo nao pode citar bloqueio, espera nem tentativa restante.
        foreach (var palavra in new[] { "bloque", "tentativ", "espera", "minuto", "aguard" })
        {
            Assert.DoesNotContain(palavra, bloqueada.Corpo, StringComparison.OrdinalIgnoreCase);
        }
    }

    // -------------------------------------------------------------- solta

    /// <summary>
    /// ⚠️ **O teste que impede a defesa de virar arma.**
    ///
    /// O bloqueio expira pelo relógio. Aqui o relógio é adiantado empurrando
    /// `BloqueadoAte` para o passado no banco — que é exatamente o que o tempo
    /// faria — e a senha certa passa a entrar.
    ///
    /// Sem isto, qualquer pessoa que conheça um e-mail tranca aquele usuário
    /// fora do sistema errando a senha quatro vezes.
    /// </summary>
    [Fact]
    public async Task PassadaAEsperaASenhaCertaEntraEZeraOContador()
    {
        using var fabrica = new FabricaApiIsolada(banco.StringConexao);
        using var cliente = fabrica.CreateClient();

        var email = await UsuarioProprioAsync(fabrica, "solta");

        for (var i = 0; i <= PoliticaBloqueioConta.FalhasToleradas; i++)
        {
            (await EntrarAsync(cliente, email, SenhaErrada)).Dispose();
        }

        using (var bloqueado = await EntrarAsync(cliente, email, BancoPostgresFixture.Senha))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, bloqueado.StatusCode);
        }

        // O tempo passa.
        using (var escopo = fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<PrismaRhDbContext>();

            var usuario = await db.Usuarios
                .IgnoreQueryFilters()
                .SingleAsync(u => u.Email == email);

            Assert.True(usuario.FalhasDeLogin > PoliticaBloqueioConta.FalhasToleradas);

            await db.Usuarios
                .IgnoreQueryFilters()
                .Where(u => u.Email == email)
                .ExecuteUpdateAsync(s => s.SetProperty(
                    u => u.BloqueadoAte, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        using var entrou = await EntrarAsync(cliente, email, BancoPostgresFixture.Senha);

        Assert.Equal(HttpStatusCode.OK, entrou.StatusCode);

        // ⚠️ E o acerto zerou o contador: a conta nao fica "marcada".
        using var conferencia = fabrica.Services.CreateScope();
        var contexto = conferencia.ServiceProvider.GetRequiredService<PrismaRhDbContext>();

        var depois = await contexto.Usuarios
            .IgnoreQueryFilters()
            .SingleAsync(u => u.Email == email);

        Assert.Equal(0, depois.FalhasDeLogin);
        Assert.Null(depois.BloqueadoAte);
    }

    /// <summary>
    /// Acertar a senha antes de estourar as toleradas limpa o contador — o
    /// caminho do usuário que errou duas vezes e lembrou na terceira.
    /// </summary>
    [Fact]
    public async Task AcertarAntesDeBloquearLimpaOContador()
    {
        using var fabrica = new FabricaApiIsolada(banco.StringConexao);
        using var cliente = fabrica.CreateClient();

        var email = await UsuarioProprioAsync(fabrica, "limpa");

        for (var i = 0; i < PoliticaBloqueioConta.FalhasToleradas; i++)
        {
            (await EntrarAsync(cliente, email, SenhaErrada)).Dispose();
        }

        using (var entrou = await EntrarAsync(cliente, email, BancoPostgresFixture.Senha))
        {
            Assert.Equal(HttpStatusCode.OK, entrou.StatusCode);
        }

        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PrismaRhDbContext>();

        var usuario = await db.Usuarios.IgnoreQueryFilters().SingleAsync(u => u.Email == email);

        Assert.Equal(0, usuario.FalhasDeLogin);
        Assert.Null(usuario.UltimaFalhaEm);
    }

    /// <summary>
    /// ⚠️ O bloqueio é **por conta**, e não por IP: bloquear uma não pode
    /// atingir a outra. Se atingisse, um atacante derrubaria o login inteiro
    /// errando a senha de uma conta qualquer.
    /// </summary>
    [Fact]
    public async Task BloquearUmaContaNaoAlcancaOutra()
    {
        using var fabrica = new FabricaApiIsolada(banco.StringConexao);
        using var cliente = fabrica.CreateClient();

        var alvo = await UsuarioProprioAsync(fabrica, "alvo");
        var vizinha = await UsuarioProprioAsync(fabrica, "vizinha");

        for (var i = 0; i <= PoliticaBloqueioConta.FalhasToleradas; i++)
        {
            (await EntrarAsync(cliente, alvo, SenhaErrada)).Dispose();
        }

        using var doAlvo = await EntrarAsync(cliente, alvo, BancoPostgresFixture.Senha);
        Assert.Equal(HttpStatusCode.Unauthorized, doAlvo.StatusCode);

        using var daVizinha = await EntrarAsync(cliente, vizinha, BancoPostgresFixture.Senha);
        Assert.Equal(HttpStatusCode.OK, daVizinha.StatusCode);
    }

    /// <summary>
    /// O limite por IP **não foi substituído**. As duas defesas convivem, e é o
    /// que o `CLAUDE.md §24.18` pede: uma vê muitos IPs contra uma conta, a
    /// outra vê um IP contra muitas contas.
    /// </summary>
    [Fact]
    public async Task OLimitePorIpContinuaValendo()
    {
        using var fabrica = new FabricaApiIsolada(banco.StringConexao);
        using var cliente = fabrica.CreateClient();

        HttpStatusCode ultima = HttpStatusCode.Unauthorized;

        // Cada tentativa usa um e-mail DIFERENTE: nenhuma conta chega a
        // bloquear, entao o unico limite que pode cortar e o do IP.
        for (var i = 0; i < 15 && ultima != HttpStatusCode.TooManyRequests; i++)
        {
            using var r = await EntrarAsync(
                cliente, $"varredura-{i}-{Guid.CreateVersion7():N}@teste.com", SenhaErrada);

            ultima = r.StatusCode;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, ultima);
    }
}
