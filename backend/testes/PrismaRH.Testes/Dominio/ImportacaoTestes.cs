using System.Text;
using PrismaRH.Dominio.Importacao;

namespace PrismaRH.Testes.Dominio;

/// <summary>
/// A entidade da importacao (Fase 5, etapa 2).
///
/// O que ela guarda importa menos que o que ela **recusa guardar**: nem o
/// binario do arquivo, nem nome, CPF ou salario de ninguem. O que substitui o
/// arquivo e o SHA-256.
/// </summary>
public class ImportacaoTestes
{
    private static readonly Guid Org = Guid.CreateVersion7();
    private static readonly Guid Usuario = Guid.CreateVersion7();
    private static readonly DateTimeOffset Agora = new(2026, 8, 30, 9, 0, 0, TimeSpan.Zero);

    private static Importacao Nova(long tamanho = 1024) => new(
        Org, Usuario, "funcionarios.csv", FormatoImportacao.Csv,
        tamanho, Importacao.CalcularHash("conteudo"u8), Agora);

    // ---------------------------------------------------------------- hash

    [Fact]
    public void OHashIdentificaOArquivo_SemGuardarOConteudo()
    {
        var a = Importacao.CalcularHash(Encoding.UTF8.GetBytes("nome;cpf\nAna;1"));
        var b = Importacao.CalcularHash(Encoding.UTF8.GetBytes("nome;cpf\nAna;1"));
        var c = Importacao.CalcularHash(Encoding.UTF8.GetBytes("nome;cpf\nAna;2"));

        // Mesmo arquivo, mesmo hash: e assim que se responde "a importacao 42
        // veio deste arquivo aqui?" sem guardar uma linha do conteudo.
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal(Importacao.TamanhoHash, a.Length);
    }

    [Fact]
    public void OHashSaiEmHexadecimalMINUSCULO()
    {
        // Duas formas de escrever o mesmo hash fariam a comparacao falhar sem
        // nada parecer errado. O formato e parte da invariante.
        var hash = Importacao.CalcularHash("x"u8);

        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("ZZZZ4c5e3f9a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f6071829304")]
    public void HashMalFormado_ERecusado(string hash)
    {
        Assert.Throws<ArgumentException>(() => new Importacao(
            Org, Usuario, "a.csv", FormatoImportacao.Csv, 10, hash, Agora));
    }

    [Fact]
    public void HashEmMAIUSCULA_ENormalizado()
    {
        var maiusculo = Importacao.CalcularHash("x"u8).ToUpperInvariant();

        var importacao = new Importacao(
            Org, Usuario, "a.csv", FormatoImportacao.Csv, 10, maiusculo, Agora);

        Assert.Equal(maiusculo.ToLowerInvariant(), importacao.HashSha256);
    }

    // ------------------------------------------------------- obrigatorios

    [Fact]
    public void SemOrganizacao_ERecusada()
    {
        Assert.Throws<ArgumentException>(() => new Importacao(
            Guid.Empty, Usuario, "a.csv", FormatoImportacao.Csv, 10,
            Importacao.CalcularHash("x"u8), Agora));
    }

