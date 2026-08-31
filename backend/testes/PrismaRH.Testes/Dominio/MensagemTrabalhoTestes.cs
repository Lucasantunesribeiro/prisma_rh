using PrismaRH.Dominio.Assincrono;
using PrismaRH.Dominio.Importacao;

namespace PrismaRH.Testes.Dominio;

/// <summary>
/// A mensagem da fila e **dado nao confiavel** — item 2 do Security Gate da
/// Fase 9.
///
/// Quase todo teste aqui descreve uma mensagem torta, e o que se exige e sempre
/// o mesmo: **recusa antes de tocar em qualquer dado**.
/// </summary>
public sealed class MensagemTrabalhoTestes
{
    private static readonly Guid Org = Guid.CreateVersion7();
    private static readonly Guid Vizinha = Guid.CreateVersion7();
    private static readonly Guid Usuario = Guid.CreateVersion7();
    private static readonly DateTimeOffset Agora = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    private const int Teto = OrcamentoSemCusto.TamanhoMaximoMensagemBytes;

    private static TrabalhoAssincrono Trabalho(Guid? org = null) => new(
        org ?? Org, Usuario, TipoTrabalho.ImportacaoFuncionarios, "chave", Agora);

    private static RecusaMensagem Ler(string corpo, out MensagemTrabalho? m) =>
        MensagemTrabalho.Ler(corpo, Teto, out m);

    // ------------------------------------------------------------ ida e volta

    [Fact]
    public void OQueSaiVoltaIgual()
    {
        var t = Trabalho();

        var recusa = Ler(MensagemTrabalho.De(t).Serializar(), out var lida);

        Assert.Equal(RecusaMensagem.Aceita, recusa);
        Assert.Equal(t.Id, lida!.IdTrabalho);
        Assert.Equal(t.IdOrganizacao, lida.IdOrganizacao);
        Assert.Equal(TipoTrabalho.ImportacaoFuncionarios, lida.Tipo);
        Assert.Equal(MensagemTrabalho.VersaoAtual, lida.Versao);
    }

