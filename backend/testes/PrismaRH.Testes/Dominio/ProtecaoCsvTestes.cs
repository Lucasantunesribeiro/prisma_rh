using System.Text;
using PrismaRH.Dominio.Importacao;

namespace PrismaRH.Testes.Dominio;

/// <summary>
/// CSV injection na EXPORTACAO (Fase 5, etapa 1).
///
/// O Prisma RH nunca avalia formula. Mas o Excel de quem abre o arquivo
/// exportado avalia - e e por isso que a defesa fica aqui, e nao na leitura.
///
/// FONTE: `CLAUDE.md secao 24.12` e o Security Gate da Fase 5, que mandam
/// prefixar celula comecando com `=`, `+`, `-` ou `@`.
/// </summary>
public class ProtecaoCsvTestes
{
    [Theory]
    [InlineData("=1+1")]
    [InlineData("=cmd|'/c calc'!A1")]
    [InlineData("@SUM(A1:A9)")]
    [InlineData("+1+1")]
    [InlineData("-1+1")]
    [InlineData("\tqualquer")]
    [InlineData("\rqualquer")]
    public void ValorQueVIRARIAFormula_RecebePrefixo(string perigoso)
    {
        Assert.True(ProtecaoCsv.EhFormula(perigoso));

        // A afirmacao e sobre a CELULA, e nao sobre a string bruta: um valor
        // que tambem precise de aspas sai como "'=1+1", entao o apostrofo nao
        // e o primeiro caractere do texto escapado - mas E o primeiro da
        // celula, que e o que o Excel le. Reler prova isso sem depender da
        // ordem interna do escape.
        var bytes = ProtecaoCsv.Arquivo(["v"], [[perigoso]]);

        using var fluxo = new MemoryStream(bytes);
        var lido = LeitorCsv.Ler(fluxo);

        Assert.True(lido.Valido);
        Assert.StartsWith("'", lido.Linhas[0].Campos[0]);
    }

    [Theory]
    [InlineData("Ana Silva")]
    [InlineData("111.222.333-44")]
    [InlineData("")]
    public void ValorComum_NaoERemexido(string inofensivo)
    {
        Assert.False(ProtecaoCsv.EhFormula(inofensivo));
        Assert.Equal(inofensivo, ProtecaoCsv.Escapar(inofensivo));
    }

    [Theory]
    [InlineData("-1234,56")]
    [InlineData("-0,01")]
    [InlineData("+1500.00")]
    public void NumeroNegativoDEVERDADE_NaoRecebePrefixo(string numero)
    {
        // Se todo "-" virasse formula, TODA coluna de desconto do sistema
        // sairia com apostrofo - e o valor deixaria de ser numero na planilha
        // de quem abre, que e justamente o que se quer preservar.
        Assert.False(ProtecaoCsv.EhFormula(numero));
        Assert.Equal(numero, ProtecaoCsv.Escapar(numero));
    }

    [Fact]
    public void ValorComDelimitador_SaiEntreAspas()
    {
        // Sem as aspas, o proprio arquivo sairia malformado: o valor viraria
        // duas colunas.
        Assert.Equal("\"rua A; numero 3\"", ProtecaoCsv.Escapar("rua A; numero 3"));
    }

    [Fact]
    public void ValorComAspas_TemAsAspasDUPLICADAS()
    {
        Assert.Equal("\"Ana \"\"Aninha\"\"\"", ProtecaoCsv.Escapar("Ana \"Aninha\""));
    }

    [Fact]
    public void ValorComQuebraDeLinha_SaiEntreAspas()
    {
        Assert.Equal("\"linha 1\nlinha 2\"", ProtecaoCsv.Escapar("linha 1\nlinha 2"));
    }

    [Fact]
    public void FormulaCOMDelimitador_RecebeOsDoisTratamentos()
    {
        var saida = ProtecaoCsv.Escapar("=A1;B2");

        // Prefixo contra a formula E aspas contra o delimitador. Fazer so um
        // dos dois deixaria o arquivo quebrado ou o Excel executando.
        Assert.Equal("\"'=A1;B2\"", saida);
    }

    [Fact]
    public void ArquivoExportado_TemBOMParaOExcelAcertarOAcento()
    {
        var bytes = ProtecaoCsv.Arquivo(["nome"], [["José"]]);

        // Sem BOM, o Excel no Windows abre como Latin-1 e o acento quebra.
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        Assert.Contains("José", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void OQueEXPORTAMOS_VoltaIGUALQuandoLIDODeNovo()
    {
        // O teste de ida e volta: se o escape estivesse errado, o proprio
        // sistema nao conseguiria reler o que acabou de escrever.
        string[] valores = ["Ana \"A\" Silva", "rua A; n 3", "=1+1", "linha\nquebrada", "-99,90"];

        var bytes = ProtecaoCsv.Arquivo(["a", "b", "c", "d", "e"], [valores]);

        using var fluxo = new MemoryStream(bytes);
        var lido = LeitorCsv.Ler(fluxo);

        Assert.True(lido.Valido);

        // A formula volta com o apostrofo de propósito: ele faz parte do dado
        // gravado no arquivo, e e ele que neutraliza o Excel.
        Assert.Equal("Ana \"A\" Silva", lido.Linhas[0].Campos[0]);
        Assert.Equal("rua A; n 3", lido.Linhas[0].Campos[1]);
        Assert.Equal("'=1+1", lido.Linhas[0].Campos[2]);
        Assert.Equal("linha\nquebrada", lido.Linhas[0].Campos[3]);
        Assert.Equal("-99,90", lido.Linhas[0].Campos[4]);
    }
}
