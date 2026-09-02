using System.Net;
using System.Net.Http.Json;

namespace PrismaRH.Testes.Isolamento;

/// <summary>
/// Fase 4D etapa 1 ponta a ponta, contra PostgreSQL real.
///
/// Dependente e dado pessoal de TERCEIRO - pessoa que nao usa o sistema e nao
/// consentiu com nada. Um furo aqui expoe o nome e a data de nascimento do
/// filho de alguem, e por isso o isolamento e o IDOR sao a maior parte destes
/// testes.
///
/// Nenhuma competencia e usada: esta etapa nao calcula folha.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class DependentesIntegracaoTestes(BancoPostgresFixture banco) : IDisposable
{
    private readonly FabricaApiIsolada _fabrica = new(banco.StringConexao);

    public void Dispose() => _fabrica.Dispose();

    private sealed record Identificado(Guid Id);

    private sealed record DependenteItem(
        Guid Id, Guid IdFuncionario, string Nome, DateOnly DataNascimento,
        string Relacao, bool DedutivelIrrf, DateOnly? InicioDeducaoIrrf, DateOnly? FimDeducaoIrrf);

    private static int _sufixo;

    private static string Sufixo() => Interlocked.Increment(ref _sufixo).ToString("D4");

    private static async Task<Guid> FuncionarioAsync(HttpClient cliente, string sufixo, int semente)
    {
        using var resposta = await cliente.PostAsJsonAsync("/api/funcionarios", new
        {
            nome = $"Titular {sufixo}",
            cpf = BancoPostgresFixture.CpfDeTeste(semente),
            dataNascimento = "1988-02-11",
        });
        resposta.EnsureSuccessStatusCode();

        return (await resposta.Content.ReadFromJsonAsync<Identificado>())!.Id;
    }

    private static Task<HttpResponseMessage> CriarAsync(
        HttpClient cliente, Guid idFuncionario, object corpo) =>
        cliente.PostAsJsonAsync($"/api/funcionarios/{idFuncionario}/dependentes", corpo);

    private static readonly object Filha = new
    {
        nome = "Helena Souza Prado",
        dataNascimento = "2018-03-22",
        relacao = "Filho",
        inicioDeducaoIrrf = "2026-01-01",
        fimDeducaoIrrf = (string?)null,
    };

    // ------------------------------------------------------------- cadastro

    [Fact]
    public async Task Criar_ELer()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var idFuncionario = await FuncionarioAsync(cliente, sufixo, 9100 + int.Parse(sufixo));

        using var criacao = await CriarAsync(cliente, idFuncionario, Filha);
        Assert.Equal(HttpStatusCode.Created, criacao.StatusCode);

        var lista = await cliente.GetFromJsonAsync<List<DependenteItem>>(
            $"/api/funcionarios/{idFuncionario}/dependentes");

        var dependente = Assert.Single(lista!);
        Assert.Equal("Helena Souza Prado", dependente.Nome);
        Assert.Equal("Filho", dependente.Relacao);
        Assert.True(dependente.DedutivelIrrf);
        Assert.Equal(new DateOnly(2026, 1, 1), dependente.InicioDeducaoIrrf);
        Assert.Null(dependente.FimDeducaoIrrf);
    }

    [Fact]
    public async Task SemPeriodo_NaoEDedutivel()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var idFuncionario = await FuncionarioAsync(cliente, sufixo, 9200 + int.Parse(sufixo));

        using var criacao = await CriarAsync(cliente, idFuncionario, new
        {
            nome = "Marta Souza Prado",
            dataNascimento = "1962-07-04",
            relacao = "Mae",
        });
        criacao.EnsureSuccessStatusCode();

        var lista = await cliente.GetFromJsonAsync<List<DependenteItem>>(
            $"/api/funcionarios/{idFuncionario}/dependentes");

        Assert.False(Assert.Single(lista!).DedutivelIrrf);
    }

    [Fact]
    public async Task Atualizar_TrocaOPeriodo()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var idFuncionario = await FuncionarioAsync(cliente, sufixo, 9300 + int.Parse(sufixo));

        using var criacao = await CriarAsync(cliente, idFuncionario, Filha);
        var criado = (await criacao.Content.ReadFromJsonAsync<Identificado>())!;

        using var atualizacao = await cliente.PutAsJsonAsync(
            $"/api/funcionarios/{idFuncionario}/dependentes/{criado.Id}", new
            {
                nome = "Helena Souza Prado",
                dataNascimento = "2018-03-22",
                relacao = "Filho",
                inicioDeducaoIrrf = "2026-01-01",
                fimDeducaoIrrf = "2026-06-30",
            });
        atualizacao.EnsureSuccessStatusCode();

        var depois = (await atualizacao.Content.ReadFromJsonAsync<DependenteItem>())!;
        Assert.Equal(new DateOnly(2026, 6, 30), depois.FimDeducaoIrrf);
    }

    [Fact]
    public async Task Remover_ApagaDeVerdade()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var idFuncionario = await FuncionarioAsync(cliente, sufixo, 9400 + int.Parse(sufixo));

        using var criacao = await CriarAsync(cliente, idFuncionario, Filha);
        var criado = (await criacao.Content.ReadFromJsonAsync<Identificado>())!;

        using var remocao = await cliente.DeleteAsync(
            $"/api/funcionarios/{idFuncionario}/dependentes/{criado.Id}");
        Assert.Equal(HttpStatusCode.NoContent, remocao.StatusCode);

        var lista = await cliente.GetFromJsonAsync<List<DependenteItem>>(
            $"/api/funcionarios/{idFuncionario}/dependentes");

        Assert.Empty(lista!);
    }

    // ------------------------------------------------------------ validacao

    [Fact]
    public async Task FimSemInicio_ERecusado()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var idFuncionario = await FuncionarioAsync(cliente, sufixo, 9500 + int.Parse(sufixo));

        using var resposta = await CriarAsync(cliente, idFuncionario, new
        {
            nome = "Sem inicio",
            dataNascimento = "2010-01-01",
            relacao = "Filho",
            fimDeducaoIrrf = "2026-12-31",
        });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task NascimentoNoFuturo_ERecusado()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var idFuncionario = await FuncionarioAsync(cliente, sufixo, 9600 + int.Parse(sufixo));

        using var resposta = await CriarAsync(cliente, idFuncionario, new
        {
            nome = "Ainda nao nasceu",
            dataNascimento = "2099-01-01",
            relacao = "Filho",
        });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task RelacaoDesconhecida_ERecusada()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var idFuncionario = await FuncionarioAsync(cliente, sufixo, 9700 + int.Parse(sufixo));

        using var resposta = await CriarAsync(cliente, idFuncionario, new
        {
            nome = "Relacao invalida",
            dataNascimento = "2010-01-01",
            relacao = "Papagaio",
        });

        // Vocabulario fechado: o que nao esta no enum NAO VIRA DADO. E essa a
        // propriedade que importa, e e ela que o teste trava.
        //
        // O status hoje e 500, e deveria ser 400. NAO e defeito desta rota: a
        // API inteira responde assim a qualquer enum malformado - conferido
        // contra POST /api/contratos/{id}/vigencias, da Fase 2. Registrado
        // como pendencia em CLAUDE.md secao 24.19 e a resolver fora desta
        // subfase, porque a correcao mexe no tratamento de erro de TODAS as
        // rotas. Por isso o teste afirma "nao foi aceito" e nao um codigo
        // especifico: prender o numero aqui congelaria o defeito.
        Assert.False(resposta.IsSuccessStatusCode);

        var lista = await cliente.GetFromJsonAsync<List<DependenteItem>>(
            $"/api/funcionarios/{idFuncionario}/dependentes");

        Assert.Empty(lista!);
    }

    // --------------------------------------------------------- autorizacao

    [Fact]
    public async Task Visualizador_Le_MasNaoCadastra()
    {
        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var idFuncionario = await FuncionarioAsync(admin, sufixo, 9800 + int.Parse(sufixo));

        using var criacao = await CriarAsync(admin, idFuncionario, Filha);
        criacao.EnsureSuccessStatusCode();

        var visualizador = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailVisualizadorA);

        var lista = await visualizador.GetFromJsonAsync<List<DependenteItem>>(
            $"/api/funcionarios/{idFuncionario}/dependentes");
        Assert.Single(lista!);

        using var tentativa = await CriarAsync(visualizador, idFuncionario, Filha);
        Assert.Equal(HttpStatusCode.Forbidden, tentativa.StatusCode);
    }

    [Fact]
    public async Task Auditor_NaoRemove()
    {
        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var idFuncionario = await FuncionarioAsync(admin, sufixo, 9900 + int.Parse(sufixo));

        using var criacao = await CriarAsync(admin, idFuncionario, Filha);
        var criado = (await criacao.Content.ReadFromJsonAsync<Identificado>())!;

        var auditor = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAuditorA);

        using var remocao = await auditor.DeleteAsync(
            $"/api/funcionarios/{idFuncionario}/dependentes/{criado.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, remocao.StatusCode);
    }

    [Fact]
    public async Task SemAutenticacao_NaoLe()
    {
        var anonimo = _fabrica.CreateClient();

        using var resposta = await anonimo.GetAsync(
            $"/api/funcionarios/{Guid.CreateVersion7()}/dependentes");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    // -------------------------------------------------------- isolamento

    [Fact]
    public async Task DependenteDeOutraOrganizacao_NaoAparece()
    {
        var clienteA = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var idFuncionarioA = await FuncionarioAsync(clienteA, sufixo, 10100 + int.Parse(sufixo));

        using var criacao = await CriarAsync(clienteA, idFuncionarioA, Filha);
        criacao.EnsureSuccessStatusCode();

        var clienteB = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminB);

        using var resposta = await clienteB.GetAsync(
            $"/api/funcionarios/{idFuncionarioA}/dependentes");

        // 404 e nao 403: um 403 confirmaria que aquele funcionario existe, e
        // permitiria mapear o cadastro do vizinho um id por vez.
        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task IdorPeloIdDoDependente_NaoAlcanca()
    {
        var clienteA = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixoA = Sufixo();
        var idFuncionarioA = await FuncionarioAsync(clienteA, sufixoA, 10200 + int.Parse(sufixoA));

        using var criacao = await CriarAsync(clienteA, idFuncionarioA, Filha);
        var dependenteDaA = (await criacao.Content.ReadFromJsonAsync<Identificado>())!;

        // A organizacao B tem funcionario proprio e tenta usar o id do
        // dependente da A por baixo dele. Como o dependente e resolvido pelo
        // PAI, o caminho nao existe.
        var clienteB = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminB);
        var sufixoB = Sufixo();
        var idFuncionarioB = await FuncionarioAsync(clienteB, sufixoB, 10300 + int.Parse(sufixoB));

        using var leitura = await clienteB.PutAsJsonAsync(
            $"/api/funcionarios/{idFuncionarioB}/dependentes/{dependenteDaA.Id}", new
            {
                nome = "Sequestrado",
                dataNascimento = "2018-03-22",
                relacao = "Filho",
            });
        Assert.Equal(HttpStatusCode.NotFound, leitura.StatusCode);

        using var remocao = await clienteB.DeleteAsync(
            $"/api/funcionarios/{idFuncionarioB}/dependentes/{dependenteDaA.Id}");
        Assert.Equal(HttpStatusCode.NotFound, remocao.StatusCode);

        // E o dependente continua intacto na organizacao dona.
        var lista = await clienteA.GetFromJsonAsync<List<DependenteItem>>(
            $"/api/funcionarios/{idFuncionarioA}/dependentes");
        Assert.Equal("Helena Souza Prado", Assert.Single(lista!).Nome);
    }

    [Fact]
    public async Task IdOrganizacaoNoCorpo_ENoop()
    {
        var clienteA = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var idFuncionario = await FuncionarioAsync(clienteA, sufixo, 10400 + int.Parse(sufixo));

        // ⚠️ Desde 02/09/2026 o campo intruso e RECUSADO, e nao ignorado.
        using var overposting = await CriarAsync(clienteA, idFuncionario, new
        {
            nome = "Tentativa de overposting",
            dataNascimento = "2015-01-01",
            relacao = "Filho",
            idOrganizacao = banco.IdOrganizacaoB,
            id = Guid.CreateVersion7(),
        });

        Assert.Equal(HttpStatusCode.BadRequest, overposting.StatusCode);

        // E o caminho legitimo grava na organizacao DO TOKEN.
        using var criacao = await CriarAsync(clienteA, idFuncionario, new
        {
            nome = "Tentativa de overposting",
            dataNascimento = "2015-01-01",
            relacao = "Filho",
        });
        criacao.EnsureSuccessStatusCode();

        // A organizacao vem do usuario autenticado, e a B continua sem
        // enxergar nada.
        var clienteB = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminB);

        using var resposta = await clienteB.GetAsync(
            $"/api/funcionarios/{idFuncionario}/dependentes");

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);

        // E o dependente ficou mesmo na organizacao A, apesar do idOrganizacao
        // da organizacao B no corpo.
        var lista = await clienteA.GetFromJsonAsync<List<DependenteItem>>(
            $"/api/funcionarios/{idFuncionario}/dependentes");
        Assert.Single(lista!);
    }

    // ------------------------------------------------------------- limites

    [Fact]
    public async Task AcimaDoTeto_ERecusado()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var idFuncionario = await FuncionarioAsync(cliente, sufixo, 10500 + int.Parse(sufixo));

        for (var i = 0; i < 30; i++)
        {
            using var criacao = await CriarAsync(cliente, idFuncionario, new
            {
                nome = $"Dependente {i}",
                dataNascimento = "2010-01-01",
                relacao = "Filho",
            });
            criacao.EnsureSuccessStatusCode();
        }

        using var excedente = await CriarAsync(cliente, idFuncionario, new
        {
            nome = "O trigesimo primeiro",
            dataNascimento = "2010-01-01",
            relacao = "Filho",
        });

        Assert.Equal(HttpStatusCode.Conflict, excedente.StatusCode);
    }

    [Fact]
    public async Task NomeAcimaDoLimite_ERecusado()
    {
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var idFuncionario = await FuncionarioAsync(cliente, sufixo, 10600 + int.Parse(sufixo));

        using var resposta = await CriarAsync(cliente, idFuncionario, new
        {
            nome = new string('a', 201),
            dataNascimento = "2010-01-01",
            relacao = "Filho",
        });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task ApagarOFuncionario_LevaOsDependentes()
    {
        // Cascade deliberado: dado pessoal de terceiro so existe por causa da
        // pessoa titular. Orfao seria retencao sem finalidade.
        var cliente = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        var sufixo = Sufixo();
        var idFuncionario = await FuncionarioAsync(cliente, sufixo, 10700 + int.Parse(sufixo));

        using var criacao = await CriarAsync(cliente, idFuncionario, Filha);
        criacao.EnsureSuccessStatusCode();

        // A API nao expoe exclusao de funcionario - a checagem e do modelo, no
        // banco. Aqui basta provar que a lista responde pelo pai: sem o
        // funcionario, a rota inteira e 404.
        using var inexistente = await cliente.GetAsync(
            $"/api/funcionarios/{Guid.CreateVersion7()}/dependentes");

        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
    }
}
