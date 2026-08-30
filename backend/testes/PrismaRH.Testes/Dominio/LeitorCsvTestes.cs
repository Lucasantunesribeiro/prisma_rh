using System.Text;
using PrismaRH.Dominio.Importacao;

namespace PrismaRH.Testes.Dominio;

/// <summary>
/// A leitura de CSV, escrita a mao (Fase 5, etapa 1).
///
/// Metade destes testes e de FORMA (RFC 4180) e metade e de ATAQUE. Arquivo de
/// usuario e a entrada menos confiavel que existe, e o Security Gate da Fase 5
/// exige limite de tamanho, limite de quantidade e recusa de malformado.
/// </summary>
public class LeitorCsvTestes
{
    // ------------------------------------------------------------- forma

    [Fact]
    public void LeCabecalhoELinhas()
    {
        var r = LeitorCsv.Ler("nome;cpf\nAna;111\nBruno;222");

        Assert.True(r.Valido);
        Assert.Equal(["nome", "cpf"], r.Cabecalho);
        Assert.Equal(2, r.Linhas.Count);
        Assert.Equal(["Ana", "111"], r.Linhas[0].Campos);
    }

    [Fact]
    public void ANumeracaoDaLinhaEADoARQUIVO_ContandoOCabecalho()
    {
        var r = LeitorCsv.Ler("nome\nAna\nBruno");

        // Quem abre a planilha para corrigir procura pelo numero que o editor
        // mostra na lateral. "Registro 1" obrigaria a fazer a conta de cabeca.
        Assert.Equal(2, r.Linhas[0].Numero);
        Assert.Equal(3, r.Linhas[1].Numero);
    }

    [Fact]
    public void CampoEntreAspas_PodeConterDelimitador()
    {
        var r = LeitorCsv.Ler("nome;obs\n\"Ana\";\"mora na rua A; numero 3\"");

        Assert.True(r.Valido);
        Assert.Equal("mora na rua A; numero 3", r.Linhas[0].Campos[1]);
    }

    [Fact]
    public void CampoEntreAspas_PodeConterQuebraDeLinha()
    {
        var r = LeitorCsv.Ler("nome;obs\nAna;\"linha 1\nlinha 2\"");

        Assert.True(r.Valido);
        Assert.Single(r.Linhas);
        Assert.Equal("linha 1\nlinha 2", r.Linhas[0].Campos[1]);
    }

    [Fact]
    public void AspasDuplicadas_ViramUmaAspaLiteral()
    {
        // RFC 4180: dentro de aspas, "" e uma aspa.
        var r = LeitorCsv.Ler("nome\n\"Ana \"\"Aninha\"\" Silva\"");

        Assert.True(r.Valido);
        Assert.Equal("Ana \"Aninha\" Silva", r.Linhas[0].Campos[0]);
    }

    [Fact]
    public void CRLF_ContaComoUMAQuebra()
    {
        // Sem isto, todo arquivo salvo no Windows viria com uma linha vazia
        // entre cada duas linhas de dados.
        var r = LeitorCsv.Ler("nome\r\nAna\r\nBruno\r\n");

        Assert.True(r.Valido);
        Assert.Equal(2, r.Linhas.Count);
    }

    [Fact]
    public void LinhaEmBrancoNoFim_EIgnorada()
    {
        // Quase todo editor deixa uma. Recusar seria recusar o arquivo que o
        // Excel acabou de salvar.
        var r = LeitorCsv.Ler("nome\nAna\n\n");

        Assert.True(r.Valido);
        Assert.Single(r.Linhas);
    }

    [Fact]
    public void UltimaLinhaSemQuebraNoFim_EntraAssimMesmo()
    {
        var r = LeitorCsv.Ler("nome\nAna");

        Assert.True(r.Valido);
        Assert.Single(r.Linhas);
    }

    [Fact]
    public void DelimitadorPadrao_EPontoEVirgula()
    {
        // O Excel em pt-BR usa virgula como separador DECIMAL e exporta CSV com
        // ponto e virgula. Virgula como padrao quebraria o caminho mais comum.
        Assert.Equal(';', LeitorCsv.DelimitadorPadrao);

        var r = LeitorCsv.Ler("a,b\n1,2", delimitador: ',');
        Assert.True(r.Valido);
        Assert.Equal(["a", "b"], r.Cabecalho);
    }

