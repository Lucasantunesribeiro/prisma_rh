using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using PrismaRH.Infraestrutura;

namespace PrismaRH.Testes;

/// <summary>
/// Sobe a API em memoria para os testes de integracao da fundacao.
/// A string de conexao aponta de proposito para um endereco inacessivel: os testes
/// precisam ser determinsticos, independentemente de existir ou nao um PostgreSQL
/// em execucao na maquina que roda a suite.
/// </summary>
public sealed class FabricaApiTestes : WebApplicationFactory<Program>
{
    public const string StringConexaoInacessivel =
        "Host=127.0.0.1;Port=1;Database=prisma_rh_testes;Username=testes;Password=testes;Timeout=1;Command Timeout=1";

    private static string NomeVariavelAmbiente => $"ConnectionStrings__{ConfiguracaoInfraestrutura.NomeConexao}";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // A API le a string de conexao durante o registro dos servicos, antes de qualquer
        // ConfigureAppConfiguration desta fabrica. A variavel de ambiente entra na
        // configuracao do proprio WebApplicationBuilder e tem prioridade sobre o
        // appsettings.Development.json, entao e a forma confiavel de sobrescrever aqui.
        Environment.SetEnvironmentVariable(NomeVariavelAmbiente, StringConexaoInacessivel);

        builder.UseEnvironment(Environments.Development);

        return base.CreateHost(builder);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Environment.SetEnvironmentVariable(NomeVariavelAmbiente, null);
        }

        base.Dispose(disposing);
    }
}
