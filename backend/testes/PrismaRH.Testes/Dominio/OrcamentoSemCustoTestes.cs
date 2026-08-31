using PrismaRH.Dominio.Importacao;

namespace PrismaRH.Testes.Dominio;

/// <summary>
/// Os guardrails de custo zero.
///
/// Testar constante parece cerimonia, e nao e: cada numero foi derivado de uma
/// franquia de servico, e **a conta pode deixar de fechar sem ninguem
/// perceber** - basta alguem dobrar um teto por conveniencia, e a franquia
/// passa a ser atravessada meses depois, numa fatura.
///
/// Free Tier **nao e teto de gasto**. Passar da franquia nao bloqueia nada, so
/// cobra. Estes testes travam a aritmetica que mantem o consumo abaixo dela.
/// </summary>
public sealed class OrcamentoSemCustoTestes
{
    // ------------------------------------------------------------------ lambda

    /// <summary>
    /// A franquia permanente da Lambda e de 400.000 GB-segundo por mes. Com
    /// 128 MB isso da 3,2 milhoes de segundos - e uma importacao leva segundos.
    /// </summary>
    [Fact]
    public void AMemoriaEscolhidaCabeNaFranquiaDaLambda()
    {
        const int franquiaGbSegundoPorMes = 400_000;

        var gbPorInvocacao = OrcamentoSemCusto.MemoriaLambdaMb / 1024.0;
        var segundosGratuitos = franquiaGbSegundoPorMes / gbPorInvocacao;

        Assert.Equal(3_200_000, segundosGratuitos);
    }

    /// <summary>
    /// ⚠️ O guardrail que transforma "provavelmente pouco" em **teto fisico**.
    ///
    /// Com concorrencia reservada 1, o consumo maximo nao depende de ninguem se
    /// comportar: uma unica execucao por vez, no maior tempo possivel, durante
    /// **o mes inteiro sem parar**, ainda cabe na franquia.
    /// </summary>
    [Fact]
    public void MesmoOcupada30DiasSemParraAFuncaoCabeNaFranquia()
    {
        const int franquiaGbSegundoPorMes = 400_000;
        const int segundosNoMes = 30 * 24 * 60 * 60;

        var gbPorExecucao = OrcamentoSemCusto.MemoriaLambdaMb / 1024.0;
        var piorCasoGbSegundo = OrcamentoSemCusto.ConcorrenciaReservada * gbPorExecucao * segundosNoMes;

        Assert.True(
            piorCasoGbSegundo < franquiaGbSegundoPorMes,
            $"pior caso {piorCasoGbSegundo:N0} GB-s contra franquia de {franquiaGbSegundoPorMes:N0}");
    }

    [Fact]
    public void NaoHaConcorrenciaProvisionada()
    {
        // Provisioned concurrency e PAGA e mantem execucoes quentes por hora.
        // Reserved concurrency e gratuita e so limita. Sao coisas diferentes, e
        // confundi-las e como se cria custo fixo sem querer.
        Assert.Equal(1, OrcamentoSemCusto.ConcorrenciaReservada);
    }

    /// <summary>
    /// O visibility timeout precisa ser bem maior que o timeout da funcao. Se
    /// fossem iguais, uma execucao lenta veria a mensagem reaparecer na fila
    /// **antes de terminar** - e o mesmo arquivo rodaria duas vezes ao mesmo
    /// tempo, consumindo franquia em dobro.
    /// </summary>
    [Fact]
    public void OVisibilityTimeoutDaFolgaSobreOTimeoutDaFuncao() =>
        Assert.True(
            OrcamentoSemCusto.VisibilityTimeoutSegundos >= OrcamentoSemCusto.TimeoutLambdaSegundos * 6);

    // --------------------------------------------------------------------- sqs

    /// <summary>
    /// ⚠️ O consumo que existe **mesmo sem ninguem usar o sistema**.
    ///
    /// O poller da Lambda pergunta "tem mensagem?" continuamente, e cada
    /// pergunta e uma requisicao cobrada. Com long polling de 20 s e os 2
    /// pollers que a AWS mantem numa fila parada, o piso mensal fica em cerca
    /// de um quarto da franquia.
    ///
    /// Sem long polling isso explodiria: o mesmo poller perguntaria varias
    /// vezes por segundo.
    /// </summary>
    [Fact]
    public void AFilaParadaConsomeMenosDeMetadeDaFranquiaDeSqs()
    {
        const int franquiaRequisicoesPorMes = 1_000_000;
        const int pollersOciosos = 2; // o minimo que a AWS reduz quando nao ha trafego
        const int segundosNoMes = 30 * 24 * 60 * 60;

        var pollsPorPollerPorMes = segundosNoMes / OrcamentoSemCusto.EsperaLongPollingSegundos;
        var pisoMensal = pollersOciosos * pollsPorPollerPorMes;

        Assert.True(
            pisoMensal < franquiaRequisicoesPorMes / 2,
            $"piso de {pisoMensal:N0} requisicoes/mes contra franquia de {franquiaRequisicoesPorMes:N0}");
    }

    [Fact]
    public void OLongPollingEstaNoMaximoQueASqsAceita() =>
        Assert.Equal(20, OrcamentoSemCusto.EsperaLongPollingSegundos);

    /// <summary>
    /// O limite duro da SQS e 256 KB. O teto daqui e muito menor de proposito:
    /// a mensagem carrega ids, e o arquivo nunca cabe nela.
    /// </summary>
    [Fact]
    public void AMensagemNaoCabeUmArquivoNemDeLonge()
    {
        Assert.True(OrcamentoSemCusto.TamanhoMaximoMensagemBytes < 256 * 1024);

        Assert.True(
            OrcamentoSemCusto.TamanhoMaximoMensagemBytes
                < OrcamentoSemCusto.TamanhoMaximoArquivoBytes / 100);
    }

