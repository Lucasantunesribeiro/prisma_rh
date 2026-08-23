using PrismaRH.Dominio.Contratos;
using PrismaRH.Dominio.Folha;

namespace PrismaRH.Testes.Dominio;

/// <summary>
/// O motor de calculo. Estes testes sao a parte mais importante da Fase 3: o
/// CLAUDE.md secao 27 exige caso normal, limites, zero, arredondamento e
/// mudanca de vigencia para toda regra de folha.
///
/// Um erro aqui nao quebra a aplicacao. Ele paga o valor errado, e ninguem
/// descobre ate alguem conferir o holerite.
/// </summary>
public class MotorCalculoFolhaTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Org = Guid.CreateVersion7();
    private static readonly Guid Funcionario = Guid.CreateVersion7();
    private static readonly Guid Empresa = Guid.CreateVersion7();
    private static readonly Guid CargoUm = Guid.CreateVersion7();
    private static readonly Guid Matriz = Guid.CreateVersion7();

    private static readonly Competencia Agosto = new(2026, 8);
    private static readonly Competencia Fevereiro = new(2026, 2);

    private static ContratoTrabalho Contrato(DateOnly admissao, decimal salario = 3000m) =>
        new(Org, Funcionario, Empresa, "000123", admissao, salario, CargoUm, Matriz, 220, Agora);

    // -----------------------------------------------------------------------
    // Elegibilidade: quem entra na folha
    // -----------------------------------------------------------------------

    [Fact]
    public void ContratoAntigoEAtivo_EntraComOMesCheio()
    {
        var contrato = Contrato(new DateOnly(2025, 3, 1));

        var apuracao = MotorCalculoFolha.Apurar(contrato, Agosto);

        Assert.NotNull(apuracao);
        Assert.Equal(30, apuracao.Avos);
        Assert.Equal(3000m, apuracao.Valor);
    }

    [Fact]
    public void AdmitidoDepoisDaCompetencia_NaoEntra()
    {
        var contrato = Contrato(new DateOnly(2026, 9, 5));

        Assert.False(MotorCalculoFolha.Elegivel(contrato, Agosto));
        Assert.Null(MotorCalculoFolha.Apurar(contrato, Agosto));
    }

    [Fact]
    public void DesligadoAntesDaCompetencia_NaoEntra()
    {
        var contrato = Contrato(new DateOnly(2025, 1, 10));
        contrato.Desligar(new DateOnly(2026, 6, 30));

        Assert.False(MotorCalculoFolha.Elegivel(contrato, Agosto));
    }

    [Fact]
    public void DesligadoNoMeioDoMes_ENTRA_PorqueTrabalhouEsseDias()
    {
        // A decisao de escopo da Fase 3: quem teve vinculo em qualquer dia do
        // mes entra. Excluir essa pessoa deixaria dez dias trabalhados sem
        // pagamento nenhum.
        var contrato = Contrato(new DateOnly(2025, 1, 10));
        contrato.Desligar(new DateOnly(2026, 8, 10));

        var apuracao = MotorCalculoFolha.Apurar(contrato, Agosto);

        Assert.NotNull(apuracao);
        Assert.Equal(10, apuracao.Avos);
        Assert.Equal(1000m, apuracao.Valor);
    }

    // -----------------------------------------------------------------------
    // Proporcionalidade: os avos
    // -----------------------------------------------------------------------

    [Fact]
    public void AdmitidoNoMeioDeUmMesDe31Dias_NaoGanhaUmTrigesimoPrimeiroAvo()
    {
        // Admitido em 20/08 sao 12 dias corridos ate 31/08, mas o mes vale 30
        // avos: o dia 31 nao existe no mes comercial. Sao 11 avos - 30 menos
        // os 19 dias anteriores a admissao.
        var contrato = Contrato(new DateOnly(2026, 8, 20));

        var apuracao = MotorCalculoFolha.Apurar(contrato, Agosto);

        Assert.NotNull(apuracao);
        Assert.Equal(11, apuracao.Avos);
        Assert.Equal(1100m, apuracao.Valor);
    }

    [Fact]
    public void FevereiroInteiro_Vale30Avos_E_PagaOSalarioCheio()
    {
        // O teste que impede o erro mais caro deste arquivo: contar dias
        // corridos daria 28 avos, e todo funcionario perderia dois dias de
        // salario todo mes de fevereiro.
        var contrato = Contrato(new DateOnly(2025, 5, 1));

        var apuracao = MotorCalculoFolha.Apurar(contrato, Fevereiro);

        Assert.NotNull(apuracao);
        Assert.Equal(30, apuracao.Avos);
        Assert.Equal(3000m, apuracao.Valor);
    }

    [Fact]
    public void FevereiroParcial_ContaAtePenultimoDiaReal()
    {
        var contrato = Contrato(new DateOnly(2026, 2, 15));

        var apuracao = MotorCalculoFolha.Apurar(contrato, Fevereiro);

        Assert.NotNull(apuracao);
        Assert.Equal(14, apuracao.Avos);
    }

    [Fact]
    public void AdmitidoNoDia31_RecebePeloMenosUmAvo()
    {
        // min(31,30) - 31 + 1 daria zero. Trabalhar um dia e receber nada e
        // pior do que a imprecisao de arredondar para cima.
        var contrato = Contrato(new DateOnly(2026, 8, 31));

        var apuracao = MotorCalculoFolha.Apurar(contrato, Agosto);

        Assert.NotNull(apuracao);
        Assert.Equal(1, apuracao.Avos);
        Assert.Equal(100m, apuracao.Valor);
    }

    [Fact]
    public void AdmitidoEDesligadoNoMesmoMes_ContaSoOIntervalo()
    {
        var contrato = Contrato(new DateOnly(2026, 8, 5));
        contrato.Desligar(new DateOnly(2026, 8, 20));

        var apuracao = MotorCalculoFolha.Apurar(contrato, Agosto);

        Assert.NotNull(apuracao);
        Assert.Equal(16, apuracao.Avos);
        Assert.Equal(1600m, apuracao.Valor);
    }

    [Fact]
    public void AdmitidoNoPrimeiroDia_JaVale30Avos()
    {
        var contrato = Contrato(new DateOnly(2026, 8, 1));

        Assert.Equal(30, MotorCalculoFolha.Apurar(contrato, Agosto)!.Avos);
    }

    // -----------------------------------------------------------------------
    // Vigencia: o aumento no meio do mes
    // -----------------------------------------------------------------------

    [Fact]
    public void AumentoNoMeioDoMes_RepartePorVigencia_E_NaoPagaOMesInteiroNoSalarioNovo()
    {
        var contrato = Contrato(new DateOnly(2025, 1, 10));
        contrato.RegistrarAlteracao(
            new DateOnly(2026, 8, 15), 3600m, CargoUm, Matriz, 220,
            MotivoVigencia.AlteracaoSalarial, Agora);

        var apuracao = MotorCalculoFolha.Apurar(contrato, Agosto);

        Assert.NotNull(apuracao);
        Assert.Equal(30, apuracao.Avos);

        // 3000 x 14/30 = 1400,00 e 3600 x 16/30 = 1920,00.
        Assert.Equal(3320m, apuracao.Valor);

        // Nem o salario velho (3000) nem o novo (3600) sozinhos.
        Assert.NotEqual(3000m, apuracao.Valor);
        Assert.NotEqual(3600m, apuracao.Valor);

        // A referencia do holerite e a vigencia do fim do periodo.
        Assert.Equal(3600m, apuracao.SalarioReferencia);
    }

    [Fact]
    public void AumentoEmFevereiro_OsAvosDasVigenciasAindaSomam30()
    {
        // Dois trechos de 14 dias somam 28 num mes de 28 dias, mas o mes vale
        // 30. Sem o ajuste na ultima vigencia, o funcionario perderia dois
        // avos so por ter recebido aumento em fevereiro.
        var contrato = Contrato(new DateOnly(2025, 1, 10));
        contrato.RegistrarAlteracao(
            new DateOnly(2026, 2, 15), 3600m, CargoUm, Matriz, 220,
            MotivoVigencia.AlteracaoSalarial, Agora);

        var apuracao = MotorCalculoFolha.Apurar(contrato, Fevereiro);

        Assert.NotNull(apuracao);
        Assert.Equal(30, apuracao.Avos);
        Assert.Equal(3320m, apuracao.Valor);
    }

    [Fact]
    public void AumentoNoMeioDoMes_ExplicaOsDoisTrechosNaMemoria()
    {
        var contrato = Contrato(new DateOnly(2025, 1, 10));
        contrato.RegistrarAlteracao(
            new DateOnly(2026, 8, 15), 3600m, CargoUm, Matriz, 220,
            MotivoVigencia.AlteracaoSalarial, Agora);

        var apuracao = MotorCalculoFolha.Apurar(contrato, Agosto)!;

        // Dois trechos mais a linha de soma.
        Assert.Equal(3, apuracao.Passos.Count);
        Assert.Contains("01/08", apuracao.Passos[0].Descricao);
        Assert.Contains("14/08", apuracao.Passos[0].Descricao);
        Assert.Equal(1400m, apuracao.Passos[0].Valor);
        Assert.Equal(1920m, apuracao.Passos[1].Valor);
        Assert.Equal(3320m, apuracao.Passos[^1].Valor);
    }

    [Fact]
    public void AumentoDepoisDaCompetencia_NaoAfetaAFolhaAntiga()
    {
        // A prova do criterio de aceite "alteracoes nao reescrevem o passado".
        var contrato = Contrato(new DateOnly(2025, 1, 10));
        contrato.RegistrarAlteracao(
            new DateOnly(2026, 9, 1), 9000m, CargoUm, Matriz, 220,
            MotivoVigencia.AlteracaoSalarial, Agora);

        Assert.Equal(3000m, MotorCalculoFolha.Apurar(contrato, Agosto)!.Valor);
    }

    [Fact]
    public void MesInteiroComUmaVigenciaSo_TemUmPassoSoNaMemoria()
    {
        var apuracao = MotorCalculoFolha.Apurar(Contrato(new DateOnly(2025, 3, 1)), Agosto)!;

        var passo = Assert.Single(apuracao.Passos);
        Assert.Equal("3.000,00 x 30/30", passo.Expressao);
        Assert.Equal(3000m, passo.Valor);
    }

    // -----------------------------------------------------------------------
    // Arredondamento
    // -----------------------------------------------------------------------

    [Fact]
    public void ValorQuebrado_ArredondaParaOCentavo()
    {
        var contrato = Contrato(new DateOnly(2026, 8, 24), salario: 1000m);

        // 24/08 sao 7 avos: 1000 x 7/30 = 233,3333...
        var apuracao = MotorCalculoFolha.Apurar(contrato, Agosto)!;

        Assert.Equal(7, apuracao.Avos);
        Assert.Equal(233.33m, apuracao.Valor);
    }

    [Theory]
    [InlineData(0.125, 0.13)]
    [InlineData(2.345, 2.35)]
    [InlineData(0.135, 0.14)]
    [InlineData(-0.125, -0.13)]
    public void Arredondar_MeioCentavoSobe_E_NaoSegueOBanqueiro(decimal valor, decimal esperado)
    {
        // MidpointRounding.ToEven, que e o padrao do .NET, devolveria 0,12 e
        // 2,34 nos dois primeiros casos. O funcionario perderia um centavo por
        // causa da paridade do digito anterior, sem forma de entender o motivo.
        Assert.Equal(esperado, Dinheiro.Arredondar(valor));
    }
}
