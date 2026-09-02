using System.Net;
using System.Net.Http.Json;
using PrismaRH.Api.Saude;
using PrismaRH.Infraestrutura;

namespace PrismaRH.Testes;

[Collection("api")]
public class SaudeEndpointTestes(FabricaApiTestes fabrica) : IClassFixture<FabricaApiTestes>
{
    [Fact]
    public async Task Health_RespondeComOContratoJsonEsperado()
    {
        using var cliente = fabrica.CreateClient();

        using var resposta = await cliente.GetAsync("/health");
        var conteudo = await resposta.Content.ReadFromJsonAsync<RespostaSaude>();

        Assert.NotNull(conteudo);
        Assert.False(string.IsNullOrWhiteSpace(conteudo.Status));
        Assert.Contains(conteudo.Verificacoes, v => v.Nome == ConfiguracaoInfraestrutura.NomeVerificacaoBanco);
    }

    [Fact]
    public async Task Health_ReportaBancoIndisponivelQuandoNaoHaConexao()
    {
        using var cliente = fabrica.CreateClient();

        using var resposta = await cliente.GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, resposta.StatusCode);

        var conteudo = await resposta.Content.ReadFromJsonAsync<RespostaSaude>();

        Assert.NotNull(conteudo);
        Assert.Equal(StatusSaude.Indisponivel, conteudo.Status);

        var banco = conteudo.Verificacoes
            .Single(v => v.Nome == ConfiguracaoInfraestrutura.NomeVerificacaoBanco);
        Assert.Equal(StatusSaude.Indisponivel, banco.Status);
    }

    /// <summary>
    /// ⚠️ O `/health` precisa dizer se o esquema do banco **bate com o código**,
    /// e não só se o banco responde.
    ///
    /// Este teste existe por causa de um incidente real em 02/09/2026: o código
    /// do bloqueio progressivo foi publicado sem a migration ser aplicada no
    /// Neon, o login passou a devolver 500 com `column u.bloqueado_ate does not
    /// exist`, e o `/health` continuou respondendo `saudavel` o tempo todo.
    ///
    /// Um diagnóstico que responde "saudável" durante uma indisponibilidade é
    /// pior que nenhum: ele desliga a suspeita.
    /// </summary>
    [Fact]
    public async Task Health_VerificaAsMigrationsEnaoSoAConexao()
    {
        using var cliente = fabrica.CreateClient();

        // Sem exigir sucesso: esta fixture aponta para um banco INALCANCAVEL de
        // proposito, e o /health responde 503 com o corpo completo - que e
        // justamente o que este teste quer ler.
        using var resposta = await cliente.GetAsync("/health");

        var conteudo = await resposta.Content.ReadFromJsonAsync<RespostaSaude>();

        Assert.NotNull(conteudo);

        Assert.Contains(
            conteudo!.Verificacoes,
            v => v.Nome == ConfiguracaoInfraestrutura.NomeVerificacaoMigrations);
    }

    /// <summary>
    /// ⚠️ Mudou na Fase 12: o documento OpenAPI **deixou de ser anonimo**.
    ///
    /// Ele nao ganhou `RequireAuthorization` - passou a ser coberto pela
    /// `FallbackPolicy`, criada porque `CLAUDE.md secao 24.4` manda negar por
    /// padrao. E o efeito e desejado: o documento descreve as 85 rotas com os
    /// esquemas de entrada, ou seja, o mapa do sistema.
    ///
    /// Ele so e mapeado em Development (`Program.cs`), entao em producao nao
    /// existe nem autenticado.
    /// </summary>
    [Fact]
    public async Task OpenApi_ExisteEmDesenvolvimentoMasNaoEAnonimo()
    {
        using var cliente = fabrica.CreateClient();

        using var resposta = await cliente.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }
}