    [Fact]
    public void SemUsuario_ERecusada()
    {
        // Sem autor nao ha rastreabilidade, e rastreabilidade e a unica razao
        // desta entidade existir.
        Assert.Throws<ArgumentException>(() => new Importacao(
            Org, Guid.Empty, "a.csv", FormatoImportacao.Csv, 10,
            Importacao.CalcularHash("x"u8), Agora));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TamanhoZeroOuNegativo_ERecusado(long tamanho)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Nova(tamanho));
    }

    [Fact]
    public void FormatoDesconhecido_ERecusado()
    {
        Assert.Throws<ArgumentException>(() => new Importacao(
            Org, Usuario, "a.csv", (FormatoImportacao)99, 10,
            Importacao.CalcularHash("x"u8), Agora));
    }

    // ---------------------------------------------------------- contadores

    [Fact]
    public void OsContadoresAcompanhamAsLinhas()
    {
        var i = Nova();

        i.Registrar(2, []);
        i.Registrar(3, ["CPF invalido"]);
        i.Registrar(4, []);

        Assert.Equal(3, i.TotalLinhas);
        Assert.Equal(2, i.LinhasValidas);
        Assert.Equal(1, i.LinhasComErro);
    }

    [Fact]
    public void ASituacaoDaLinhaEDERIVADADosErros()
    {
        var i = Nova();

        // Nao ha parametro "situacao". Um chamador que pudesse dizer "valida"
        // com erros na lista criaria uma linha que se contradiz - e ela
        // passaria pela invariante de Aplicar.
        Assert.Equal(SituacaoLinha.Valida, i.Registrar(2, []).Situacao);
        Assert.Equal(SituacaoLinha.ComErro, i.Registrar(3, ["x"]).Situacao);
    }

    [Fact]
    public void ErrosDeMaisNumaLinha_SaoLimitados()
    {
        var i = Nova();

        var muitos = Enumerable.Range(1, 50).Select(n => $"erro {n}").ToArray();

        var linha = i.Registrar(2, muitos);

        // Trinta mensagens nao ajudam quem le a entender que a linha esta ruim,
        // e um arquivo desenhado para isso encheria a tabela.
        Assert.Equal(LinhaImportacao.MaximoErrosPorLinha, linha.Erros.Count);
    }

    [Fact]
    public void ErroLonguissimo_ETruncado()
    {
        var i = Nova();

        var linha = i.Registrar(2, [new string('x', 5_000)]);

        Assert.Equal(LinhaImportacao.TamanhoMaximoErro, linha.Erros[0].Length);
    }

    [Fact]
    public void ErroVazio_NaoContaComoErro()
    {
        var i = Nova();

        var linha = i.Registrar(2, ["", "   "]);

        // Uma string em branco nao e um problema relatado: se contasse, a linha
        // ficaria "com erro" sem que houvesse erro nenhum a mostrar.
        Assert.Empty(linha.Erros);
        Assert.Equal(SituacaoLinha.Valida, linha.Situacao);
    }

    [Fact]
    public void NumeroDeLinhaZeroOuNegativo_ERecusado()
    {
        var i = Nova();

        Assert.Throws<ArgumentOutOfRangeException>(() => i.Registrar(0, []));
    }

    // ------------------------------------------------------- estado

    [Fact]
    public void NasceAnalisada()
    {
        Assert.Equal(StatusImportacao.Analisada, Nova().Status);
    }

    [Fact]
    public void ComLinhaComErro_NAOPodeSerAplicada()
    {
        var i = Nova();

        i.Registrar(2, []);
        i.Registrar(3, ["CPF invalido"]);

        // A invariante que sustenta "importacao invalida nao deixa dados
        // parcialmente gravados" no lugar onde ela nao pode ser esquecida. A
        // transacao do banco e a SEGUNDA camada; esta e a primeira.
        var erro = Assert.Throws<InvalidOperationException>(i.Aplicar);

        Assert.Contains("1 linha(s) com erro", erro.Message);
        Assert.Equal(StatusImportacao.Analisada, i.Status);
    }

    [Fact]
    public void SemLinhaAlguma_NAOPodeSerAplicada()
    {
        Assert.Throws<InvalidOperationException>(Nova().Aplicar);
    }

    [Fact]
    public void SoComLinhasValidas_EAplicada()
    {
        var i = Nova();

        i.Registrar(2, []);
        i.Registrar(3, []);
        i.Aplicar();

        Assert.Equal(StatusImportacao.Aplicada, i.Status);
    }

    [Fact]
    public void DepoisDeAplicada_NadaMaisMuda()
    {
        var i = Nova();
        i.Registrar(2, []);
        i.Aplicar();

        // Uma importacao aplicada e fato historico. O CLAUDE.md secao 4.3
        // proibe reescrever o passado em silencio.
        Assert.Throws<InvalidOperationException>(() => i.Registrar(3, []));
        Assert.Throws<InvalidOperationException>(i.Aplicar);
        Assert.Throws<InvalidOperationException>(i.Recusar);
    }

    [Fact]
    public void RecusadaTAMBEMFicaRegistrada()
    {
        var i = Nova();
        i.Registrar(2, ["CPF invalido"]);
        i.Recusar();

        // Uma tentativa que falhou tambem e rastreabilidade: apagar o vestigio
        // deixaria "por que o cadastro nao mudou?" sem resposta.
        Assert.Equal(StatusImportacao.Recusada, i.Status);
        Assert.Equal(1, i.LinhasComErro);
    }

    // -------------------------------------------- o que NAO e guardado

    [Fact]
    public void ALinhaNaoTemCampoParaValorAlgum()
    {
        var i = Nova();
        var linha = i.Registrar(7, ["CPF invalido"]);

        // Este teste e sobre a FORMA da entidade, e trava a decisao de
        // minimizacao: se alguem acrescentar "Nome" ou "Cpf" aqui por
        // conveniencia de relatorio, ele quebra.
        var propriedades = typeof(LinhaImportacao)
            .GetProperties()
            .Select(p => p.Name)
            .ToArray();

        Assert.Equal(
            ["Id", "IdOrganizacao", "IdImportacao", "NumeroNoArquivo", "Situacao", "Erros"],
            propriedades);

        // O que liga o relatorio ao arquivo e o NUMERO DA LINHA, e mais nada.
        Assert.Equal(7, linha.NumeroNoArquivo);
    }

    [Fact]
    public void AImportacaoNaoTemCampoParaOConteudoDoArquivo()
    {
        var propriedades = typeof(Importacao)
            .GetProperties()
            .Select(p => p.Name)
            .ToArray();

        // O binario NAO e guardado - decisao aprovada em 29/08/2026. Guardar
        // exigiria armazenamento isolado, retencao e download autorizado, que
        // e Fase 9. O hash faz o papel de identificar sem reter.
        Assert.DoesNotContain("Conteudo", propriedades);
        Assert.DoesNotContain("Bytes", propriedades);
        Assert.Contains("HashSha256", propriedades);
    }
}