    /// <summary>
    /// A mensagem carrega **identificadores**, e nada mais.
    ///
    /// Uma fila tem retencao propria e uma DLQ onde a mensagem fica quatorze
    /// dias. Dado pessoal ali seria uma segunda copia do que ha de mais
    /// sensivel no produto, num lugar com regras diferentes das do banco.
    /// </summary>
    [Fact]
    public void AMensagemNaoCarregaDadoPessoalNemDeLonge()
    {
        var corpo = MensagemTrabalho.De(Trabalho()).Serializar();

        Assert.True(
            System.Text.Encoding.UTF8.GetByteCount(corpo) < 300,
            $"mensagem com {corpo.Length} caracteres - grande demais para so conter ids");

        foreach (var proibido in new[] { "cpf", "salario", "nome", "arquivo", "conteudo", "senha" })
        {
            Assert.DoesNotContain(proibido, corpo, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ------------------------------------------------------- recusa por esquema

    [Theory]
    [InlineData("", RecusaMensagem.NaoEJson)]
    [InlineData("   ", RecusaMensagem.NaoEJson)]
    [InlineData("nao e json", RecusaMensagem.NaoEJson)]
    [InlineData("{", RecusaMensagem.NaoEJson)]
    [InlineData("null", RecusaMensagem.NaoEJson)]
    public void CorpoQueNaoEJsonERecusado(string corpo, RecusaMensagem esperado)
    {
        Assert.Equal(esperado, Ler(corpo, out var m));
        Assert.Null(m);
    }

    /// <summary>
    /// Fila tem mensagem em voo. No dia em que o formato mudar, havera mensagem
    /// antiga na fila e worker novo lendo — e sem o campo de versao o worker
    /// novo interpretaria o formato velho **em silencio**.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(99)]
    public void VersaoQueOWorkerNaoConheceERecusada(int versao)
    {
        var corpo = $$"""
            {"versao":{{versao}},"idTrabalho":"{{Guid.CreateVersion7()}}",
             "idOrganizacao":"{{Org}}","tipo":"ImportacaoFuncionarios"}
            """;

        Assert.Equal(RecusaMensagem.VersaoDesconhecida, Ler(corpo, out _));
    }

    /// <summary>
    /// ⚠️ Mensagem sem tenant nao e "mensagem do sistema": e mensagem invalida.
    /// Aceita-la faria o worker rodar sem dono — o cenario exato que o Security
    /// Gate manda impedir.
    /// </summary>
    [Fact]
    public void MensagemSemOrganizacaoERecusada()
    {
        var corpo = $$"""
            {"versao":1,"idTrabalho":"{{Guid.CreateVersion7()}}",
             "idOrganizacao":"{{Guid.Empty}}","tipo":"ImportacaoFuncionarios"}
            """;

        Assert.Equal(RecusaMensagem.SemOrganizacao, Ler(corpo, out _));
    }

    [Fact]
    public void MensagemSemTrabalhoERecusada()
    {
        var corpo = $$"""
            {"versao":1,"idTrabalho":"{{Guid.Empty}}",
             "idOrganizacao":"{{Org}}","tipo":"ImportacaoFuncionarios"}
            """;

        Assert.Equal(RecusaMensagem.SemTrabalho, Ler(corpo, out _));
    }

    /// <summary>
    /// `(TipoTrabalho)999` desserializa sem erro e so quebraria la na frente,
    /// dentro do `switch`. Vocabulario fechado conferido na entrada.
    /// </summary>
    [Fact]
    public void TipoForaDoVocabularioERecusado()
    {
        var corpo = $$"""
            {"versao":1,"idTrabalho":"{{Guid.CreateVersion7()}}",
             "idOrganizacao":"{{Org}}","tipo":999}
            """;

        Assert.Equal(RecusaMensagem.TipoDesconhecido, Ler(corpo, out _));
    }

    [Fact]
    public void CorpoAcimaDoTetoERecusadoSemSerLido()
    {
        var corpo = $$"""
            {"versao":1,"idTrabalho":"{{Guid.CreateVersion7()}}","idOrganizacao":"{{Org}}",
             "tipo":"ImportacaoFuncionarios","lixo":"{{new string('x', Teto)}}"}
            """;

        Assert.Equal(RecusaMensagem.GrandeDemais, Ler(corpo, out _));
    }

    /// <summary>
    /// O teto e conferido em **bytes**, e nao em caracteres: um corpo cheio de
    /// acentos ocupa mais que o `Length` sugere, e o limite da fila e em bytes.
    /// </summary>
    [Fact]
    public void OTetoEContadoEmBytesENaoEmCaracteres()
    {
        var acentuado = new string('ç', Teto - 100); // 2 bytes cada em UTF-8

        Assert.True(acentuado.Length < Teto);
        Assert.Equal(RecusaMensagem.GrandeDemais, Ler($"{{\"x\":\"{acentuado}\"}}", out _));
    }

    // ------------------------------------------------- recusa contra o objeto

    [Fact]
    public void MensagemQueCombinaComOTrabalhoEAceita()
    {
        var t = Trabalho();

        Assert.Equal(RecusaMensagem.Aceita, MensagemTrabalho.De(t).Conferir(t));
    }

    /// <summary>
    /// ⚠️ **O teste que impede o vazamento entre organizacoes.**
    ///
    /// A mensagem passou no esquema — o JSON e perfeitamente valido. Mas o
    /// tenant dela nao bate com o do trabalho gravado. Ou houve adulteracao, ou
    /// um defeito montou a mensagem errada; nos dois casos, processar
    /// significaria rodar a planilha de uma empresa dentro da organizacao de
    /// outra.
    ///
    /// Por isso o worker **confere** contra o objeto, em vez de aceitar o
    /// tenant da mensagem de bom grado.
    /// </summary>
    [Fact]
    public void MensagemComTenantDeOutraOrganizacaoERecusada()
    {
        var t = Trabalho(Org);

        var adulterada = MensagemTrabalho.De(t) with { IdOrganizacao = Vizinha };

        // Ela e um JSON valido e passa no esquema...
        Assert.Equal(RecusaMensagem.Aceita, Ler(adulterada.Serializar(), out var lida));

        // ...e para aqui, na conferencia contra o trabalho de verdade.
        Assert.Equal(RecusaMensagem.TenantDivergente, lida!.Conferir(t));
    }

    [Fact]
    public void MensagemApontandoParaOutroTrabalhoERecusada()
    {
        var t = Trabalho();
        var outra = MensagemTrabalho.De(t) with { IdTrabalho = Guid.CreateVersion7() };

        Assert.Equal(RecusaMensagem.SemTrabalho, outra.Conferir(t));
    }

    [Fact]
    public void MensagemComTipoDiferenteDoTrabalhoERecusada()
    {
        var t = Trabalho();
        var outra = MensagemTrabalho.De(t) with { Tipo = (TipoTrabalho)7 };

        Assert.Equal(RecusaMensagem.TipoDesconhecido, outra.Conferir(t));
    }
}
