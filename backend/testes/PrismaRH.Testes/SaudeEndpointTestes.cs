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

    [Fact]
    public async Task OpenApi_EstaDisponivelEmDesenvolvimento()
    {
        using var cliente = fabrica.CreateClient();

        using var resposta = await cliente.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }
}
