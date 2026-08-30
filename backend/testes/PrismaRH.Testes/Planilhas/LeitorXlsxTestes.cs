using ClosedXML.Excel;
using PrismaRH.Dominio.Importacao;
using PrismaRH.Infraestrutura.Planilhas;

namespace PrismaRH.Testes.Planilhas;

/// <summary>
/// Leitura de XLSX.
///
/// A afirmacao que atravessa este arquivo: o XLSX produz o **mesmo**
/// <see cref="ResultadoLeitura"/> que o CSV. E por isso que mapeamento,
/// validacao, duplicata e transacao nao precisaram de uma linha de codigo nova
/// para o segundo formato.
/// </summary>
public class LeitorXlsxTestes
{
    private static readonly string[] Cabecalho = ["nome", "cpf", "data de nascimento"];

    [Fact]
    public void LeCabecalhoELinhas()
    {
        var bytes = FabricaXlsx.Simples(
            Cabecalho,
            ["Ana Paula", "111.444.777-35", "14/03/1991"],
            ["Bruno Lima", "529.982.247-25", "02/11/1985"]);

        var resultado = LeitorXlsx.Ler(bytes);

        Assert.True(resultado.Valido);
        Assert.Equal(Cabecalho, resultado.Cabecalho);
        Assert.Equal(2, resultado.Linhas.Count);
        Assert.Equal("Ana Paula", resultado.Linhas[0].Campos[0]);

        // O numero e o da LINHA NO ARQUIVO, e nao o do registro: e o que o
        // Excel mostra na lateral para quem vai corrigir.
        Assert.Equal(2, resultado.Linhas[0].Numero);
        Assert.Equal(3, resultado.Linhas[1].Numero);
    }

    /// <summary>
    /// Data de verdade do Excel vira ISO - que e uma das duas formas que o
    /// importador ja aceitava no CSV.
    ///
    /// Converter aqui faz a data da planilha atravessar a MESMA validacao da
    /// data digitada, e nao uma segunda parecida.
    /// </summary>
    [Fact]
    public void DataDoExcel_ViraIsoENaoSerial()
    {
        var bytes = FabricaXlsx.Planilha(planilha =>
        {
            planilha.Cell(1, 1).SetValue("data de nascimento");
            planilha.Cell(2, 1).Value = new DateTime(1991, 3, 14, 0, 0, 0, DateTimeKind.Unspecified);
        });

        var resultado = LeitorXlsx.Ler(bytes);

        Assert.Equal("1991-03-14", resultado.Linhas[0].Campos[0]);
    }

    /// <summary>
    /// CPF digitado como numero nao pode virar notacao cientifica.
    ///
    /// Sem o formato invariante, `11144477735` sairia como
    /// "1.1144477735E+10" - e o erro que a pessoa veria ("CPF invalido") nao
    /// teria nada a ver com a causa.
    /// </summary>
    [Fact]
    public void NumeroGrande_NaoViraNotacaoCientifica()
    {
        var bytes = FabricaXlsx.Planilha(planilha =>
        {
            planilha.Cell(1, 1).SetValue("cpf");
            planilha.Cell(2, 1).Value = 11144477735d;
        });

        var resultado = LeitorXlsx.Ler(bytes);

        Assert.Equal("11144477735", resultado.Linhas[0].Campos[0]);
    }

