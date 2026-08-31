using PrismaRH.Dominio.Assincrono;
using PrismaRH.Dominio.Importacao;

namespace PrismaRH.Testes.Dominio;

/// <summary>
/// O nucleo do processamento assincrono (Fase 9).
///
/// O Security Gate desta fase chama o job que perde o tenant de **"o vazamento
/// mais provavel do produto inteiro"**, e a razao e concreta: o filtro global
/// do EF le a organizacao do usuario autenticado, e um worker nao tem usuario.
///
/// Metade destes testes existe so para provar que o tenant sobrevive a viagem.
/// </summary>
public sealed class TrabalhoAssincronoTestes
{
    private static readonly Guid Org = Guid.CreateVersion7();
    private static readonly Guid Vizinha = Guid.CreateVersion7();
    private static readonly Guid Usuario = Guid.CreateVersion7();
    private static readonly DateTimeOffset Agora = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    private const string Hash = "a1b2c3d4e5f6";

    private static TrabalhoAssincrono Novo(Guid? org = null) => new(
        org ?? Org,
        Usuario,
        TipoTrabalho.ImportacaoFuncionarios,
        TrabalhoAssincrono.ChaveDeImportacao(org ?? Org, Hash),
        Agora);

    // ------------------------------------------------------------- invariantes

    [Fact]
    public void NasceEnfileiradoESemTentativa()
    {
        var t = Novo();

        Assert.Equal(StatusTrabalho.Enfileirado, t.Status);
        Assert.Equal(0, t.Tentativas);
        Assert.True(t.Pendente);
        Assert.NotEqual(Guid.Empty, t.Id);
    }

    /// <summary>
    /// Trabalho sem dono e o cenario que o gate manda impedir. O construtor
    /// recusa: um trabalho sem organizacao rodaria fora de qualquer tenant.
    /// </summary>
    [Fact]
    public void SemOrganizacaoNaoExiste() =>
        Assert.Throws<ArgumentException>(() => new TrabalhoAssincrono(
            Guid.Empty, Usuario, TipoTrabalho.ImportacaoFuncionarios, "x", Agora));

    [Fact]
    public void SemSolicitanteNaoExiste() =>
        Assert.Throws<ArgumentException>(() => new TrabalhoAssincrono(
            Org, Guid.Empty, TipoTrabalho.ImportacaoFuncionarios, "x", Agora));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SemChaveDeIdempotenciaNaoExiste(string chave) =>
        Assert.Throws<ArgumentException>(() => new TrabalhoAssincrono(
            Org, Usuario, TipoTrabalho.ImportacaoFuncionarios, chave, Agora));

