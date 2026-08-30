using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrismaRH.Dominio.Importacao;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Testes.Isolamento;

/// <summary>
/// Upload, preview e confirmacao (Fase 5, etapa 3), contra PostgreSQL real.
///
/// O foco destes testes nao e "o CSV foi lido" - isso a etapa 1 ja provou. E a
/// pergunta que so o sistema inteiro responde: **o cliente consegue mentir para
/// o backend?**
///
/// Organizacao E, exclusiva desta suite.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class ImportacaoHttpTestes(BancoPostgresFixture banco) : IDisposable
{
    private readonly FabricaApiIsolada _fabrica = new(banco.StringConexao);

    public void Dispose() => _fabrica.Dispose();

    private const string Preview = "/api/importacoes/funcionarios/preview";
    private const string Confirmar = "/api/importacoes/funcionarios/confirmar";

    private sealed record LinhaItem(
        int Linha, string? Nome, string? Cpf, DateOnly? DataNascimento, List<string> Erros);

    private sealed record PreviewItem(
        string NomeArquivo, long TamanhoBytes, string HashSha256,
        int Total, int Validas, int ComErro, bool Importavel,
        List<string> ErrosDoArquivo, List<LinhaItem> Linhas);

    private sealed record ConfirmacaoItem(
        Guid IdImportacao, string Status, string HashSha256,
        int Total, int Validas, int ComErro, int FuncionariosCriados, List<LinhaItem> Linhas);

    private static int _sufixo;

    private static string Sufixo() => Interlocked.Increment(ref _sufixo).ToString("D4");

    private Task<HttpClient> AdminAsync() =>
        _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminE);

    /// <summary>Monta um multipart com o conteudo dado, como o navegador faria.</summary>
    private static MultipartFormDataContent Arquivo(
        string conteudo, string nome = "funcionarios.csv")
    {
        var bytes = new UTF8Encoding(false).GetBytes(conteudo);
        var parte = new ByteArrayContent(bytes);

        parte.Headers.ContentType = new MediaTypeHeaderValue("text/csv");

        return new MultipartFormDataContent { { parte, "arquivo", nome } };
    }

    /// <summary>Um CSV valido, com CPFs unicos por sufixo.</summary>
    private static string CsvValido(string sufixo, int quantidade = 2)
    {
        var texto = new StringBuilder("nome;cpf;data de nascimento\n");

        for (var i = 0; i < quantidade; i++)
        {
            var cpf = BancoPostgresFixture.CpfDeTeste(90_000 + (int.Parse(sufixo) * 10) + i);

            texto.Append($"Importado {sufixo}-{i};{cpf};15/03/1990\n");
        }

        return texto.ToString();
    }

    private PrismaRhDbContext Contexto(IServiceScope escopo) =>
        escopo.ServiceProvider.GetRequiredService<PrismaRhDbContext>();

    // ------------------------------------------------------------- autorizacao

    [Fact]
    public async Task Analista_PODEImportar()
    {
        var analista = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAnalistaA);

        using var r = await analista.PostAsync(Preview, Arquivo(CsvValido(Sufixo())));

        // Manter cadastro e importar sao o mesmo trabalho: a politica
        // AdministrarPessoas cobre os dois.
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Theory]
    [InlineData("auditor")]
    [InlineData("visualizador")]
    public async Task AuditorEVisualizador_SaoBLOQUEADOS(string perfil)
    {
        var email = perfil == "auditor"
            ? BancoPostgresFixture.EmailAuditorA
            : BancoPostgresFixture.EmailVisualizadorA;

        var cliente = await _fabrica.ClienteComoAsync(email);

        using var previa = await cliente.PostAsync(Preview, Arquivo(CsvValido(Sufixo())));
        using var confirmacao = await cliente.PostAsync(Confirmar, Arquivo(CsvValido(Sufixo())));

        // 403, e nao 404: a politica corre ANTES do handler, entao o perfil
        // errado nem chega ao filtro global. Isso nao vaza nada - a resposta e
        // identica para qualquer arquivo.
        Assert.Equal(HttpStatusCode.Forbidden, previa.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, confirmacao.StatusCode);
    }

    [Fact]
    public async Task Anonimo_NaoImporta()
    {
        var anonimo = _fabrica.CreateClient();

        using var r = await anonimo.PostAsync(Preview, Arquivo(CsvValido(Sufixo())));

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    // ---------------------------------------------------------------- preview

    [Fact]
    public async Task Preview_NAOGravaNADA()
    {
        var admin = await AdminAsync();
        var sufixo = Sufixo();

        // Nome EXCLUSIVO deste teste: a organizacao E e compartilhada com os
        // outros testes da classe, que gravam de proposito. A afirmacao aqui e
        // sobre ESTE arquivo, e nao sobre a tabela inteira.
        var nome = $"preview-{sufixo}.csv";

        using var resposta = await admin.PostAsync(Preview, Arquivo(CsvValido(sufixo), nome));
        resposta.EnsureSuccessStatusCode();

        var previa = (await resposta.Content.ReadFromJsonAsync<PreviewItem>())!;

        Assert.True(previa.Importavel);
        Assert.Equal(2, previa.Validas);

        using var escopo = _fabrica.Services.CreateScope();
        var db = Contexto(escopo);

        // O ROADMAP e explicito: preview e validacao vem ANTES da persistencia.
        // Nem importacao, nem funcionario.
        Assert.Empty(await db.Importacoes.IgnoreQueryFilters()
            .Where(i => i.NomeOriginalArquivo == nome)
            .ToListAsync());

        Assert.Empty(await db.Funcionarios.IgnoreQueryFilters()
            .Where(f => f.Nome.StartsWith($"Importado {sufixo}"))
            .ToListAsync());
    }

    [Fact]
    public async Task Preview_MASCARAOCPF()
    {
        var admin = await AdminAsync();

        using var resposta = await admin.PostAsync(Preview, Arquivo(CsvValido(Sufixo())));
        var previa = (await resposta.Content.ReadFromJsonAsync<PreviewItem>())!;

        // CPF e dado altamente sensivel. A tela precisa identificar a linha, e
        // nao expor o documento inteiro de uma folha de funcionarios.
        Assert.All(previa.Linhas, l => Assert.Contains('*', l.Cpf!));
    }

    [Fact]
    public async Task PreviewInvalido_NaoViraRegistroHistorico()
    {
        var admin = await AdminAsync();
        var nome = $"invalido-{Sufixo()}.csv";

        using var resposta = await admin.PostAsync(
            Preview, Arquivo("nome;cpf;data de nascimento\nAna;123;30/02/1990\n", nome));

        resposta.EnsureSuccessStatusCode();
        var previa = (await resposta.Content.ReadFromJsonAsync<PreviewItem>())!;

        Assert.False(previa.Importavel);
        Assert.Equal(1, previa.ComErro);

        using var escopo = _fabrica.Services.CreateScope();

        // Preview que ninguem confirmou NAO vira historico - senao a tabela
        // viraria deposito de tentativa que nunca aconteceu. Nem sequer como
        // Recusada: a recusa so e gravada quando alguem CONFIRMA.
        Assert.Equal(0, await Contexto(escopo).Importacoes.IgnoreQueryFilters()
            .CountAsync(i => i.NomeOriginalArquivo == nome));
    }

    // ------------------------------------------------------------ confirmacao

    [Fact]
    public async Task Confirmacao_CriaOsFuncionariosEOVinculoDeORIGEM()
    {
        var admin = await AdminAsync();
        var sufixo = Sufixo();

        using var resposta = await admin.PostAsync(Confirmar, Arquivo(CsvValido(sufixo)));
        resposta.EnsureSuccessStatusCode();

        var confirmacao = (await resposta.Content.ReadFromJsonAsync<ConfirmacaoItem>())!;

        Assert.Equal("Aplicada", confirmacao.Status);
        Assert.Equal(2, confirmacao.FuncionariosCriados);

        using var escopo = _fabrica.Services.CreateScope();
        var db = Contexto(escopo);

        var criados = await db.Funcionarios.IgnoreQueryFilters()
            .Where(f => f.Nome.StartsWith($"Importado {sufixo}"))
            .ToListAsync();

        Assert.Equal(2, criados.Count);

        // A FK de origem REALMENTE preenchida, e apontando para a linha certa.
        Assert.All(criados, f => Assert.NotNull(f.IdLinhaImportacao));

        var linhas = await db.LinhasImportacao.IgnoreQueryFilters()
            .Where(l => l.IdImportacao == confirmacao.IdImportacao)
            .ToListAsync();

        Assert.All(criados, f => Assert.Contains(linhas, l => l.Id == f.IdLinhaImportacao));
    }

    [Fact]
    public async Task ArquivoALTERADOEntrePreviewEConfirmacao_ValeOREENVIADO()
    {
        var admin = await AdminAsync();
        var sufixo = Sufixo();

        // Preview com DOIS registros...
        using var previaResposta = await admin.PostAsync(Preview, Arquivo(CsvValido(sufixo, 2)));
        var previa = (await previaResposta.Content.ReadFromJsonAsync<PreviewItem>())!;

        Assert.Equal(2, previa.Total);

        // ...e confirmacao com UM. O servidor nao guardou o preview, entao nao
        // ha o que comparar: o que vale e o arquivo que chegou agora.
        using var resposta = await admin.PostAsync(Confirmar, Arquivo(CsvValido(sufixo, 1)));
        var confirmacao = (await resposta.Content.ReadFromJsonAsync<ConfirmacaoItem>())!;

        Assert.Equal(1, confirmacao.Total);
        Assert.Equal(1, confirmacao.FuncionariosCriados);

        // E o HASH gravado e o do arquivo REENVIADO, nao o do preview.
        Assert.NotEqual(previa.HashSha256, confirmacao.HashSha256);
    }

    [Fact]
    public async Task OClienteNAOConsegueDizerQuaisLinhasSaoValidas()
    {
        var admin = await AdminAsync();
        var sufixo = Sufixo();

        // Um arquivo com erro, acompanhado de campos que TENTAM afirmar que
        // esta tudo certo. Se o backend olhasse qualquer um deles, gravaria.
        var conteudo = new UTF8Encoding(false).GetBytes(
            "nome;cpf;data de nascimento\nMentiroso " + sufixo + ";123;30/02/1990\n");

        var parte = new ByteArrayContent(conteudo);
        parte.Headers.ContentType = new MediaTypeHeaderValue("text/csv");

        using var corpo = new MultipartFormDataContent
        {
            { parte, "arquivo", "mentira.csv" },
            { new StringContent("true"), "importavel" },
            { new StringContent("0"), "comErro" },
            { new StringContent("1"), "validas" },
            { new StringContent(new string('a', 64)), "hashSha256" },
        };

        using var resposta = await admin.PostAsync(Confirmar, corpo);
        resposta.EnsureSuccessStatusCode();

        var confirmacao = (await resposta.Content.ReadFromJsonAsync<ConfirmacaoItem>())!;

        // O backend refez tudo e recusou. Nenhum campo do cliente teve efeito.
        Assert.Equal("Recusada", confirmacao.Status);
        Assert.Equal(0, confirmacao.FuncionariosCriados);
        Assert.NotEqual(new string('a', 64), confirmacao.HashSha256);

        using var escopo = _fabrica.Services.CreateScope();

        Assert.Empty(await Contexto(escopo).Funcionarios.IgnoreQueryFilters()
            .Where(f => f.Nome.StartsWith($"Mentiroso {sufixo}"))
            .ToListAsync());
    }

    [Fact]
    public async Task TentarTrocarAOrganizacao_NAOTemEfeito()
    {
        var admin = await AdminAsync();
        var sufixo = Sufixo();

        var conteudo = new UTF8Encoding(false).GetBytes(CsvValido(sufixo, 1));
        var parte = new ByteArrayContent(conteudo);
        parte.Headers.ContentType = new MediaTypeHeaderValue("text/csv");

        using var corpo = new MultipartFormDataContent
        {
            { parte, "arquivo", "invasao.csv" },
            { new StringContent(banco.IdOrganizacaoC.ToString()), "idOrganizacao" },
        };

        using var resposta = await admin.PostAsync(Confirmar, corpo);
        resposta.EnsureSuccessStatusCode();

        var confirmacao = (await resposta.Content.ReadFromJsonAsync<ConfirmacaoItem>())!;

        using var escopo = _fabrica.Services.CreateScope();
        var db = Contexto(escopo);

        var importacao = await db.Importacoes.IgnoreQueryFilters()
            .SingleAsync(i => i.Id == confirmacao.IdImportacao);

        // O IdOrganizacao vem do USUARIO AUTENTICADO, sempre. O campo enviado
        // e ignorado porque nem sequer e lido.
        Assert.Equal(banco.IdOrganizacaoE, importacao.IdOrganizacao);
        Assert.NotEqual(banco.IdOrganizacaoC, importacao.IdOrganizacao);
    }

    [Fact]
    public async Task ConfirmacaoREPETIDA_NaoDuplicaEmSILENCIO()
    {
        var admin = await AdminAsync();
        var sufixo = Sufixo();
        var conteudo = CsvValido(sufixo);

        using var primeira = await admin.PostAsync(Confirmar, Arquivo(conteudo));
        var umaVez = (await primeira.Content.ReadFromJsonAsync<ConfirmacaoItem>())!;

        Assert.Equal("Aplicada", umaVez.Status);

        // Mesmo arquivo, de novo.
        using var segunda = await admin.PostAsync(Confirmar, Arquivo(conteudo));
        segunda.EnsureSuccessStatusCode();

        var duasVezes = (await segunda.Content.ReadFromJsonAsync<ConfirmacaoItem>())!;

        // RECUSADA, com motivo legivel - e nao 500 do indice unico, nem
        // duplicata em silencio. O CPF ja existe, e o relatorio diz em qual
        // linha.
        Assert.Equal("Recusada", duasVezes.Status);
        Assert.Equal(0, duasVezes.FuncionariosCriados);
        Assert.All(duasVezes.Linhas, l => Assert.Contains(l.Erros, e => e.Contains("Ja existe")));

        using var escopo = _fabrica.Services.CreateScope();

        Assert.Equal(2, await Contexto(escopo).Funcionarios.IgnoreQueryFilters()
            .CountAsync(f => f.Nome.StartsWith($"Importado {sufixo}")));
    }

    /// <summary>
    /// Tudo ou nada, com o erro detectado na VALIDACAO.
    ///
    /// Este teste prova a primeira camada: nada e gravado porque a validacao
    /// recusou antes de abrir escrita alguma. Ele NAO prova o rollback da
    /// transacao - para isso, ver
    /// <see cref="DuasConfirmacoesSIMULTANEAS_NaoDeixamEstadoPelaMetade"/>,
    /// que forca o banco a recusar depois da transacao aberta.
    /// </summary>
    [Fact]
    public async Task ErroDeValidacao_NaoGravaLinhaALGUMA()
    {
        var admin = await AdminAsync();
        var sufixo = Sufixo();

        // Cinco linhas validas, e a ULTIMA repete o CPF da primeira. Sem a
        // deteccao de duplicata dentro do arquivo, as quatro primeiras seriam
        // gravadas e a quinta quebraria - com a base ja pela metade.
        var cpf = BancoPostgresFixture.CpfDeTeste(95_000 + int.Parse(sufixo));

        var texto = new StringBuilder("nome;cpf;data de nascimento\n");
        texto.Append($"Rollback {sufixo}-0;{cpf};15/03/1990\n");

        for (var i = 1; i < 4; i++)
        {
            var outro = BancoPostgresFixture.CpfDeTeste(96_000 + (int.Parse(sufixo) * 10) + i);
            texto.Append($"Rollback {sufixo}-{i};{outro};15/03/1990\n");
        }

        texto.Append($"Rollback {sufixo}-repetido;{cpf};15/03/1990\n");

        using var resposta = await admin.PostAsync(Confirmar, Arquivo(texto.ToString()));
        resposta.EnsureSuccessStatusCode();

        var confirmacao = (await resposta.Content.ReadFromJsonAsync<ConfirmacaoItem>())!;

        Assert.Equal("Recusada", confirmacao.Status);

        using var escopo = _fabrica.Services.CreateScope();

        // NENHUM funcionario criado - nem os quatro que estavam certos. Tudo ou
        // nada: importar parcialmente deixaria o cadastro num estado que
        // ninguem pediu.
        Assert.Empty(await Contexto(escopo).Funcionarios.IgnoreQueryFilters()
            .Where(f => f.Nome.StartsWith($"Rollback {sufixo}"))
            .ToListAsync());
    }

    /// <summary>
    /// O rollback DE VERDADE: duas confirmacoes do mesmo arquivo ao mesmo tempo.
    ///
    /// As duas validam antes de qualquer uma gravar, entao nenhuma ve o CPF da
    /// outra e as duas se julgam importaveis. So uma consegue inserir; a outra
    /// esbarra no indice unico **com a transacao ja aberta e o trabalho pela
    /// metade** - que e o unico caminho que exerce o `catch (DbUpdateException)`
    /// e o `RollbackAsync`.
    ///
    /// A afirmacao nao depende de qual das duas venceu: o invariante e que
    /// existam DOIS funcionarios, e nao quatro nem tres.
    /// </summary>
    [Fact]
    public async Task DuasConfirmacoesSIMULTANEAS_NaoDeixamEstadoPelaMetade()
    {
        var admin = await AdminAsync();
        var sufixo = Sufixo();
        var conteudo = CsvValido(sufixo);

        var primeira = admin.PostAsync(Confirmar, Arquivo(conteudo));
        var segunda = admin.PostAsync(Confirmar, Arquivo(conteudo));

        var respostas = await Task.WhenAll(primeira, segunda);

        foreach (var r in respostas)
        {
            // Nenhuma das duas pode dar 500: o conflito e previsto, e a
            // resposta e 200 (recusada na validacao) ou 409 (recusada pelo
            // banco). As duas dizem a mesma coisa - nada foi gravado nesta.
            Assert.True(
                r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
                $"Status inesperado: {r.StatusCode}");

            r.Dispose();
        }

        using var escopo = _fabrica.Services.CreateScope();

        // DOIS, e nao quatro (duplicata) nem tres (importacao pela metade).
        Assert.Equal(2, await Contexto(escopo).Funcionarios.IgnoreQueryFilters()
            .CountAsync(f => f.Nome.StartsWith($"Importado {sufixo}")));
    }

    // ---------------------------------------------------------------- limites

    [Fact]
    public async Task ArquivoAcimaDoTETO_ERecusadoNaROTA()
    {
        var admin = await AdminAsync();

        var enorme = new StringBuilder("nome;cpf;data de nascimento\n");
        enorme.Append('x', 6 * 1024 * 1024);

        using var r = await admin.PostAsync(Confirmar, Arquivo(enorme.ToString()));

        // Os limites da etapa 1 continuam valendo depois do HTTP entrar.
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("maior que o limite", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ArquivoQueNaoECsv_ERecusado()
    {
        var admin = await AdminAsync();

        using var r = await admin.PostAsync(Preview, Arquivo(CsvValido(Sufixo()), "virus.exe"));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task SemArquivo_ERecusadoCom400()
    {
        var admin = await AdminAsync();

        using var vazio = new MultipartFormDataContent
        {
            { new StringContent("nada"), "outro-campo" },
        };

        using var r = await admin.PostAsync(Preview, vazio);

        // 400, e nao 500: arquivo ausente e erro do cliente.
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task CabecalhoSemAsColunasObrigatorias_ERecusado()
    {
        var admin = await AdminAsync();

        using var resposta = await admin.PostAsync(
            Preview, Arquivo("apelido;telefone\nAna;9999\n"));

        resposta.EnsureSuccessStatusCode();
        var previa = (await resposta.Content.ReadFromJsonAsync<PreviewItem>())!;

        Assert.False(previa.Importavel);
        Assert.Contains(previa.ErrosDoArquivo, e => e.Contains("Faltam colunas obrigatorias"));
    }

    // ------------------------------------------------------------- isolamento

    [Fact]
    public async Task ImportacaoDaVizinha_DEVOLVE404()
    {
        var admin = await AdminAsync();

        using var resposta = await admin.PostAsync(Confirmar, Arquivo(CsvValido(Sufixo())));
        var confirmacao = (await resposta.Content.ReadFromJsonAsync<ConfirmacaoItem>())!;

        var vizinha = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminC);

        using var r = await vizinha.GetAsync($"/api/importacoes/{confirmacao.IdImportacao}");

        // 404, e nao 403: um 403 confirmaria que a importacao existe.
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task OFuncionarioImportadoNAOAparecePARAAVizinha()
    {
        var admin = await AdminAsync();
        var sufixo = Sufixo();

        using var resposta = await admin.PostAsync(Confirmar, Arquivo(CsvValido(sufixo, 1)));
        resposta.EnsureSuccessStatusCode();

        var vizinha = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminC);

        using var r = await vizinha.GetAsync($"/api/funcionarios?nome=Importado {sufixo}");
        r.EnsureSuccessStatusCode();

        Assert.DoesNotContain($"Importado {sufixo}", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ApagarAImportacaoComCadastro_ERECUSADOPeloBANCO()
    {
        var admin = await AdminAsync();

        using var resposta = await admin.PostAsync(Confirmar, Arquivo(CsvValido(Sufixo(), 1)));
        var confirmacao = (await resposta.Content.ReadFromJsonAsync<ConfirmacaoItem>())!;

        using var escopo = _fabrica.Services.CreateScope();
        var db = Contexto(escopo);

        // RESTRICT provado contra PostgreSQL real: apagar a importacao que
        // produziu cadastro e recusado pelo banco. E isso que "rastreabilidade
        // da origem" significa - a origem nao some enquanto o dado existir.
        var erro = await Assert.ThrowsAnyAsync<Exception>(() =>
            db.Database.ExecuteSqlRawAsync(
                "delete from importacoes where id = {0}", confirmacao.IdImportacao));

        Assert.Contains("id_linha_importacao", erro.ToString());
    }
}