    [Fact]
    public void ORetryTemTetoParaNaoVirarConta() =>
        Assert.InRange(OrcamentoSemCusto.MaximoTentativas, 1, 5);

    // ---------------------------------------------------- orcamento de blobs

    /// <summary>
    /// ⚠️ **A correcao de arquitetura de 31/08/2026.**
    ///
    /// O limite do Neon gratuito e **por projeto**, e nao por tenant. Um teto
    /// "por organizacao" seria ilusao aritmetica: dez organizacoes o
    /// multiplicariam por dez e estourariam o projeto, cada uma respeitando o
    /// proprio limite.
    /// </summary>
    [Fact]
    public void OOrcamentoDeBlobsEUmaFracaoDoProjetoInteiro()
    {
        const long neonGratuitoBytes = 512L * 1024 * 1024; // 0,5 GB POR PROJETO

        var fracao = (double)OrcamentoSemCusto.ArmazenamentoGlobalMaximoBytes / neonGratuitoBytes;

        Assert.InRange(fracao, 0.05, 0.15);
    }

    [Fact]
    public void CabemDezArquivosNoTetoAoMesmoTempo() =>
        Assert.Equal(10, OrcamentoSemCusto.ArquivosNoTeto);

    [Fact]
    public void ArquivoQueCabeEAceito() =>
        Assert.True(OrcamentoSemCusto.CabeNoOrcamentoGlobal(0, 5 * 1024 * 1024));

    [Fact]
    public void ArquivoExatamenteNoTetoGlobalAindaCabe() =>
        Assert.True(OrcamentoSemCusto.CabeNoOrcamentoGlobal(
            OrcamentoSemCusto.ArmazenamentoGlobalMaximoBytes - 1024, 1024));

    /// <summary>
    /// A borda importa: e nela que um "menor ou igual" trocado por "menor"
    /// deixa passar exatamente o caso que o limite existia para barrar.
    /// </summary>
    [Fact]
    public void UmByteAlemDoTetoGlobalERecusado() =>
        Assert.False(OrcamentoSemCusto.CabeNoOrcamentoGlobal(
            OrcamentoSemCusto.ArmazenamentoGlobalMaximoBytes, 1));

    /// <summary>
    /// Arquivo maior que o teto individual e recusado **mesmo com o banco
    /// vazio** - os dois limites sao independentes.
    /// </summary>
    [Fact]
    public void ArquivoAcimaDoTetoIndividualERecusadoAindaQueHajaEspaco() =>
        Assert.False(OrcamentoSemCusto.CabeNoOrcamentoGlobal(
            0, OrcamentoSemCusto.TamanhoMaximoArquivoBytes + 1));

    /// <summary>
    /// ⚠️ O orcamento e **compartilhado**, e este teste diz isso em codigo.
    ///
    /// Nove arquivos de 5 MB de quem quer que seja deixam espaco para so mais
    /// um. A decima primeira importacao e recusada, venha de qual organizacao
    /// vier - e a mensagem precisa deixar claro que o motivo e espaco, e nao
    /// permissao.
    /// </summary>
    [Fact]
    public void OOrcamentoEDisputadoEntreTodasAsOrganizacoes()
    {
        var noveArquivos = 9L * OrcamentoSemCusto.TamanhoMaximoArquivoBytes;

        Assert.True(OrcamentoSemCusto.CabeNoOrcamentoGlobal(
            noveArquivos, OrcamentoSemCusto.TamanhoMaximoArquivoBytes));

        var dezArquivos = 10L * OrcamentoSemCusto.TamanhoMaximoArquivoBytes;

        Assert.False(OrcamentoSemCusto.CabeNoOrcamentoGlobal(dezArquivos, 1));
    }

    [Theory]
    [InlineData(-1, 100)]
    [InlineData(0, 0)]
    [InlineData(0, -5)]
    public void ValorAbsurdoEstouraEmVezDePassarBatido(long ja, long novo) =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => OrcamentoSemCusto.CabeNoOrcamentoGlobal(ja, novo));

    // -------------------------------------------------------------- retencoes

    /// <summary>
    /// A DLQ guarda por mais tempo que o blob, e isso e proposital: o arquivo
    /// bem-sucedido nao precisa sobreviver, mas o que falhou precisa esperar
    /// alguem olhar.
    /// </summary>
    [Fact]
    public void ADlqGuardaPorMaisTempoQueOBlob() =>
        Assert.True(OrcamentoSemCusto.RetencaoDlqDias > OrcamentoSemCusto.RetencaoBlob.TotalDays);

    /// <summary>
    /// Log tecnico e o que dura menos. Ele responde "por que falhou ontem"; o
    /// que precisa durar anos e a trilha de auditoria, que vive no banco.
    /// </summary>
    [Fact]
    public void OLogEOQueDuraMenos()
    {
        Assert.True(OrcamentoSemCusto.RetencaoLogsDias <= OrcamentoSemCusto.RetencaoBlob.TotalDays);
        Assert.True(OrcamentoSemCusto.RetencaoLogsDias < OrcamentoSemCusto.RetencaoDlqDias);

        // Retencao infinita e o padrao do CloudWatch quando ninguem define - e
        // e assim que a franquia e atravessada meses depois.
        Assert.InRange(OrcamentoSemCusto.RetencaoLogsDias, 1, 7);
    }
}