    /// <summary>
    /// ⚠️ Duas organizacoes podem importar o **mesmo arquivo modelo**. Se a
    /// chave fosse so o hash, o trabalho da segunda encontraria o da primeira -
    /// e isso nao seria economia, seria vazamento.
    /// </summary>
    [Fact]
    public void AChaveSeparaOrganizacoesQueImportamOMesmoArquivo()
    {
        var daA = TrabalhoAssincrono.ChaveDeImportacao(Org, Hash);
        var daB = TrabalhoAssincrono.ChaveDeImportacao(Vizinha, Hash);

        Assert.NotEqual(daA, daB);
        Assert.Contains(Hash, daA, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ ciclo de vida

    [Fact]
    public void IniciarContaTentativaEMarcaOInicio()
    {
        var t = Novo();

        Assert.True(t.Iniciar(Agora));
        Assert.Equal(StatusTrabalho.Processando, t.Status);
        Assert.Equal(1, t.Tentativas);
        Assert.Equal(Agora, t.IniciadoEm);
    }

    /// <summary>
    /// ⚠️ **Idempotencia, e o teste que a fase inteira depende.**
    ///
    /// A SQS entrega *pelo menos uma vez*, e nao exatamente uma. Sem esta
    /// recusa, a mesma planilha entregue duas vezes criaria os funcionarios
    /// duas vezes - e ninguem perceberia ate a folha sair errada.
    /// </summary>
    [Fact]
    public void ASegundaEntregaDeUmTrabalhoConcluidoNaoRefazNada()
    {
        var t = Novo();
        var recurso = Guid.CreateVersion7();

        t.Iniciar(Agora);
        t.Concluir(recurso, Agora);

        // A fila entrega de novo.
        Assert.False(t.Iniciar(Agora.AddMinutes(5)));

        // E nada mudou: nem status, nem contagem, nem o resultado.
        Assert.Equal(StatusTrabalho.Concluido, t.Status);
        Assert.Equal(1, t.Tentativas);
        Assert.Equal(recurso, t.IdRecurso);
    }

    [Fact]
    public void ConcluirSemApontarParaNadaERecusado()
    {
        var t = Novo();
        t.Iniciar(Agora);

        Assert.Throws<ArgumentException>(() => t.Concluir(Guid.Empty, Agora));
    }

    [Fact]
    public void OPrimeiroInicioEOQueFicaRegistrado()
    {
        var t = Novo();

        t.Iniciar(Agora);
        t.Falhar("rede", OrcamentoSemCusto.MaximoTentativas, Agora);
        t.Iniciar(Agora.AddMinutes(10));

        // Duas tentativas, mas o inicio e o da primeira: e ele que responde
        // "ha quanto tempo este trabalho esta em andamento?".
        Assert.Equal(2, t.Tentativas);
        Assert.Equal(Agora, t.IniciadoEm);
    }

    // -------------------------------------------------------------- falha e DLQ

    [Fact]
    public void FalhaComTentativaSobrandoVoltaParaAFila()
    {
        var t = Novo();
        t.Iniciar(Agora);

        t.Falhar("banco fora do ar", OrcamentoSemCusto.MaximoTentativas, Agora);

        Assert.Equal(StatusTrabalho.Enfileirado, t.Status);
        Assert.True(t.Pendente);
        Assert.Equal("banco fora do ar", t.Erro);
    }

    /// <summary>
    /// Esgotadas as tentativas, o trabalho para de vez.
    ///
    /// Sem este teto, a mensagem que sempre falha volta para a fila para
    /// sempre - e cada volta consome invocacao e GB-segundo. E assim que um
    /// defeito vira despesa.
    /// </summary>
    [Fact]
    public void EsgotadasAsTentativasOTrabalhoFalhaDeVez()
    {
        var t = Novo();

        for (var i = 0; i < OrcamentoSemCusto.MaximoTentativas; i++)
        {
            t.Iniciar(Agora);
            t.Falhar("sempre falha", OrcamentoSemCusto.MaximoTentativas, Agora);
        }

        Assert.Equal(StatusTrabalho.Falhou, t.Status);
        Assert.False(t.Pendente);
        Assert.Equal(OrcamentoSemCusto.MaximoTentativas, t.Tentativas);
    }

    [Fact]
    public void MotivoGiganteECortadoEmVezDeEstourar()
    {
        var t = Novo();
        t.Iniciar(Agora);

        t.Falhar(new string('x', 5_000), OrcamentoSemCusto.MaximoTentativas, Agora);

        Assert.Equal(TrabalhoAssincrono.TamanhoMaximoErro, t.Erro!.Length);
    }

    [Fact]
    public void RetentarLimpaOErroAnterior()
    {
        var t = Novo();
        t.Iniciar(Agora);
        t.Falhar("erro velho", OrcamentoSemCusto.MaximoTentativas, Agora);

        t.Iniciar(Agora.AddMinutes(1));

        // Erro de tentativa passada exibido como se fosse do estado atual
        // faria a tela mentir sobre um trabalho que esta rodando bem.
        Assert.Null(t.Erro);
    }

    // ----------------------------------------------------------- PertenceA

    [Fact]
    public void ReconheceAPropriaOrganizacao() => Assert.True(Novo().PertenceA(Org));

    [Fact]
    public void RecusaOrganizacaoVizinha() => Assert.False(Novo().PertenceA(Vizinha));

    /// <summary>
    /// Falha fechada: `Guid.Empty` e o que o filtro global devolve quando nao
    /// ha usuario. Ele nao pode casar com organizacao nenhuma - se casasse, o
    /// worker sem contexto passaria a enxergar tudo em vez de nada.
    /// </summary>
    [Fact]
    public void GuidVazioNaoPertenceANinguem() => Assert.False(Novo().PertenceA(Guid.Empty));
}
