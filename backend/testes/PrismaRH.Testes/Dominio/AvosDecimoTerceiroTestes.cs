using PrismaRH.Dominio.Contratos;
using PrismaRH.Dominio.DecimoTerceiro;

namespace PrismaRH.Testes.Dominio;

/// <summary>
/// Fase 4F, etapa 1: os avos de 13o salario.
///
/// FONTE (CLAUDE.md secao 29): Lei n. 4.090, de 13/07/1962 - 1/12 da
/// remuneracao por MES DE SERVICO, e a fracao IGUAL OU SUPERIOR a 15 dias e
/// havida como mes integral.
///
/// Esta etapa nao calcula dinheiro. Ela responde "a quantos avos esta pessoa
/// tem direito neste ano, e por que".
/// </summary>
public class AvosDecimoTerceiroTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Org = Guid.CreateVersion7();

    private static ContratoTrabalho Contrato(DateOnly admissao, DateOnly? desligamento = null)
    {
        var contrato = new ContratoTrabalho(
            Org, Guid.CreateVersion7(), Guid.CreateVersion7(), "1001",
            admissao, 3000m, Guid.CreateVersion7(), Guid.CreateVersion7(), 220, Agora);

        if (desligamento is { } data)
        {
            contrato.Desligar(data);
        }

        return contrato;
    }

    private static int AvosDe(DateOnly admissao, int ano, DateOnly? desligamento = null) =>
        AvosDecimoTerceiro.Apurar(Contrato(admissao, desligamento), ano).Avos;

    // ------------------------------------------------------------ ano cheio

    [Fact]
    public void AnoInteiroTrabalhado_DaDozeAvos()
    {
        var apuracao = AvosDecimoTerceiro.Apurar(Contrato(new DateOnly(2020, 1, 1)), 2026);

        Assert.Equal(12, apuracao.Avos);
        Assert.Equal("12/12", apuracao.Fracao);
        Assert.True(apuracao.AnoCompleto);
        Assert.All(apuracao.Meses, m => Assert.True(m.Conta));
    }

    [Fact]
    public void AnoAntesDaAdmissao_NaoDaAvoAlgum()
    {
        var apuracao = AvosDecimoTerceiro.Apurar(Contrato(new DateOnly(2026, 1, 1)), 2025);

        Assert.Equal(0, apuracao.Avos);
        Assert.All(apuracao.Meses, m => Assert.Equal(0, m.DiasTrabalhados));
    }

    // ------------------------------------------------------- os 15 dias

    [Theory]
    // Marco tem 31 dias. Admitido no dia 17: 17 a 31 sao 15 dias EXATOS.
    // Igual ou superior a 15 conta - por isso marco entra, e o ano da 10.
    [InlineData(17, 10)]
    // Dia 18: 14 dias. Nao conta, e o ano da 9.
    [InlineData(18, 9)]
    public void QuinzeDiasEXATOS_ContamComoMesInteiro(int diaDaAdmissao, int esperado) =>
        Assert.Equal(esperado, AvosDe(new DateOnly(2026, 3, diaDaAdmissao), 2026));

    [Fact]
    public void OMesDaAdmissao_ContaSeTiverQuinzeDias()
    {
        var apuracao = AvosDecimoTerceiro.Apurar(Contrato(new DateOnly(2026, 3, 17)), 2026);

        var marco = apuracao.Meses[2];
        Assert.Equal(3, marco.Mes);
        Assert.Equal(15, marco.DiasTrabalhados);
        Assert.True(marco.Conta);

        var fevereiro = apuracao.Meses[1];
        Assert.Equal(0, fevereiro.DiasTrabalhados);
        Assert.False(fevereiro.Conta);
        Assert.Equal("sem vinculo no mes", fevereiro.Motivo);
    }

    [Fact]
    public void FevereiroDeAnoNAOBissexto_TemVinteEOitoDias()
    {
        // 2026 nao e bissexto. Admitido em 14/02: 14 a 28 sao 15 dias. Conta.
        Assert.True(AvosDecimoTerceiro.Apurar(
            Contrato(new DateOnly(2026, 2, 14)), 2026).Meses[1].Conta);

        // Em 15/02: 14 dias. Nao conta.
        Assert.False(AvosDecimoTerceiro.Apurar(
            Contrato(new DateOnly(2026, 2, 15)), 2026).Meses[1].Conta);
    }

    [Fact]
    public void FevereiroBissexto_GanhaUmDia()
    {
        // 2028 e bissexto: 29 dias. Admitido em 15/02, sao 15 dias - conta,
        // ao contrario do mesmo dia num ano comum.
        Assert.True(AvosDecimoTerceiro.Apurar(
            Contrato(new DateOnly(2028, 2, 15)), 2028).Meses[1].Conta);
    }

    // ----------------------------------------------------- desligamento

    [Theory]
    // Desligado em 15/07: julho tem 15 dias, conta. Janeiro a julho = 7.
    [InlineData(15, 7)]
    // Em 14/07: 14 dias, nao conta. Janeiro a junho = 6.
    [InlineData(14, 6)]
    public void OMesDoDesligamento_SegueAMesmaRegra(int diaDaSaida, int esperado) =>
        Assert.Equal(esperado, AvosDe(
            new DateOnly(2020, 1, 1), 2026, desligamento: new DateOnly(2026, 7, diaDaSaida)));

    [Fact]
    public void DepoisDoDesligamento_OsMesesNaoContam()
    {
        var apuracao = AvosDecimoTerceiro.Apurar(
            Contrato(new DateOnly(2020, 1, 1), new DateOnly(2026, 7, 20)), 2026);

        Assert.Equal(7, apuracao.Avos);
        Assert.All(apuracao.Meses.Skip(7), m => Assert.Equal(0, m.DiasTrabalhados));
    }

    [Fact]
    public void AdmitidoEDesligadoNoMesmoAno_ContaSoOMeio()
    {
        // 10/03 a 20/09. Marco tem 22 dias (conta), setembro tem 20 (conta).
        var apuracao = AvosDecimoTerceiro.Apurar(
            Contrato(new DateOnly(2026, 3, 10), new DateOnly(2026, 9, 20)), 2026);

        Assert.Equal(7, apuracao.Avos);
        Assert.True(apuracao.Meses[2].Conta);
        Assert.True(apuracao.Meses[8].Conta);
        Assert.False(apuracao.Meses[9].Conta);
    }

    [Fact]
    public void AdmitidoEDesligadoNoMesmoMES_ContaSeAlcancarQuinzeDias()
    {
        // 05/06 a 19/06: 15 dias. Um avo.
        Assert.Equal(1, AvosDe(
            new DateOnly(2026, 6, 5), 2026, desligamento: new DateOnly(2026, 6, 19)));

        // 05/06 a 18/06: 14 dias. Nenhum.
        Assert.Equal(0, AvosDe(
            new DateOnly(2026, 6, 5), 2026, desligamento: new DateOnly(2026, 6, 18)));
    }

    // ---------------------------------------------------------- estrutura

    [Fact]
    public void SempreDevolveOsDozeMeses()
    {
        // Mesmo os que nao contam: a tela precisa mostrar POR QUE fevereiro
        // ficou de fora, e nao apenas omiti-lo.
        var apuracao = AvosDecimoTerceiro.Apurar(Contrato(new DateOnly(2026, 11, 20)), 2026);

        Assert.Equal(12, apuracao.Meses.Count);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12], apuracao.Meses.Select(m => m.Mes));
    }

    [Fact]
    public void MotivoExplicaCadaCaso()
    {
        var apuracao = AvosDecimoTerceiro.Apurar(Contrato(new DateOnly(2026, 3, 20)), 2026);

        Assert.Equal("sem vinculo no mes", apuracao.Meses[0].Motivo);
        Assert.Equal("so 12 dias, menos que 15", apuracao.Meses[2].Motivo);
        Assert.Equal("30 dias trabalhados", apuracao.Meses[3].Motivo);
    }

    [Fact]
    public void AnoForaDoIntervalo_ERecusado()
    {
        var contrato = Contrato(new DateOnly(2020, 1, 1));

        Assert.Throws<ArgumentOutOfRangeException>(() => AvosDecimoTerceiro.Apurar(contrato, 1999));
        Assert.Throws<ArgumentOutOfRangeException>(() => AvosDecimoTerceiro.Apurar(contrato, 2101));
    }
}
