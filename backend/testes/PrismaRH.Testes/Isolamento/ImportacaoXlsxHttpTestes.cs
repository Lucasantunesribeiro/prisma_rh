using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrismaRH.Infraestrutura.Persistencia;
using PrismaRH.Testes.Planilhas;

namespace PrismaRH.Testes.Isolamento;

/// <summary>
/// XLSX e mapeamento de colunas pelas rotas HTTP (Fase 5, etapa 4), contra
/// PostgreSQL real.
///
/// A afirmacao central: **XLSX entra no mesmo pipeline do CSV.** O formato
/// escolhe o leitor, e nada mais - validacao, duplicata, transacao, isolamento
/// e rastreabilidade sao literalmente o mesmo codigo. Estes testes existem para
/// provar isso ponta a ponta, e nao para reprovar a leitura, que os testes de
/// <see cref="Planilhas.LeitorXlsxTestes"/> ja cobrem.
///
/// Organizacao E, como a suite de CSV, com faixa de CPF propria.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class ImportacaoXlsxHttpTestes(BancoPostgresFixture banco) : IDisposable
{
    private readonly FabricaApiIsolada _fabrica = new(banco.StringConexao);

    public void Dispose() => _fabrica.Dispose();

    private const string Modelo = "/api/importacoes/funcionarios/modelo";
    private const string Preview = "/api/importacoes/funcionarios/preview";
    private const string Confirmar = "/api/importacoes/funcionarios/confirmar";

    private sealed record LinhaItem(
        int Linha, string? Nome, string? Cpf, DateOnly? DataNascimento, List<string> Erros);

    private sealed record MapeamentoItem(string Nome, string Cpf, string DataNascimento);

    private sealed record PreviewItem(
        string NomeArquivo, string Formato, long TamanhoBytes, string HashSha256,
        List<string> Colunas, MapeamentoItem Mapeamento,
        int Total, int Validas, int ComErro, bool Importavel,
        List<string> ErrosDoArquivo, List<LinhaItem> Linhas);

    private sealed record ConfirmacaoItem(
        Guid IdImportacao, string Status, string Formato, string HashSha256,
        int Total, int Validas, int ComErro, int FuncionariosCriados,
        List<string> ErrosDoArquivo, List<LinhaItem> Linhas);

    private static int _sufixo;

    /// <summary>
    /// Faixa de CPF propria desta suite.
    ///
    /// A de CSV usa 90.000 para cima; esta usa 70.000. As duas rodam na mesma
    /// organizacao, e CPF repetido entre elas viraria falha intermitente que
    /// nao tem nada a ver com o que se quer provar.
    /// </summary>
    private static int Semente() => 70_000 + (Interlocked.Increment(ref _sufixo) * 10);

    private Task<HttpClient> AdminAsync() =>
        _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminE);

    private PrismaRhDbContext Contexto(IServiceScope escopo) =>
        escopo.ServiceProvider.GetRequiredService<PrismaRhDbContext>();

    /// <summary>Multipart com bytes crus, como o navegador faria.</summary>
    private static MultipartFormDataContent Envio(
        byte[] bytes,
        string nome,
        string tipo,
        (string Campo, string Valor)[]? extras = null)
    {
        var parte = new ByteArrayContent(bytes);
        parte.Headers.ContentType = new MediaTypeHeaderValue(tipo);

        var conteudo = new MultipartFormDataContent { { parte, "arquivo", nome } };

        foreach (var (campo, valor) in extras ?? [])
        {
            conteudo.Add(new StringContent(valor), campo);
        }

        return conteudo;
    }

    private static MultipartFormDataContent Xlsx(
        byte[] bytes, string nome = "funcionarios.xlsx", (string, string)[]? extras = null) =>
        Envio(
            bytes, nome,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", extras);

    /// <summary>Uma planilha valida, com as colunas padrao.</summary>
    private static byte[] PlanilhaValida(int semente, int quantidade = 2, string[]? cabecalho = null)
    {
        var colunas = cabecalho ?? ["nome", "cpf", "data de nascimento"];

        return FabricaXlsx.Planilha(planilha =>
        {
            for (var c = 0; c < colunas.Length; c++)
            {
                planilha.Cell(1, c + 1).SetValue(colunas[c]);
            }

            for (var i = 0; i < quantidade; i++)
            {
                planilha.Cell(i + 2, 1).SetValue($"Planilha {semente}-{i}");
                planilha.Cell(i + 2, 2).SetValue(BancoPostgresFixture.CpfDeTeste(semente + i));

                // Data COMO DATA, e nao como texto: e o caso real, e e o que
                // exercita a conversao para ISO.
                planilha.Cell(i + 2, 3).Value =
                    new DateTime(1990, 3, 15, 0, 0, 0, DateTimeKind.Unspecified);
            }
        });
    }

    // ------------------------------------------------------------------ modelo

    [Fact]
    public async Task ModeloCsv_EhBaixavelEPassaNaPropriaValidacao()
    {
        var admin = await AdminAsync();

        using var r = await admin.GetAsync($"{Modelo}?formato=csv");
        r.EnsureSuccessStatusCode();

        var bytes = await r.Content.ReadAsByteArrayAsync();

        // O modelo que o sistema entrega precisa passar no importador do
        // sistema. Se nao passar, ele nao e modelo: e a primeira frustracao de
        // quem tentou seguir a instrucao.
        using var previa = await admin.PostAsync(Preview, Envio(bytes, "modelo.csv", "text/csv"));
        var resultado = (await previa.Content.ReadFromJsonAsync<PreviewItem>())!;

        Assert.True(resultado.Importavel);
        Assert.Equal(2, resultado.Validas);
    }

    [Fact]
    public async Task ModeloXlsx_EhBaixavelEPassaNaPropriaValidacao()
    {
        var admin = await AdminAsync();

        using var r = await admin.GetAsync($"{Modelo}?formato=xlsx");
        r.EnsureSuccessStatusCode();

        var bytes = await r.Content.ReadAsByteArrayAsync();

        using var previa = await admin.PostAsync(Preview, Xlsx(bytes, "modelo.xlsx"));
        var resultado = (await previa.Content.ReadFromJsonAsync<PreviewItem>())!;

        Assert.Equal("Xlsx", resultado.Formato);
        Assert.True(resultado.Importavel);
    }

    [Fact]
    public async Task Modelo_EhBLOQUEADOParaAuditor()
    {
        var auditor = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAuditorA);

        using var r = await auditor.GetAsync(Modelo);

        // Auditor confere folha; nao importa cadastro. O modelo so serve para
        // importar (`CLAUDE.md secao 24.4`).
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    // ------------------------------------------------------- o caminho feliz

    [Fact]
    public async Task PlanilhaValida_ImportaEGravaOFormatoXlsx()
    {
        var admin = await AdminAsync();
        var semente = Semente();

        using var previa = await admin.PostAsync(Preview, Xlsx(PlanilhaValida(semente)));
        var resultado = (await previa.Content.ReadFromJsonAsync<PreviewItem>())!;

        Assert.True(resultado.Importavel);
        Assert.Equal(2, resultado.Total);

        // A data que era DateTime na planilha atravessou a mesma validacao da
        // data digitada no CSV.
        Assert.Equal(new DateOnly(1990, 3, 15), resultado.Linhas[0].DataNascimento);

        // O CPF sai mascarado da fronteira HTTP, no XLSX como no CSV.
        Assert.Contains('*', resultado.Linhas[0].Cpf!);

        using var confirmacao = await admin.PostAsync(Confirmar, Xlsx(PlanilhaValida(semente)));
        var gravada = (await confirmacao.Content.ReadFromJsonAsync<ConfirmacaoItem>())!;

        Assert.Equal("Aplicada", gravada.Status);
        Assert.Equal("Xlsx", gravada.Formato);
        Assert.Equal(2, gravada.FuncionariosCriados);

        using var escopo = _fabrica.Services.CreateScope();

        var importacao = await Contexto(escopo).Importacoes
            .IgnoreQueryFilters()
            .SingleAsync(i => i.Id == gravada.IdImportacao);

        Assert.Equal(PrismaRH.Dominio.Importacao.FormatoImportacao.Xlsx, importacao.Formato);
    }

    /// <summary>
    /// A origem fica rastreavel do funcionario ate a linha da planilha.
    ///
    /// E a mesma FK do CSV: o pipeline nao tem um caminho diferente para XLSX.
    /// </summary>
    [Fact]
    public async Task FuncionarioImportadoDeXlsx_ApontaParaALinhaDeOrigem()
    {
        var admin = await AdminAsync();

        using var r = await admin.PostAsync(Confirmar, Xlsx(PlanilhaValida(Semente(), 1)));
        var gravada = (await r.Content.ReadFromJsonAsync<ConfirmacaoItem>())!;

        using var escopo = _fabrica.Services.CreateScope();
        var db = Contexto(escopo);

        var linha = await db.LinhasImportacao
            .IgnoreQueryFilters()
            .SingleAsync(l => l.IdImportacao == gravada.IdImportacao && l.NumeroNoArquivo == 2);

        var funcionario = await db.Funcionarios
            .IgnoreQueryFilters()
            .SingleAsync(f => f.IdLinhaImportacao == linha.Id);

        Assert.Equal(gravada.IdImportacao, linha.IdImportacao);
        Assert.NotEqual(Guid.Empty, funcionario.Id);
    }

    // ------------------------------------------- conteudo contra a extensao

    [Fact]
    public async Task CsvRenomeadoParaXlsx_ERECUSADO()
    {
        var admin = await AdminAsync();

        var bytes = new UTF8Encoding(false).GetBytes("nome;cpf;data de nascimento\n");

        using var r = await admin.PostAsync(Preview, Xlsx(bytes, "mentira.xlsx"));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains(
            "conteudo nao e uma planilha", await r.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task XlsxRenomeadoParaCsv_ERECUSADO()
    {
        var admin = await AdminAsync();

        using var r = await admin.PostAsync(
            Preview, Envio(PlanilhaValida(Semente()), "mentira.csv", "text/csv"));

        // Nao adivinhar e a decisao: adivinhar erraria justamente no caso
        // interessante, que e o de alguem tentando fazer um arquivo passar por
        // outro.
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains(
            "extensao .csv", await r.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtensaoDesconhecida_ERECUSADA()
    {
        var admin = await AdminAsync();

        using var r = await admin.PostAsync(
            Preview, Envio([1, 2, 3], "virus.exe", "application/octet-stream"));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    // ---------------------------------------------- protecoes do formato XLSX

    /// <summary>
    /// A bomba de descompressao chega pela rota e e recusada **sem 500**.
    ///
    /// O arquivo cabe folgado no teto de 5 MB do upload. O que nao cabe e o que
    /// ele vira descompactado - e e por isso que o teto de bytes sozinho nao
    /// protegeria.
    /// </summary>
    [Fact]
    public async Task BombaDeDescompressao_ERECUSADASemDerrubarNada()
    {
        var admin = await AdminAsync();

        var bomba = FabricaXlsx.Bomba(
            PrismaRH.Infraestrutura.Planilhas.GuardaXlsx.TamanhoMaximoDescomprimido
            + (4 * 1024 * 1024));

        using var r = await admin.PostAsync(Preview, Xlsx(bomba, "bomba.xlsx"));

        // 200 com relatorio de erro, e nao 500: arquivo de usuario nunca vira
        // erro do servidor.
        r.EnsureSuccessStatusCode();

        var resultado = (await r.Content.ReadFromJsonAsync<PreviewItem>())!;

        Assert.False(resultado.Importavel);
        Assert.Contains(resultado.ErrosDoArquivo, e => e.Contains("descompactada", StringComparison.Ordinal));

        // E o servidor continua de pe.
        using var depois = await admin.PostAsync(Preview, Xlsx(PlanilhaValida(Semente())));
        depois.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task PlanilhaComMacro_ERECUSADA()
    {
        var admin = await AdminAsync();

        var comMacro = FabricaXlsx.Pacote([
            .. FabricaXlsx.PartesObrigatorias(),
            ("xl/vbaProject.bin", [1, 2, 3, 4]),
        ]);

        using var r = await admin.PostAsync(Preview, Xlsx(comMacro, "macro.xlsx"));
        r.EnsureSuccessStatusCode();

        var resultado = (await r.Content.ReadFromJsonAsync<PreviewItem>())!;

        Assert.False(resultado.Importavel);
        Assert.Contains(resultado.ErrosDoArquivo, e => e.Contains("macro", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PlanilhaCorrompida_ViraRelatorioENaoQuinhentos()
    {
        var admin = await AdminAsync();

        var inteira = PlanilhaValida(Semente());

        using var r = await admin.PostAsync(Preview, Xlsx(inteira[..(inteira.Length / 2)]));
        r.EnsureSuccessStatusCode();

        var resultado = (await r.Content.ReadFromJsonAsync<PreviewItem>())!;

        Assert.False(resultado.Importavel);
        Assert.NotEmpty(resultado.ErrosDoArquivo);
    }

    [Fact]
    public async Task PlanilhaComFormula_NaoGravaNinguem()
    {
        var admin = await AdminAsync();
        var semente = Semente();

        var comFormula = FabricaXlsx.Planilha(planilha =>
        {
            planilha.Cell(1, 1).SetValue("nome");
            planilha.Cell(1, 2).SetValue("cpf");
            planilha.Cell(1, 3).SetValue("data de nascimento");

            planilha.Cell(2, 1).FormulaA1 = "CONCAT(\"Fan\",\"tasma\")";
            planilha.Cell(2, 2).SetValue(BancoPostgresFixture.CpfDeTeste(semente));
            planilha.Cell(2, 3).SetValue("15/03/1990");
        });

        using var r = await admin.PostAsync(Confirmar, Xlsx(comFormula));
        r.EnsureSuccessStatusCode();

        var gravada = (await r.Content.ReadFromJsonAsync<ConfirmacaoItem>())!;

        Assert.Equal("Recusada", gravada.Status);
        Assert.Equal(0, gravada.FuncionariosCriados);

        using var escopo = _fabrica.Services.CreateScope();

        // O valor calculado da formula nao existe em lugar nenhum do banco.
        Assert.False(await Contexto(escopo).Funcionarios
            .IgnoreQueryFilters()
            .AnyAsync(f => f.Nome == "Fantasma"));
    }

    // ------------------------------------------------------------ mapeamento

    [Fact]
    public async Task MapeamentoEscolhido_LeAsColunasDoArquivoDaEmpresa()
    {
        var admin = await AdminAsync();
        var semente = Semente();

        // A planilha que a empresa ja tem, com os nomes dela.
        var bytes = PlanilhaValida(semente, 2, ["Nome Completo", "Documento", "Nascimento"]);

        using var previa = await admin.PostAsync(Preview, Xlsx(bytes, "rh.xlsx", [
            ("colunaNome", "Nome Completo"),
            ("colunaCpf", "Documento"),
            ("colunaDataNascimento", "Nascimento"),
        ]));

        var resultado = (await previa.Content.ReadFromJsonAsync<PreviewItem>())!;

        Assert.True(resultado.Importavel);
        Assert.Equal(["Nome Completo", "Documento", "Nascimento"], resultado.Colunas);
        Assert.Equal("Documento", resultado.Mapeamento.Cpf);

        using var confirmacao = await admin.PostAsync(Confirmar, Xlsx(bytes, "rh.xlsx", [
            ("colunaNome", "Nome Completo"),
            ("colunaCpf", "Documento"),
            ("colunaDataNascimento", "Nascimento"),
        ]));

        var gravada = (await confirmacao.Content.ReadFromJsonAsync<ConfirmacaoItem>())!;

        Assert.Equal("Aplicada", gravada.Status);
        Assert.Equal(2, gravada.FuncionariosCriados);
    }

    [Fact]
    public async Task MapeamentoParaColunaInexistente_ERECUSADO()
    {
        var admin = await AdminAsync();

        using var r = await admin.PostAsync(Preview, Xlsx(PlanilhaValida(Semente()), "f.xlsx", [
            ("colunaCpf", "coluna que nao existe"),
        ]));

        r.EnsureSuccessStatusCode();
        var resultado = (await r.Content.ReadFromJsonAsync<PreviewItem>())!;

        // Vocabulario fechado: o cliente escolhe DENTRO do que o servidor
        // acabou de ler do arquivo. O que estiver fora nao vira indice de
        // coluna - vira recusa.
        Assert.False(resultado.Importavel);
        Assert.Contains(resultado.ErrosDoArquivo, e => e.Contains("nao existe no arquivo", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MapeamentoComDuasColunasIguais_ERECUSADO()
    {
        var admin = await AdminAsync();

        using var r = await admin.PostAsync(Preview, Xlsx(PlanilhaValida(Semente()), "f.xlsx", [
            ("colunaNome", "cpf"),
        ]));

        r.EnsureSuccessStatusCode();
        var resultado = (await r.Content.ReadFromJsonAsync<PreviewItem>())!;

        // Sem esta conferencia, o CPF entraria como nome - e o arquivo
        // pareceria valido, porque cada campo isolado esta preenchido.
        Assert.False(resultado.Importavel);
        Assert.Contains(resultado.ErrosDoArquivo, e => e.Contains("mesma coluna", StringComparison.Ordinal));
    }

    /// <summary>
    /// Nome de coluna gigante e cortado na entrada, e nao ecoado inteiro.
    ///
    /// A primeira versao deste teste reprovou por um motivo que valeu a pena: a
    /// mensagem de erro nao ecoava o texto gigante, mas o campo `mapeamento` da
    /// resposta devolvia ele inteiro. Validar num lugar e esquecer do outro e
    /// exatamente o que cortar na entrada evita.
    /// </summary>
    [Fact]
    public async Task MapeamentoGiganteEhCORTADOEnaoEcoadoInteiro()
    {
        var admin = await AdminAsync();
        var gigante = new string('x', 5_000);

        using var r = await admin.PostAsync(Preview, Xlsx(PlanilhaValida(Semente()), "f.xlsx", [
            ("colunaNome", gigante),
        ]));

        r.EnsureSuccessStatusCode();

        var corpo = await r.Content.ReadAsStringAsync();
        var resultado = (await r.Content.ReadFromJsonAsync<PreviewItem>())!;

        Assert.DoesNotContain(gigante, corpo, StringComparison.Ordinal);
        Assert.Equal(
            PrismaRH.Aplicacao.Importacao.MapeamentoFuncionarios.TamanhoMaximoNome,
            resultado.Mapeamento.Nome.Length);

        // Cortado, ele deixa de casar com qualquer coluna - que e a recusa
        // certa, e a mensagem cabe na tela.
        Assert.False(resultado.Importavel);
        Assert.Contains(
            resultado.ErrosDoArquivo, e => e.Contains("nao existe no arquivo", StringComparison.Ordinal));
    }

    /// <summary>
    /// Dois problemas de CABECALHO nao viram 409.
    ///
    /// ⚠️ Este teste nasceu de um defeito real da etapa 3, encontrado na
    /// etapa 4: `Importacao.Registrar` criava uma LinhaImportacao nova a cada
    /// chamada, e dois erros da MESMA linha do arquivo - dois nomes de coluna
    /// errados, ambos da linha 1 - violavam o indice unico. A pessoa recebia
    /// "alguem importou o mesmo arquivo ao mesmo tempo" quando o problema era a
    /// planilha dela.
    /// </summary>
    [Fact]
    public async Task DoisErrosNaMESMALinha_ViramUmRelatorioENaoUmConflito()
    {
        var admin = await AdminAsync();

        using var r = await admin.PostAsync(Confirmar, Xlsx(PlanilhaValida(Semente()), "f.xlsx", [
            ("colunaNome", "nao existe 1"),
            ("colunaCpf", "nao existe 2"),
        ]));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var gravada = (await r.Content.ReadFromJsonAsync<ConfirmacaoItem>())!;

        Assert.Equal("Recusada", gravada.Status);
        Assert.Equal(2, gravada.ErrosDoArquivo.Count);

        // Uma linha so no relatorio, com os dois erros dentro.
        using var escopo = _fabrica.Services.CreateScope();

        var linha = await Contexto(escopo).LinhasImportacao
            .IgnoreQueryFilters()
            .SingleAsync(l => l.IdImportacao == gravada.IdImportacao);

        Assert.Equal(2, linha.Erros.Count);
    }

    /// <summary>
    /// O mapeamento e reconferido na confirmacao, contra o arquivo RELIDO.
    ///
    /// Aqui o preview passa com um arquivo, e a confirmacao chega com outro que
    /// nao tem aquelas colunas. O mapeamento que funcionava vira recusa -
    /// porque o que vale e o arquivo reenviado, e nunca o preview anterior.
    /// </summary>
    [Fact]
    public async Task MapeamentoValidoNoPreview_ERECONFERIDONaConfirmacao()
    {
        var admin = await AdminAsync();
        var semente = Semente();

        (string, string)[] mapeamento =
        [
            ("colunaNome", "Nome Completo"),
            ("colunaCpf", "Documento"),
            ("colunaDataNascimento", "Nascimento"),
        ];

        var comOsNomesDaEmpresa = PlanilhaValida(
            semente, 1, ["Nome Completo", "Documento", "Nascimento"]);

        using var previa = await admin.PostAsync(
            Preview, Xlsx(comOsNomesDaEmpresa, "rh.xlsx", mapeamento));

        Assert.True((await previa.Content.ReadFromJsonAsync<PreviewItem>())!.Importavel);

        // Mesmo mapeamento, outro arquivo - com as colunas padrao.
        using var confirmacao = await admin.PostAsync(
            Confirmar, Xlsx(PlanilhaValida(semente, 1), "rh.xlsx", mapeamento));

        var gravada = (await confirmacao.Content.ReadFromJsonAsync<ConfirmacaoItem>())!;

        Assert.Equal("Recusada", gravada.Status);
        Assert.Equal(0, gravada.FuncionariosCriados);
        Assert.Contains(gravada.ErrosDoArquivo, e => e.Contains("nao existe no arquivo", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------- isolamento

    [Fact]
    public async Task ImportacaoXlsxDaVizinha_DEVOLVE404()
    {
        var admin = await AdminAsync();

        using var r = await admin.PostAsync(Confirmar, Xlsx(PlanilhaValida(Semente(), 1)));
        var gravada = (await r.Content.ReadFromJsonAsync<ConfirmacaoItem>())!;

        var vizinha = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminC);

        using var consulta = await vizinha.GetAsync($"/api/importacoes/{gravada.IdImportacao}");

        Assert.Equal(HttpStatusCode.NotFound, consulta.StatusCode);
    }

    [Fact]
    public async Task IdOrganizacaoNoCorpo_NaoTemEfeitoNoXlsxTambem()
    {
        var admin = await AdminAsync();
        var semente = Semente();

        using var r = await admin.PostAsync(Confirmar, Xlsx(PlanilhaValida(semente, 1), "f.xlsx", [
            ("idOrganizacao", Guid.NewGuid().ToString()),
            ("IdOrganizacao", Guid.NewGuid().ToString()),
        ]));

        var gravada = (await r.Content.ReadFromJsonAsync<ConfirmacaoItem>())!;

        Assert.Equal("Aplicada", gravada.Status);

        using var escopo = _fabrica.Services.CreateScope();

        var importacao = await Contexto(escopo).Importacoes
            .IgnoreQueryFilters()
            .SingleAsync(i => i.Id == gravada.IdImportacao);

        // O campo enviado nem e lido: a organizacao vem do usuario autenticado.
        var organizacaoDoAdmin = await Contexto(escopo).Usuarios
            .IgnoreQueryFilters()
            .Where(u => u.Email == BancoPostgresFixture.EmailAdminE)
            .Select(u => u.IdOrganizacao)
            .SingleAsync();

        Assert.Equal(organizacaoDoAdmin, importacao.IdOrganizacao);
    }
}