    // -------------------------------------------------------- codificacao

    [Fact]
    public void UTF8ComBOM_NaoDeixaOBOMNoPrimeiroCampo()
    {
        var bytes = new List<byte>(Encoding.UTF8.GetPreamble());
        bytes.AddRange(Encoding.UTF8.GetBytes("nome\nJosé"));

        using var fluxo = new MemoryStream(bytes.ToArray());
        var r = LeitorCsv.Ler(fluxo);

        // Sem tratar o BOM, o cabecalho viraria "﻿nome" e NENHUMA busca
        // por coluna acharia "nome" - o arquivo pareceria nao ter cabecalho.
        Assert.Equal("nome", r.Cabecalho[0]);
        Assert.Equal("José", r.Linhas[0].Campos[0]);
    }

    [Fact]
    public void ArquivoLatin1_NaoCorrompeOAcento()
    {
        // O caso real: Excel brasileiro salvando CSV sem BOM. Decodificar como
        // UTF-8 tolerante colocaria "Jos?" no banco SEM ERRO NENHUM.
        using var fluxo = new MemoryStream(Encoding.Latin1.GetBytes("nome\nJosé"));

        var r = LeitorCsv.Ler(fluxo);

        Assert.True(r.Valido);
        Assert.Equal("José", r.Linhas[0].Campos[0]);
    }

    [Fact]
    public void ArquivoUTF8SemBOM_ContinuaSendoLidoComoUTF8()
    {
        using var fluxo = new MemoryStream(
            new UTF8Encoding(false).GetBytes("nome\nConceição"));

        var r = LeitorCsv.Ler(fluxo);

        Assert.Equal("Conceição", r.Linhas[0].Campos[0]);
    }

    // ------------------------------------------------------------ recusas

    [Fact]
    public void ArquivoVazio_ERecusado()
    {
        using var fluxo = new MemoryStream([]);

        var r = LeitorCsv.Ler(fluxo);

        Assert.False(r.Valido);
        Assert.Contains(r.Erros, e => e.Mensagem.Contains("vazio"));
    }

    [Fact]
    public void SoCabecalho_ERecusadoComMensagemPROPRIA()
    {
        // "0 registros importados com sucesso" seria pior que dizer o que houve.
        var r = LeitorCsv.Ler("nome;cpf\n");

        Assert.False(r.Valido);
        Assert.Contains(r.Erros, e => e.Mensagem.Contains("nenhuma linha de dados"));
    }

    [Fact]
    public void ColunaDuplicadaNoCabecalho_ERecusada()
    {
        // Duas colunas com o mesmo nome tornariam a busca ambigua, e o valor
        // usado dependeria da ordem - defeito que so aparece em producao.
        var r = LeitorCsv.Ler("cpf;nome;CPF\n1;Ana;2");

        Assert.False(r.Valido);
        Assert.Contains(r.Erros, e => e.Mensagem.Contains("mais de uma vez"));
        Assert.Empty(r.Linhas);
    }

    [Fact]
    public void ColunaSemNome_ERecusada()
    {
        var r = LeitorCsv.Ler("nome;;cpf\nAna;x;1");

        Assert.False(r.Valido);
        Assert.Contains(r.Erros, e => e.Mensagem.Contains("sem nome"));
    }

    [Fact]
    public void LinhaComMenosCampos_ERelatadaSemDerrubarAsOutras()
    {
        var r = LeitorCsv.Ler("nome;cpf\nAna;1\nBruno\nCarla;3");

        // O ROADMAP pede relatorio LINHA A LINHA: abortar no primeiro erro
        // faria a pessoa corrigir e reenviar uma vez por problema.
        Assert.False(r.Valido);
        Assert.Single(r.Erros);
        Assert.Equal(3, r.Erros[0].Linha);
        Assert.Equal(2, r.Linhas.Count);
    }

    [Fact]
    public void AspasAbertasENaoFechadas_SaoRecusadas()
    {
        var r = LeitorCsv.Ler("nome\n\"Ana");

        Assert.False(r.Valido);
        Assert.Contains(r.Erros, e => e.Mensagem.Contains("Aspas abertas"));
    }

    // ------------------------------------------------------------- limites

