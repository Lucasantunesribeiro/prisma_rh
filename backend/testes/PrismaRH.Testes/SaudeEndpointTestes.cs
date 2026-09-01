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
