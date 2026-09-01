using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PrismaRH.Infraestrutura.Ia;
using PrismaRH.Infraestrutura.Integracoes;

namespace PrismaRH.Testes.Isolamento;

/// <summary>
/// Sobe a API apontando para o PostgreSQL do container.
///
/// A string de conexao vai por variavel de ambiente pelo mesmo motivo da
/// FabricaApiTestes: o Program le a configuracao durante o registro dos
/// servicos, antes de qualquer ConfigureAppConfiguration da fabrica.
/// </summary>
public sealed class FabricaApiIsolada(
    string stringConexao,
    Func<HttpMessageHandler>? parceiroExterno = null,
    Func<HttpMessageHandler>? provedorIa = null) : WebApplicationFactory<Program>
{
    private const string VariavelConexao = "ConnectionStrings__PrismaRh";
    private const string VariavelJwt = "Jwt__ChaveAssinatura";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        Environment.SetEnvironmentVariable(VariavelConexao, stringConexao);
        Environment.SetEnvironmentVariable(
            VariavelJwt, "chave-de-teste-de-isolamento-com-mais-de-32-caracteres");

        builder.UseEnvironment(Environments.Development);

        // Troca APENAS o ultimo elo - quem poe os bytes na rede. Tudo o que vem
        // antes continua sendo o codigo de producao: a guarda de destino, o
        // controle de redirect, o teto de corpo e o parsing.
        //
        // Se o teste substituisse a classe inteira por um duble, ele provaria
        // que o duble funciona. Nenhuma suite deste projeto encosta na internet.
        if (parceiroExterno is not null)
        {
            builder.ConfigureServices(servicos => servicos
                .AddHttpClient<ConsultaCnpjBrasilApi>()
                .ConfigurePrimaryHttpMessageHandler(parceiroExterno));
        }

        // O provedor de IA (Fase 11) entra pela mesma porta e pela mesma razao:
        // trocar so o ultimo elo deixa a guarda de destino, o prazo, o parsing e
        // o cache sendo exercitados de verdade.
        if (provedorIa is not null)
        {
            builder.ConfigureServices(servicos => servicos
                .AddHttpClient<ClienteGemini>(c => c.Timeout = OrcamentoIa.Prazo)
                .ConfigurePrimaryHttpMessageHandler(provedorIa));
        }

        return base.CreateHost(builder);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Environment.SetEnvironmentVariable(VariavelConexao, null);
            Environment.SetEnvironmentVariable(VariavelJwt, null);
        }

        base.Dispose(disposing);
    }

    /// <summary>Cliente ja autenticado como o e-mail informado.</summary>
    public async Task<HttpClient> ClienteComoAsync(string email, string senha = BancoPostgresFixture.Senha)
    {
        var cliente = CreateClient();

        using var resposta = await cliente.PostAsJsonAsync(
            "/api/autenticacao/entrar",
            new { email, senha });

        resposta.EnsureSuccessStatusCode();

        var sessao = await resposta.Content.ReadFromJsonAsync<RespostaSessao>();
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", sessao!.AccessToken);

        return cliente;
    }

    private sealed record RespostaSessao(string AccessToken);
}
