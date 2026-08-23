using System.Net;
using System.Net.Http.Json;

namespace PrismaRH.Testes.Isolamento;

/// <summary>
/// A fronteira entre clientes. Se algum destes testes falhar, nenhuma fase
/// seguinte do produto tem valor: os dados de folha de um cliente estariam
/// visiveis para outro.
///
/// Cada teste verifica PRESENCA e AUSENCIA. So verificar ausencia esconderia o
/// pior defeito possivel num filtro global: um filtro quebrado que devolve
/// lista vazia para todo mundo passa em qualquer teste que so procure o que
/// nao deveria aparecer.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class IsolamentoTestes(BancoPostgresFixture banco) : IDisposable
{
    private readonly FabricaApiIsolada _fabrica = new(banco.StringConexao);

    public void Dispose() => _fabrica.Dispose();

    private sealed record Pagina(int Total, List<ItemEmpresa> Itens);

    private sealed record ItemEmpresa(Guid Id, string RazaoSocial);

    [Fact]
    public async Task Listar_MostraApenasAsEmpresasDaPropriaOrganizacao()
    {
        var clienteA = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var pagina = await clienteA.GetFromJsonAsync<Pagina>("/api/empresas");

        Assert.NotNull(pagina);

        // PRESENCA: a empresa da propria organizacao aparece.
        Assert.Contains(pagina.Itens, e => e.Id == banco.IdEmpresaA);

        // AUSENCIA: a do vizinho, nao.
        Assert.DoesNotContain(pagina.Itens, e => e.Id == banco.IdEmpresaB);
    }

    [Fact]
    public async Task Listar_CadaOrganizacaoVeSomenteOSeuLado()
    {
        var clienteA = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var clienteB = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminB);

        var paginaA = await clienteA.GetFromJsonAsync<Pagina>("/api/empresas");
        var paginaB = await clienteB.GetFromJsonAsync<Pagina>("/api/empresas");

        Assert.Contains(paginaA!.Itens, e => e.Id == banco.IdEmpresaA);
        Assert.Contains(paginaB!.Itens, e => e.Id == banco.IdEmpresaB);

        Assert.DoesNotContain(paginaA.Itens, e => e.Id == banco.IdEmpresaB);
        Assert.DoesNotContain(paginaB.Itens, e => e.Id == banco.IdEmpresaA);
    }

    [Fact]
    public async Task Obter_EmpresaDeOutraOrganizacao_Devolve404_NaoUm403()
    {
        var clienteA = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);

        using var propria = await clienteA.GetAsync($"/api/empresas/{banco.IdEmpresaA}");
        using var alheia = await clienteA.GetAsync($"/api/empresas/{banco.IdEmpresaB}");

        Assert.Equal(HttpStatusCode.OK, propria.StatusCode);

        // 403 confirmaria que aquele id existe e permitiria mapear os recursos
        // do vizinho um id por vez. 404 nao conta nada.
        Assert.Equal(HttpStatusCode.NotFound, alheia.StatusCode);
    }

    [Fact]
    public async Task Atualizar_EmpresaDeOutraOrganizacao_NaoAlteraNada()
    {
        var clienteA = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var clienteB = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminB);

        using var tentativa = await clienteA.PutAsJsonAsync(
            $"/api/empresas/{banco.IdEmpresaB}",
            new { razaoSocial = "INVADIDA", nomeFantasia = "INVADIDA" });

        Assert.Equal(HttpStatusCode.NotFound, tentativa.StatusCode);

        // Prova que nao houve efeito colateral: o dono continua vendo o nome original.
        var paginaB = await clienteB.GetFromJsonAsync<Pagina>("/api/empresas");
        var empresaB = Assert.Single(paginaB!.Itens, e => e.Id == banco.IdEmpresaB);
        Assert.Equal("Empresa da B", empresaB.RazaoSocial);
    }

    [Fact]
    public async Task Inativar_EmpresaDeOutraOrganizacao_NaoTemEfeito()
    {
        var clienteA = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var clienteB = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminB);

        using var tentativa = await clienteA.DeleteAsync($"/api/empresas/{banco.IdEmpresaB}");
        Assert.Equal(HttpStatusCode.NotFound, tentativa.StatusCode);

        using var conferencia = await clienteB.GetAsync($"/api/empresas/{banco.IdEmpresaB}");
        Assert.Equal(HttpStatusCode.OK, conferencia.StatusCode);
    }

    [Fact]
    public async Task Criar_IgnoraOrganizacaoEnviadaNoCorpo_EUsaADoToken()
    {
        var clienteA = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var clienteB = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminB);

        // Tentativa classica: mandar o tenant do vizinho no corpo.
        using var criacao = await clienteA.PostAsJsonAsync("/api/empresas", new
        {
            razaoSocial = "Criada por A",
            cnpj = "34028316000103",
            nomeFantasia = (string?)null,
            idOrganizacao = banco.IdOrganizacaoB
        });

        Assert.Equal(HttpStatusCode.Created, criacao.StatusCode);
        var criada = await criacao.Content.ReadFromJsonAsync<ItemEmpresa>();

        // Ficou com A, nao com B.
        var paginaA = await clienteA.GetFromJsonAsync<Pagina>("/api/empresas");
        Assert.Contains(paginaA!.Itens, e => e.Id == criada!.Id);

        var paginaB = await clienteB.GetFromJsonAsync<Pagina>("/api/empresas");
        Assert.DoesNotContain(paginaB!.Itens, e => e.Id == criada!.Id);
    }

    [Fact]
    public async Task Estabelecimentos_DeEmpresaAlheia_Devolve404()
    {
        var clienteA = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);

        using var propria = await clienteA.GetAsync($"/api/empresas/{banco.IdEmpresaA}/estabelecimentos");
        using var alheia = await clienteA.GetAsync($"/api/empresas/{banco.IdEmpresaB}/estabelecimentos");

        Assert.Equal(HttpStatusCode.OK, propria.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, alheia.StatusCode);
    }

    [Fact]
    public async Task SemToken_NaoEnxergaNada()
    {
        var anonimo = _fabrica.CreateClient();

        using var resposta = await anonimo.GetAsync("/api/empresas");

        // Falha FECHADO: 401, nunca uma lista vazia com status 200 - que
        // pareceria "nao ha empresas" em vez de "voce nao esta autenticado".
        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }
}