    [Fact]
    public void ArquivoMaiorQueOTeto_ERecusadoSEMSerLidoInteiro()
    {
        var limites = LimitesImportacao.Padrao with { TamanhoMaximoBytes = 100 };

        using var fluxo = new MemoryStream(Encoding.UTF8.GetBytes(new string('a', 5_000)));

        var r = LeitorCsv.Ler(fluxo, limites);

        Assert.False(r.Valido);
        Assert.Contains(r.Erros, e => e.Mensagem.Contains("maior que o limite"));
        Assert.Empty(r.Linhas);
    }

    [Fact]
    public void MaisRegistrosQueOTeto_ParaNoTeto()
    {
        var limites = LimitesImportacao.Padrao with { MaximoRegistros = 3 };

        var texto = new StringBuilder("nome\n");

        for (var i = 0; i < 50; i++)
        {
            texto.Append("Pessoa ").Append(i).Append('\n');
        }

        var r = LeitorCsv.Ler(texto.ToString(), limites);

        Assert.False(r.Valido);
        Assert.Equal(3, r.Linhas.Count);
        Assert.Contains(r.Erros, e => e.Mensagem.Contains("mais de 3 registros"));
    }

    [Fact]
    public void MaisColunasQueOTeto_ERecusadoNoCabecalho()
    {
        // Protege contra a linha unica com milhoes de delimitadores - o jeito
        // mais barato de fazer um parser alocar memoria sem arquivo grande.
        var limites = LimitesImportacao.Padrao with { MaximoColunas = 5 };

        var r = LeitorCsv.Ler(string.Join(';', Enumerable.Range(1, 40).Select(i => $"c{i}")), limites);

        Assert.False(r.Valido);
        Assert.Contains(r.Erros, e => e.Mensagem.Contains("mais de 5 colunas"));
    }

    [Fact]
    public void CampoAcimaDoTeto_ETruncadoDeFormaVISIVEL()
    {
        var limites = LimitesImportacao.Padrao with { TamanhoMaximoCampo = 10 };

        var r = LeitorCsv.Ler("nome\n" + new string('x', 500), limites);

        // Truncar em silencio gravaria meio nome como se fosse o nome inteiro.
        // A marca deixa a validacao de dominio recusar em seguida.
        Assert.EndsWith("[TRUNCADO]", r.Linhas[0].Campos[0]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void LimiteZeroOuNegativo_ERecusado(int valor)
    {
        // Teto zero nao e "sem limite": e configuracao errada, e aceita-la
        // devolveria a aplicacao ao estado sem protecao alguma.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LimitesImportacao.Padrao with { TamanhoMaximoBytes = valor });
    }

    [Fact]
    public void DelimitadorQueQuebraOFormato_ERecusado()
    {
        Assert.Throws<ArgumentException>(() => LeitorCsv.Ler("a\nb", delimitador: '"'));
        Assert.Throws<ArgumentException>(() => LeitorCsv.Ler("a\nb", delimitador: '\n'));
    }

    // -------------------------------------------------- busca por coluna

    [Fact]
    public void BuscaDeColuna_IgnoraMaiusculaAcentoEEspaco()
    {
        var r = LeitorCsv.Ler(" Nome ;CPF;Data de Admissão\nAna;1;2020-01-01");

        // Quem monta a planilha escreve "CPF", "Cpf" ou " cpf ". Recusar por
        // isso seria rigor sem proposito - o CONTEUDO segue validado com rigor.
        Assert.Equal(0, r.Coluna("nome"));
        Assert.Equal(1, r.Coluna("cpf"));
        Assert.Equal(2, r.Coluna("data de admissao"));
        Assert.Null(r.Coluna("salario"));
    }

    // ------------------------------------------------------ nao avalia nada

    [Fact]
    public void FormulaNoArquivo_EncerraComoTEXTO()
    {
        var r = LeitorCsv.Ler("nome\n=cmd|'/c calc'!A1");

        // O leitor nao e uma planilha: ele nao avalia, nao executa, nao chama
        // nada. O risco de formula existe na ESCRITA, e e la que ele e tratado.
        Assert.True(r.Valido);
        Assert.Equal("=cmd|'/c calc'!A1", r.Linhas[0].Campos[0]);
    }
}
