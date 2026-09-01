using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrismaRH.Dominio.Auditoria;
using PrismaRH.Infraestrutura.Ia;
using PrismaRH.Infraestrutura.Persistencia;
using PrismaRH.Testes.Integracoes;

namespace PrismaRH.Testes.Isolamento;

/// <summary>
/// O assistente de IA (Fase 11), pela HTTP e contra PostgreSQL real.
///
/// `AssistenteIaTestes` prova a camada de infraestrutura isolada. Estes provam o
/// que só o sistema inteiro responde: **quem pode pedir explicação, se a
/// inconsistência da vizinha aparece, o que fica na trilha, e se a tela continua
/// funcionando com o provedor fora do ar.**
///
/// Nenhum deles encosta na internet — o último elo do `HttpClient` é trocado, e
/// guarda de destino, prazo, parsing, autorização, cache e auditoria continuam
/// sendo o código de produção.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class AssistenteHttpTestes(BancoPostgresFixture banco)
{
    private sealed record Identificado(Guid Id);

    private sealed record ExplicacaoItem(
        string Situacao, string Texto, bool GeradoPorIa, bool DoCache, string Aviso);

    private sealed record DisponivelItem(bool Disponivel);

    private sealed record ResultadoBruto(Guid Id, string Codigo, string Matricula);

    private sealed record ExecucaoItem(Guid Id, int TotalResultados, List<ResultadoBruto>? Resultados);

    private sealed record FolhaResumo(Guid Id, string Competencia);

    private static int _sufixo;

    private static int Semente() => 80_000 + (Interlocked.Increment(ref _sufixo) * 10);

    private static string Gerada(string texto) => JsonSerializer.Serialize(new
    {
        candidates = new[] { new { content = new { parts = new[] { new { text = texto } } } } },
        usageMetadata = new { totalTokenCount = 140 },
    });

    private static ParceiroFalso ProvedorQueResponde(string texto = "Esta pessoa foi desligada antes do fechamento.")
        => ParceiroFalso.ComJson(Gerada(texto));

    /// <summary>
    /// A fábrica com a chave presente — sem ela o cliente nem sai da máquina, e
    /// os testes de autorização passariam sem provar nada.
    /// </summary>
    private FabricaApiIsolada Fabrica(ParceiroFalso provedor)
    {
        Environment.SetEnvironmentVariable(ClienteGemini.VariavelChave, "chave-de-teste");

        return new FabricaApiIsolada(banco.StringConexao, null, () => provedor);
    }

    private static Task<HttpResponseMessage> Explicar(HttpClient cliente, Guid id) =>
        cliente.PostAsync($"/api/assistente/inconsistencias/{id}/explicacao", null);

    // ------------------------------------------------------------- permissões

    [Fact]
    public async Task SemTokenNaoExplica()
    {
        using var fabrica = Fabrica(ProvedorQueResponde());
        using var cliente = fabrica.CreateClient();

        using var resposta = await Explicar(cliente, Guid.CreateVersion7());

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    /// <summary>
    /// ⚠️ Pedir explicação **gasta cota de um serviço que cobra por token**.
    /// Por isso a política é `ProcessarFolha` — quem trata inconsistência — e
    /// não leitura geral: Auditor e Visualizador leem o achado do motor
    /// determinístico, que é a informação que importa (`CLAUDE.md §24.4`).
    /// </summary>
    [Theory]
    [InlineData(BancoPostgresFixture.EmailAuditorI)]
    [InlineData(BancoPostgresFixture.EmailVisualizadorA)]
    public async Task QuemNaoProcessaFolhaRecebe403(string email)
    {
        using var fabrica = Fabrica(ProvedorQueResponde());
        using var cliente = await fabrica.ClienteComoAsync(email);

        using var resposta = await Explicar(cliente, Guid.CreateVersion7());

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    // -------------------------------------------------------------- isolamento

    /// <summary>
    /// ⚠️ **O teste de isolamento da fase.**
    ///
    /// A inconsistência é da organização A. A analista de B pede a explicação
    /// dela com o id na mão — e recebe **404, não 403**, pelo motivo de sempre:
    /// um 403 confirmaria que aquele id existe (`§24.5`).
    ///
    /// E o mais importante: **o provedor nunca é chamado.** O filtro global
    /// barra antes, então nem sequer há uma requisição de onde o dado da A
    /// poderia sair. O isolamento é arquitetural, não depende de o modelo se
    /// comportar (`§37.5`).
    /// </summary>
    [Fact]
    public async Task InconsistenciaDaVizinhaDevolve404ENaoChamaOProvedor()
    {
        var provedor = ProvedorQueResponde();

        using var fabrica = Fabrica(provedor);
        using var admin = await fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminI);

        var id = await InconsistenciaAsync(admin);

        // A propria organizacao consegue - a inconsistencia existe mesmo.
        using var daDona = await Explicar(admin, id);
        Assert.Equal(HttpStatusCode.OK, daDona.StatusCode);

        var chamadasAteAqui = provedor.Chamadas.Count;
        Assert.Equal(1, chamadasAteAqui);

        using var vizinha = await fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        using var daVizinha = await Explicar(vizinha, id);

        Assert.Equal(HttpStatusCode.NotFound, daVizinha.StatusCode);

        // ⚠️ Nenhuma chamada nova: a IA nem chegou a ser acionada.
        Assert.Equal(chamadasAteAqui, provedor.Chamadas.Count);
    }

    // ------------------------------------------------------------- funcionando

    [Fact]
    public async Task ExplicacaoVemRotuladaComoGeradaPorIa()
    {
        using var fabrica = Fabrica(ProvedorQueResponde("A pessoa saiu antes do fechamento da folha."));
        using var admin = await fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminI);

        var id = await InconsistenciaAsync(admin);

        using var resposta = await Explicar(admin, id);
        resposta.EnsureSuccessStatusCode();

        var item = (await resposta.Content.ReadFromJsonAsync<ExplicacaoItem>())!;

        Assert.Equal("Respondeu", item.Situacao);
        Assert.Contains("antes do fechamento", item.Texto, StringComparison.Ordinal);

        // ⚠️ `CLAUDE.md §37.3`: toda saida e rotulada como gerada por IA e
        // passivel de erro. Sem o rotulo, o texto de maquina passa por
        // apuracao do sistema.
        Assert.True(item.GeradoPorIa);
        Assert.Contains("inteligencia artificial", item.Aviso, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ⚠️ **A propriedade que a fase inteira precisa preservar** (`CLAUDE.md §1`):
    /// o Prisma RH não depende de outro sistema para funcionar.
    ///
    /// Provedor fora do ar devolve **200 com o motivo dentro**, e não 502. A
    /// tela mostra um aviso; o achado do motor determinístico continua ali.
    /// </summary>
    [Fact]
    public async Task ProvedorForaDoArNaoQuebraATela()
    {
        using var fabrica = Fabrica(ParceiroFalso.ComStatus(HttpStatusCode.ServiceUnavailable));
        using var admin = await fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminI);

        var id = await InconsistenciaAsync(admin);

        using var resposta = await Explicar(admin, id);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);

        var item = (await resposta.Content.ReadFromJsonAsync<ExplicacaoItem>())!;

        Assert.Equal("Indisponivel", item.Situacao);
        Assert.False(item.GeradoPorIa);
        Assert.Empty(item.Texto);

        // A inconsistencia continua legivel pela rota normal.
        using var listagem = await admin.GetAsync("/api/inconsistencias?tamanho=5");
        listagem.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task DisponivelRespondeSemGastarChamada()
    {
        var provedor = ProvedorQueResponde();

        using var fabrica = Fabrica(provedor);
        using var cliente = await fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAuditorA);

        using var resposta = await cliente.GetAsync("/api/assistente/disponivel");
        resposta.EnsureSuccessStatusCode();

        var item = (await resposta.Content.ReadFromJsonAsync<DisponivelItem>())!;

        Assert.True(item.Disponivel);

        // Perguntar se existe nao consome cota.
        Assert.Empty(provedor.Chamadas);
    }

    // --------------------------------------------------------------- custo

    /// <summary>
    /// ⚠️ Cobrança por token torna o abuso **lucrativo para quem ataca e caro
    /// para quem mantém** (Security Gate da Fase 11, item 11). O limite existe
    /// por isso.
    ///
    /// E ele é **por organização**, não por IP: num sistema multiempresa com
    /// cota compartilhada, o gasto de um tenant é problema de todos
    /// (`CLAUDE.md §24.18`). O teste prova as duas metades — a organização que
    /// estourou é barrada, e a vizinha continua atendida no mesmo instante.
    /// </summary>
    [Fact]
    public async Task OLimiteEPorOrganizacaoENaoAlcancaAVizinha()
    {
        using var fabrica = Fabrica(ProvedorQueResponde());
        using var admin = await fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminI);

        var id = await InconsistenciaAsync(admin);

        // A partir da segunda, todas vem do cache - o limitador e middleware e
        // conta antes de o handler rodar, entao o provedor e chamado uma vez so.
        HttpStatusCode ultima = HttpStatusCode.OK;

        for (var i = 0; i < OrcamentoIa.MaximoChamadasPorHora + 1; i++)
        {
            using var r = await Explicar(admin, id);
            ultima = r.StatusCode;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, ultima);

        // A vizinha nao paga pelo excesso da outra.
        using var vizinha = await fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);
        using var daVizinha = await Explicar(vizinha, Guid.CreateVersion7());

        Assert.Equal(HttpStatusCode.NotFound, daVizinha.StatusCode);
    }

    // --------------------------------------------------------------- auditoria

    /// <summary>
    /// ⚠️ `CLAUDE.md §37.5` manda registrar **quando uma sugestão de IA
    /// participa de uma decisão**. A razão é direta: meses depois, ao revisar
    /// por que uma divergência foi justificada de determinado jeito, precisa
    /// ficar claro se havia um texto de máquina na tela naquele momento.
    ///
    /// O que o teste também prova é o limite: o evento registra **que** houve —
    /// modelo, tokens, correlação — e **nunca o texto**. Guardar a saída criaria
    /// uma segunda cópia de conteúdo derivado de dado do tenant.
    /// </summary>
    [Fact]
    public async Task AExplicacaoEAuditadaSemGuardarOTexto()
    {
        const string Segredo = "ESTE TEXTO NAO PODE APARECER NA TRILHA";

        using var fabrica = Fabrica(ProvedorQueResponde(Segredo));
        using var admin = await fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminI);

        var id = await InconsistenciaAsync(admin);

        using var resposta = await Explicar(admin, id);
        resposta.EnsureSuccessStatusCode();

        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PrismaRhDbContext>();

        var evento = await db.EventosAuditoria
            .IgnoreQueryFilters()
            .Where(e => e.Acao == AcaoAuditada.ExplicacaoIaGerada && e.IdEntidade == id)
            .SingleAsync();

        Assert.Equal(EntidadeAuditada.ExplicacaoIa, evento.Entidade);
        Assert.NotNull(evento.IdUsuario);
        Assert.Contains(OrcamentoIa.Modelo, evento.Contexto!, StringComparison.Ordinal);
        Assert.Contains("tokens=", evento.Contexto, StringComparison.Ordinal);

        // ⚠️ O texto do modelo NAO esta na trilha.
        Assert.DoesNotContain(Segredo, evento.Contexto, StringComparison.Ordinal);
        Assert.DoesNotContain(Segredo, evento.Descricao, StringComparison.Ordinal);
    }

    /// <summary>
    /// A segunda leitura vem do cache: não chama o provedor **e não gera evento
    /// novo**. Auditar de novo a mesma explicação encheria a trilha de linhas
    /// que não representam decisão nova.
    /// </summary>
    [Fact]
    public async Task SegundaLeituraNaoGastaChamadaNemGeraEventoNovo()
    {
        var provedor = ProvedorQueResponde();

        using var fabrica = Fabrica(provedor);
        using var admin = await fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminI);

        var id = await InconsistenciaAsync(admin);

        using (var primeira = await Explicar(admin, id))
        {
            primeira.EnsureSuccessStatusCode();
        }

        using var segunda = await Explicar(admin, id);
        segunda.EnsureSuccessStatusCode();

        var item = (await segunda.Content.ReadFromJsonAsync<ExplicacaoItem>())!;

        Assert.True(item.DoCache);
        Assert.Single(provedor.Chamadas);

        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PrismaRhDbContext>();

        var eventos = await db.EventosAuditoria
            .IgnoreQueryFilters()
            .CountAsync(e => e.Acao == AcaoAuditada.ExplicacaoIaGerada && e.IdEntidade == id);

        Assert.Equal(1, eventos);
    }

    // ------------------------------------------------------------------ cenário

    /// <summary>
    /// Produz uma inconsistência de verdade — mesmo caminho do
    /// `WorkflowHttpTestes`: calcula a folha com a pessoa ativa e cadastra o
    /// desligamento depois, sem recalcular.
    /// </summary>
    private async Task<Guid> InconsistenciaAsync(HttpClient admin)
    {
        var semente = Semente();
        var sufixo = semente.ToString("D6");

        // ⚠️ Competencia PROPRIA de cada teste. Abrir a mesma folha da mesma
        // empresa duas vezes devolve 409, e o teste falharia por colisao entre
        // testes - nao pelo que ele pretende provar.
        var ano = 2040 + ((semente - 80_000) / 10);
        var competencia = $"{ano}-08";
        var desligamento = $"{ano}-07-20";

        using (var rubrica = await admin.PostAsJsonAsync("/api/rubricas", new
        {
            codigo = "SAL",
            nome = "Salario base",
            tipo = "Provento",
            estrategia = "SalarioBaseProporcional",
        }))
        {
            Assert.True(rubrica.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict);
        }

        using var respostaCargo = await admin.PostAsJsonAsync("/api/cargos", new
        {
            codigo = $"IA{sufixo}",
            nome = $"Cargo assistente {sufixo}",
        });
        respostaCargo.EnsureSuccessStatusCode();
        var cargo = (await respostaCargo.Content.ReadFromJsonAsync<Identificado>())!;

        using var respostaFuncionario = await admin.PostAsJsonAsync("/api/funcionarios", new
        {
            nome = $"Assistente Pessoa {sufixo}",
            cpf = BancoPostgresFixture.CpfDeTeste(semente),
            dataNascimento = "1990-05-20",
        });
        respostaFuncionario.EnsureSuccessStatusCode();
        var funcionario = (await respostaFuncionario.Content.ReadFromJsonAsync<Identificado>())!;

        using var respostaContrato = await admin.PostAsJsonAsync(
            $"/api/funcionarios/{funcionario.Id}/contratos",
            new
            {
                idEmpresa = banco.IdEmpresaI,
                matricula = $"IA{sufixo}",
                dataAdmissao = "2030-01-01",
                salarioInicial = 3_000m,
                idCargo = cargo.Id,
                idEstabelecimento = banco.IdEstabelecimentoI,
                jornadaMensalHoras = 220,
            });
        respostaContrato.EnsureSuccessStatusCode();
        var contrato = (await respostaContrato.Content.ReadFromJsonAsync<Identificado>())!;

        using var abertura = await admin.PostAsJsonAsync(
            "/api/folhas", new { idEmpresa = banco.IdEmpresaI, competencia });
        abertura.EnsureSuccessStatusCode();
        var folha = (await abertura.Content.ReadFromJsonAsync<FolhaResumo>())!;

        using (var calculo = await admin.PostAsync($"/api/folhas/{folha.Id}/calcular", null))
        {
            calculo.EnsureSuccessStatusCode();
        }

        using (var desliga = await admin.PostAsJsonAsync(
            $"/api/contratos/{contrato.Id}/desligamento",
            new { dataDesligamento = desligamento, motivo = "PedidoDeDemissao" }))
        {
            desliga.EnsureSuccessStatusCode();
        }

        using var analise = await admin.PostAsync($"/api/folhas/{folha.Id}/analisar", null);
        analise.EnsureSuccessStatusCode();
        var execucao = (await analise.Content.ReadFromJsonAsync<ExecucaoItem>())!;

        return execucao.Resultados!
            .Single(r => r.Codigo == "DesligadoNaFolha" && r.Matricula == $"IA{sufixo}")
            .Id;
    }
}
