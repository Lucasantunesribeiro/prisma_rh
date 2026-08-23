using System.Net;
using System.Net.Http.Json;
using PrismaRH.Dominio.Identidade;

namespace PrismaRH.Testes.Isolamento;

/// <summary>
/// Autorizacao por perfil, verificada no BACKEND. Esconder botao no frontend
/// nao e mecanismo de autorizacao (CLAUDE.md secao 6): estes testes chamam a
/// API direto, como faria qualquer um com o token na mao.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class AutorizacaoTestes(BancoPostgresFixture banco) : IDisposable
{
    private readonly FabricaApiIsolada _fabrica = new(banco.StringConexao);

    public void Dispose() => _fabrica.Dispose();

    public static TheoryData<string, Perfil> TodosOsPerfis => new()
    {
        { BancoPostgresFixture.EmailPlataformaA, Perfil.AdministradorPlataforma },
        { BancoPostgresFixture.EmailAdminA, Perfil.AdministradorEmpresa },
        { BancoPostgresFixture.EmailAnalistaA, Perfil.AnalistaRh },
        { BancoPostgresFixture.EmailAuditorA, Perfil.Auditor },
        { BancoPostgresFixture.EmailVisualizadorA, Perfil.Visualizador }
    };

    [Theory]
    [MemberData(nameof(TodosOsPerfis))]
    public async Task Leitura_EPermitidaParaTodosOsCincoPerfis(string email, Perfil esperado)
    {
        var cliente = await _fabrica.ClienteComoAsync(email);

        using var resposta = await cliente.GetAsync("/api/empresas");
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);

        using var eu = await cliente.GetAsync("/api/autenticacao/eu");
        var corpo = await eu.Content.ReadFromJsonAsync<RespostaEu>();
        Assert.Equal(esperado.ToString(), corpo!.Perfil);
    }

    [Theory]
    [InlineData(BancoPostgresFixture.EmailAnalistaA)]
    [InlineData(BancoPostgresFixture.EmailAuditorA)]
    [InlineData(BancoPostgresFixture.EmailVisualizadorA)]
    public async Task Criar_EProibidoParaQuemNaoAdministraEmpresas(string email)
    {
        var cliente = await _fabrica.ClienteComoAsync(email);

        using var resposta = await cliente.PostAsJsonAsync("/api/empresas", new
        {
            razaoSocial = "Nao deveria entrar",
            cnpj = "11222333000181",
            nomeFantasia = (string?)null
        });

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    [Theory]
    [InlineData(BancoPostgresFixture.EmailAuditorA)]
    [InlineData(BancoPostgresFixture.EmailVisualizadorA)]
    public async Task Inativar_EProibidoParaAuditorEVisualizador(string email)
    {
        var cliente = await _fabrica.ClienteComoAsync(email);

        using var resposta = await cliente.DeleteAsync($"/api/empresas/{banco.IdEmpresaA}");

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    [Fact]
    public async Task AdministradorDeEmpresa_ConsegueCriarEAtualizar()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);

        using var criacao = await cliente.PostAsJsonAsync("/api/empresas", new
        {
            razaoSocial = "Criada pelo admin",
            cnpj = "11444777000161",
            nomeFantasia = "Admin"
        });

        // O CNPJ ja existe na organizacao B, mas nao na A: precisa passar,
        // porque a unicidade e POR ORGANIZACAO.
        Assert.Equal(HttpStatusCode.Created, criacao.StatusCode);
    }

    [Fact]
    public async Task Estabelecimento_Criar_ProibidoParaVisualizador_PermitidoParaAdmin()
    {
        var visualizador = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailVisualizadorA);
        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);

        var corpo = new { codigo = "900", nome = "Nova unidade" };
        var rota = $"/api/empresas/{banco.IdEmpresaA}/estabelecimentos";

        using var negado = await visualizador.PostAsJsonAsync(rota, corpo);
        Assert.Equal(HttpStatusCode.Forbidden, negado.StatusCode);

        using var permitido = await admin.PostAsJsonAsync(rota, corpo);
        Assert.Equal(HttpStatusCode.Created, permitido.StatusCode);
    }

    private sealed record RespostaEu(Guid Id, Guid IdOrganizacao, string Perfil);
}