    /// <summary>
    /// Formula nao e avaliada **nem aproveitada do cache**.
    ///
    /// O requisito era nao avaliar. A recusa vai alem por uma razao de
    /// correcao: o valor em cache pode estar velho, e importar numero velho sem
    /// que ninguem perceba e pior que recusar o arquivo.
    /// </summary>
    [Fact]
    public void CelulaComFormula_ViraErroDeLinha()
    {
        var bytes = FabricaXlsx.Planilha(planilha =>
        {
            planilha.Cell(1, 1).SetValue("nome");
            planilha.Cell(2, 1).FormulaA1 = "CONCAT(\"An\",\"a\")";
        });

        var resultado = LeitorXlsx.Ler(bytes);

        Assert.Empty(resultado.Linhas);
        var erro = Assert.Single(resultado.Erros);
        Assert.Equal(2, erro.Linha);
        Assert.Contains("formula", erro.Mensagem, StringComparison.OrdinalIgnoreCase);

        // E a prova de que nada foi calculado: "Ana" nao aparece em lugar
        // nenhum do resultado.
        Assert.DoesNotContain("Ana", erro.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public void CelulaComFormulaPerigosa_TambemNaoEhExecutada()
    {
        // O classico da injecao de planilha. Aqui ele nao vira nem dado nem
        // comando: vira erro de linha.
        var bytes = FabricaXlsx.Planilha(planilha =>
        {
            planilha.Cell(1, 1).SetValue("nome");
            planilha.Cell(2, 1).FormulaA1 = "HYPERLINK(\"http://exemplo.invalido\",\"clique\")";
        });

        var resultado = LeitorXlsx.Ler(bytes);

        Assert.Empty(resultado.Linhas);
        Assert.Contains("formula", resultado.Erros[0].Mensagem, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Texto que PARECE formula continua sendo texto.
    ///
    /// A defesa contra *CSV injection* e de escrita, e vive na
    /// <see cref="ProtecaoCsv"/>. Na leitura, `=SOMA(A1:A9)` digitado como
    /// texto e so uma string estranha.
    /// </summary>
    [Fact]
    public void TextoQueParaceFormula_ContinuaTexto()
    {
        var bytes = FabricaXlsx.Simples(["nome"], ["=cmd|'/c calc'!A1"]);

        var resultado = LeitorXlsx.Ler(bytes);

        Assert.True(resultado.Valido);
        Assert.Equal("=cmd|'/c calc'!A1", resultado.Linhas[0].Campos[0]);
    }

    [Fact]
    public void LinhaTotalmenteVazia_EhIgnorada()
    {
        // Basta alguem ter pintado a borda ate a linha 50 para o arquivo ter
        // linhas "usadas" e vazias. Recusa-las seria recusar o arquivo que o
        // Excel acabou de salvar.
        var bytes = FabricaXlsx.Planilha(planilha =>
        {
            planilha.Cell(1, 1).SetValue("nome");
            planilha.Cell(2, 1).SetValue("Ana");
            planilha.Cell(5, 1).Style.Fill.BackgroundColor = XLColor.Yellow;
            planilha.Cell(6, 1).SetValue("Bruno");
        });

        var resultado = LeitorXlsx.Ler(bytes);

        Assert.True(resultado.Valido);
        Assert.Equal(2, resultado.Linhas.Count);
        Assert.Equal("Bruno", resultado.Linhas[1].Campos[0]);
    }

    /// <summary>
    /// So a primeira aba visivel.
    ///
    /// Ler todas juntaria dados de abas diferentes sem ninguem pedir; ler a aba
    /// oculta importaria o que a pessoa escondeu de proposito - o rascunho, a
    /// copia velha, a lista de quem sai no mes que vem.
    /// </summary>
    [Fact]
    public void AbaOculta_NaoEhLida()
    {
        using var pasta = new XLWorkbook();

        var oculta = pasta.AddWorksheet("Rascunho");
        oculta.Cell(1, 1).SetValue("nome");
        oculta.Cell(2, 1).SetValue("Nao deveria aparecer");
        oculta.Visibility = XLWorksheetVisibility.Hidden;

        var visivel = pasta.AddWorksheet("Funcionarios");
        visivel.Cell(1, 1).SetValue("nome");
        visivel.Cell(2, 1).SetValue("Ana");

        using var memoria = new MemoryStream();
        pasta.SaveAs(memoria);

        var resultado = LeitorXlsx.Ler(memoria.ToArray());

        var linha = Assert.Single(resultado.Linhas);
        Assert.Equal("Ana", linha.Campos[0]);
    }

    [Fact]
    public void CabecalhoDuplicado_EhRecusado()
    {
        // Duas colunas com o mesmo nome tornariam a busca ambigua, e o valor
        // usado dependeria da ordem - defeito que so aparece em producao.
        var bytes = FabricaXlsx.Simples(["nome", "Nome"], ["Ana", "Bruno"]);

        var resultado = LeitorXlsx.Ler(bytes);

        Assert.False(resultado.Valido);
        Assert.Contains(
            resultado.Erros, e => e.Mensagem.Contains("mais de uma vez", StringComparison.Ordinal));
    }

    [Fact]
    public void ColunaDeCabecalhoSemNome_EhRecusada()
    {
        var bytes = FabricaXlsx.Planilha(planilha =>
        {
            planilha.Cell(1, 1).SetValue("nome");
            planilha.Cell(1, 3).SetValue("cpf");
            planilha.Cell(2, 1).SetValue("Ana");
        });

        var resultado = LeitorXlsx.Ler(bytes);

        Assert.False(resultado.Valido);
        Assert.Contains(
            resultado.Erros, e => e.Mensagem.Contains("sem nome", StringComparison.Ordinal));
    }

    [Fact]
    public void ColunasDemais_SaoRecusadas()
    {
        var limites = LimitesImportacao.Padrao with { MaximoColunas = 3 };

        var bytes = FabricaXlsx.Planilha(planilha =>
        {
            for (var c = 1; c <= 4; c++)
            {
                planilha.Cell(1, c).SetValue($"coluna{c}");
            }
        });

        var resultado = LeitorXlsx.Ler(bytes, limites);

        Assert.False(resultado.Valido);
        Assert.Contains("mais de 3 colunas", resultado.Erros[0].Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistrosDemais_ParamAContagem()
    {
        var limites = LimitesImportacao.Padrao with { MaximoRegistros = 2 };

        var bytes = FabricaXlsx.Simples(["nome"], ["A"], ["B"], ["C"], ["D"]);

        var resultado = LeitorXlsx.Ler(bytes, limites);

        Assert.False(resultado.Valido);
        Assert.Equal(2, resultado.Linhas.Count);
        Assert.Contains("mais de 2 registros", resultado.Erros[0].Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public void CampoLongoDemais_EhMarcadoComoTruncado()
    {
        var limites = LimitesImportacao.Padrao with { TamanhoMaximoCampo = 10 };

        var bytes = FabricaXlsx.Simples(["nome"], [new string('a', 40)]);

        var resultado = LeitorXlsx.Ler(bytes, limites);

        // Truncar em silencio gravaria meio nome como se fosse o nome inteiro.
        Assert.EndsWith("[TRUNCADO]", resultado.Linhas[0].Campos[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ArquivoMaiorQueOTeto_EhRecusadoDuranteALeitura()
    {
        var limites = LimitesImportacao.Padrao with { TamanhoMaximoBytes = 512 };

        using var fluxo = new MemoryStream(FabricaXlsx.Simples(["nome"], ["Ana"]));

        var resultado = LeitorXlsx.Ler(fluxo, limites);

        Assert.False(resultado.Valido);
        Assert.Contains("maior que o limite", resultado.Erros[0].Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public void ArquivoVazio_ViraRelatorio()
    {
        var resultado = LeitorXlsx.Ler(Array.Empty<byte>());

        Assert.False(resultado.Valido);
        Assert.Contains("vazio", resultado.Erros[0].Mensagem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SoCabecalho_NaoEhImportacaoNenhuma()
    {
        var bytes = FabricaXlsx.Simples(Cabecalho);

        var resultado = LeitorXlsx.Ler(bytes);

        Assert.False(resultado.Valido);
        Assert.Contains(
            "nenhuma linha de dados", resultado.Erros[0].Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public void ArquivoQueNaoEhPlanilha_ViraRelatorioENaoExcecao()
    {
        var resultado = LeitorXlsx.Ler(System.Text.Encoding.UTF8.GetBytes("nome;cpf\nAna;123\n"));

        Assert.False(resultado.Valido);
        Assert.Contains(
            "nao e uma planilha XLSX", resultado.Erros[0].Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public void PlanilhaCorrompida_ViraRelatorioENaoExcecao()
    {
        var inteiro = FabricaXlsx.Simples(["nome"], ["Ana"]);

        var resultado = LeitorXlsx.Ler(inteiro[..(inteiro.Length / 2)]);

        Assert.False(resultado.Valido);
        Assert.NotEmpty(resultado.Erros);
    }
}
