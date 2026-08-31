using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrismaRH.Dominio.Auditoria;
using PrismaRH.Infraestrutura.Persistencia;
using PrismaRH.Testes.Integracoes;

namespace PrismaRH.Testes.Isolamento;

/// <summary>
/// A consulta de CNPJ na BrasilAPI (Fase 8), contra PostgreSQL real.
///
/// Os testes de `ConsultaCnpjBrasilApiTestes` provam o cliente. Estes provam o
/// que so o sistema inteiro responde: **quem pode consultar, o que fica na
/// trilha, se a empresa da vizinha aparece, e se o cadastro manual sobrevive ao
/// parceiro fora do ar.**
///
/// Nenhum deles encosta na internet: o ultimo elo do `HttpClient` e trocado por
/// um duble, e todo o resto - guarda, redirect, teto, parsing, autorizacao,
/// limite e auditoria - e o codigo de producao.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class IntegracaoCnpjHttpTestes(BancoPostgresFixture banco)
{
    /// <summary>CNPJ da empresa da organizacao A, ja cadastrada.</summary>
    private const string CnpjDaA = "11222333000181";

    /// <summary>CNPJ da empresa da organizacao B. Para a A, e de estranho.</summary>
    private const string CnpjDaB = "11444777000161";

    /// <summary>Valido, e de ninguem no Prisma RH.</summary>
    private const string CnpjDeFora = "99999999000191";

    private sealed record DadosItem(
        string RazaoSocial, string? NomeFantasia, string SituacaoCadastral, bool AtivaNaReceita);

    private sealed record ConsultaItem(
        string Situacao, string Mensagem, DadosItem? Dados, bool JaCadastrada);

    private FabricaApiIsolada Fabrica(ParceiroFalso parceiro) =>
        new(banco.StringConexao, () => parceiro);

    private static Task<HttpResponseMessage> Consultar(HttpClient cliente, string cnpj) =>
        cliente.PostAsJsonAsync("/api/integracoes/cnpj/consultas", new { cnpj });

    // ---------------------------------------------------------------- permissoes

    [Fact]
    public async Task SemTokenNaoConsulta()
    {
        using var fabrica = Fabrica(ParceiroFalso.ComJson(ParceiroFalso.RespostaValida));
        using var cliente = fabrica.CreateClient();

        using var resposta = await Consultar(cliente, CnpjDaA);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    /// <summary>
    /// A consulta so serve ao formulario de empresa, e cadastrar empresa e
    /// `AdministrarEmpresas`. Dar a mais gente seria ampliar quem consegue
    /// gastar a cota de um servico de terceiro sem precisar
    /// (`CLAUDE.md secao 24.4`).
    /// </summary>
    [Theory]
    [InlineData(BancoPostgresFixture.EmailAnalistaA)]
    [InlineData(BancoPostgresFixture.EmailAuditorA)]
    [InlineData(BancoPostgresFixture.EmailVisualizadorA)]
    public async Task QuemNaoAdministraEmpresasRecebe403(string email)
    {
        using var fabrica = Fabrica(ParceiroFalso.ComJson(ParceiroFalso.RespostaValida));
        using var cliente = await fabrica.ClienteComoAsync(email);

        using var resposta = await Consultar(cliente, CnpjDaA);

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    [Fact]
    public async Task AdministradorDaEmpresaConsulta()
    {
        using var fabrica = Fabrica(ParceiroFalso.ComJson(ParceiroFalso.RespostaValida));
        using var cliente = await fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);

        using var resposta = await Consultar(cliente, CnpjDeFora);
        var corpo = await resposta.Content.ReadFromJsonAsync<ConsultaItem>();

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("Encontrada", corpo!.Situacao);
        Assert.Equal("INDUSTRIA EXEMPLO S.A.", corpo.Dados!.RazaoSocial);
        Assert.True(corpo.Dados.AtivaNaReceita);
    }

    // ----------------------------------------------------------------- validacao

    /// <summary>
    /// Os digitos sao conferidos **antes** de qualquer chamada externa. Nao e so
    /// cortesia com a cota alheia: e o que garante que so um `Cnpj` valido
    /// chega perto da montagem da URL.
    /// </summary>
    [Theory]
    [InlineData("123")]
    [InlineData("11222333000180")]
    [InlineData("")]
    [InlineData("nao-e-um-cnpj")]
    [InlineData("../../etc/passwd")]
    [InlineData("11222333000181/../../admin")]
    public async Task CnpjInvalidoNemChegaNoParceiro(string cnpj)
    {
        var parceiro = ParceiroFalso.ComJson(ParceiroFalso.RespostaValida);

        using var fabrica = Fabrica(parceiro);
        using var cliente = await fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);

        using var resposta = await Consultar(cliente, cnpj);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Empty(parceiro.Chamadas);
    }

    // -------------------------------------------------------------- multiempresa

    /// <summary>
    /// ⚠️ O teste de isolamento desta fase.
    ///
    /// `jaCadastrada` responde "ja existe **nesta** organizacao", e nunca "existe
    /// em alguma". Se respondesse a segunda coisa, um administrador conseguiria
    /// descobrir a carteira de clientes da concorrente um CNPJ por vez - sem ler
    /// um unico dado dela.
    /// </summary>
    [Fact]
    public async Task AEmpresaDaVizinhaNaoContaComoJaCadastrada()
    {
        using var fabrica = Fabrica(ParceiroFalso.ComJson(ParceiroFalso.RespostaValida));

        using var daA = await fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        using var daB = await fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminB);

        using var propria = await Consultar(daA, CnpjDaA);
        using var doVizinho = await Consultar(daA, CnpjDaB);
        using var doDono = await Consultar(daB, CnpjDaB);

        Assert.True((await propria.Content.ReadFromJsonAsync<ConsultaItem>())!.JaCadastrada);

        // O mesmo CNPJ: falso para quem nao e dono, verdadeiro para quem e.
        Assert.False((await doVizinho.Content.ReadFromJsonAsync<ConsultaItem>())!.JaCadastrada);
        Assert.True((await doDono.Content.ReadFromJsonAsync<ConsultaItem>())!.JaCadastrada);
    }

    // ------------------------------------------------ o parceiro se comportando mal

    /// <summary>
    /// ⚠️ A propriedade mais importante da fase inteira.
    ///
    /// `CLAUDE.md secao 1`: o Prisma RH nao depende de outro sistema para
    /// funcionar. Com a BrasilAPI fora do ar, a rota responde **200 com o motivo
    /// dentro** - e nao 502. Um erro HTTP viraria tela quebrada; o que se quer e
    /// um aviso, com o formulario manual intacto ao lado.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task ParceiroForaDoArNaoQuebraARota(HttpStatusCode status)
    {
        using var fabrica = Fabrica(ParceiroFalso.ComStatus(status));
        using var cliente = await fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);

        using var resposta = await Consultar(cliente, CnpjDeFora);
        var corpo = await resposta.Content.ReadFromJsonAsync<ConsultaItem>();

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("Indisponivel", corpo!.Situacao);
        Assert.Null(corpo.Dados);
    }

    [Fact]
    public async Task RespostaMalformadaDoParceiroNaoVazaParaATela()
    {
        using var fabrica = Fabrica(ParceiroFalso.ComJson("<html>portal em manutencao</html>"));
        using var cliente = await fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);

        using var resposta = await Consultar(cliente, CnpjDeFora);
        var corpo = await resposta.Content.ReadFromJsonAsync<ConsultaItem>();

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("Indisponivel", corpo!.Situacao);

        // Nem o corpo do parceiro, nem o nome dele, aparecem para o usuario.
        Assert.DoesNotContain("html", corpo.Mensagem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RedirectDoParceiroParaEnderecoInternoNaoEExecutado()
    {
        var parceiro = ParceiroFalso.QueRedirecionaPara("https://169.254.169.254/latest/meta-data/");

        using var fabrica = Fabrica(parceiro);
        using var cliente = await fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);

        using var resposta = await Consultar(cliente, CnpjDeFora);
        var corpo = await resposta.Content.ReadFromJsonAsync<ConsultaItem>();

        Assert.Equal("Indisponivel", corpo!.Situacao);

        // A prova: o segundo destino nunca virou requisicao.
        Assert.Single(parceiro.Chamadas);
    }

    // ------------------------------------------------------------------ auditoria

    /// <summary>
    /// Enviar dado para fora e decisao de privacidade, e o Security Gate da Fase
    /// 8 pede **registro do que foi enviado**. O evento guarda o CNPJ que saiu
    /// da nossa rede, o resultado e a origem.
    /// </summary>
    [Fact]
    public async Task AConsultaFicaNaTrilhaDeAuditoria()
    {
        using var fabrica = Fabrica(ParceiroFalso.ComJson(ParceiroFalso.RespostaValida));
        using var cliente = await fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);

        using var resposta = await Consultar(cliente, CnpjDeFora);
        resposta.EnsureSuccessStatusCode();

        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PrismaRhDbContext>();

        var evento = await db.EventosAuditoria
            .IgnoreQueryFilters()
            .Where(e => e.Acao == AcaoAuditada.CnpjConsultado && e.IdOrganizacao == banco.IdOrganizacaoA)
            .OrderByDescending(e => e.OcorridoEm)
            .FirstAsync(CancellationToken.None);

        Assert.Equal(EntidadeAuditada.ConsultaCnpj, evento.Entidade);
        Assert.Contains(CnpjDeFora, evento.Contexto!, StringComparison.Ordinal);
        Assert.Contains("origem=brasilapi", evento.Contexto, StringComparison.Ordinal);
        Assert.Contains("Receita Federal", evento.Descricao, StringComparison.Ordinal);

        // O identificador de correlacao do log tecnico e o mesmo da trilha: e
        // por ele que se sai de um e se chega no outro.
        Assert.NotEqual(Guid.Empty, evento.IdEntidade);
    }

    // ---------------------------------------------------------------------- cache

    /// <summary>
    /// A segunda consulta ao mesmo CNPJ nao sai da nossa rede - e a trilha diz
    /// isso com todas as letras. Registrar as duas com a mesma frase faria a
    /// auditoria afirmar um envio que nao houve.
    /// </summary>
    [Fact]
    public async Task ASegundaConsultaVemDoCacheEATrilhaRegistraIsso()
    {
        var parceiro = ParceiroFalso.ComJson(ParceiroFalso.RespostaValida);

        using var fabrica = Fabrica(parceiro);
        using var cliente = await fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);

        using (var primeira = await Consultar(cliente, CnpjDeFora))
        {
            primeira.EnsureSuccessStatusCode();
        }

        using (var segunda = await Consultar(cliente, CnpjDeFora))
        {
            segunda.EnsureSuccessStatusCode();
        }

        Assert.Single(parceiro.Chamadas);

        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PrismaRhDbContext>();

        var origens = await db.EventosAuditoria
            .IgnoreQueryFilters()
            .Where(e => e.Acao == AcaoAuditada.CnpjConsultado && e.IdOrganizacao == banco.IdOrganizacaoA)
            .OrderByDescending(e => e.OcorridoEm)
            .Take(2)
            .Select(e => e.Contexto!)
            .ToListAsync(CancellationToken.None);

        Assert.Contains(origens, c => c.Contains("origem=cache", StringComparison.Ordinal));
        Assert.Contains(origens, c => c.Contains("origem=brasilapi", StringComparison.Ordinal));
    }

    /// <summary>
    /// Indisponibilidade **nao** e guardada. Se fosse, a queda do parceiro
    /// sobreviveria ao proprio fim: ele voltaria ao ar e o Prisma RH continuaria
    /// dizendo que esta fora pelos dez minutos seguintes.
    /// </summary>
    [Fact]
    public async Task FalhaDoParceiroNaoEntraNoCache()
    {
        var tentativas = 0;

        var parceiro = new ParceiroFalso(_ =>
        {
            tentativas++;
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("{}"),
            };
        });

        using var fabrica = Fabrica(parceiro);
        using var cliente = await fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);

        using (var _ = await Consultar(cliente, CnpjDeFora))
        {
        }

        using (var _ = await Consultar(cliente, CnpjDeFora))
        {
        }

        Assert.Equal(2, tentativas);
    }

    // ---------------------------------------------------------------- rate limit

    /// <summary>
    /// Vinte por minuto, por organizacao. Cadastrar empresa e ato raro e
    /// deliberado: vinte cobrem o uso humano com folga e nao cobrem um script.
    ///
    /// O limite existe porque a cota do parceiro e **compartilhada** - sem ele,
    /// uma organizacao consome o que era de todas (`CLAUDE.md secao 24.18`).
    /// </summary>
    [Fact]
    public async Task AcimaDoLimiteAOrganizacaoRecebe429()
    {
        using var fabrica = Fabrica(ParceiroFalso.ComJson(ParceiroFalso.RespostaValida));
        using var cliente = await fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);

        var recusadas = 0;

        for (var tentativa = 0; tentativa < 25; tentativa++)
        {
            using var resposta = await Consultar(cliente, CnpjDeFora);

            if (resposta.StatusCode == HttpStatusCode.TooManyRequests)
            {
                recusadas++;
            }
        }

        Assert.Equal(5, recusadas);
    }

    /// <summary>
    /// ⚠️ O teste que separa "existe limite" de "existe limite POR ORGANIZACAO".
    ///
    /// Sao coisas diferentes, e a primeira passa mesmo quando a segunda esta
    /// quebrada. Um limite global faria a organizacao A, sozinha, deixar todas
    /// as outras sem consulta - exatamente o que o `CLAUDE.md secao 24.18`
    /// proibe: *"nenhuma organizacao pode causar custo ou indisponibilidade
    /// para outra"*.
    ///
    /// Aqui a A gasta a cota inteira dela, e a B - que nao consultou nada -
    /// continua sendo atendida.
    /// </summary>
    [Fact]
    public async Task OLimiteDeUmaOrganizacaoNaoAlcancaAOutra()
    {
        using var fabrica = Fabrica(ParceiroFalso.ComJson(ParceiroFalso.RespostaValida));

        using var daA = await fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        using var daB = await fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminB);

        for (var tentativa = 0; tentativa < 25; tentativa++)
        {
            using var _ = await Consultar(daA, CnpjDeFora);
        }

        using var esgotada = await Consultar(daA, CnpjDeFora);
        using var vizinha = await Consultar(daB, CnpjDeFora);

        Assert.Equal(HttpStatusCode.TooManyRequests, esgotada.StatusCode);
        Assert.Equal(HttpStatusCode.OK, vizinha.StatusCode);
    }
}
