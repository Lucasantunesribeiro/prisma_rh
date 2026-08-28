using PrismaRH.Dominio.Contratos;
using PrismaRH.Dominio.Ferias;

namespace PrismaRH.Testes.Dominio;

/// <summary>
/// Fase 4E, etapa 1: os periodos aquisitivos de ferias.
///
/// FONTES (CLAUDE.md secao 29):
/// - CLT art. 130 - 12 meses de vigencia dao direito a ferias;
/// - CLT art. 134 - concessao nos 12 meses subsequentes;
/// - CLT art. 137 - concedidas depois disso, remuneracao EM DOBRO.
///
/// Esta etapa nao calcula dinheiro. Ela responde "a quantos periodos esta
/// pessoa tem direito, e algum esta prestes a vencer?".
/// </summary>
public class PeriodoAquisitivoTestes
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
            contrato.Desligar(data, MotivoDesligamento.DispensaSemJustaCausa);
        }

        return contrato;
    }

    // --------------------------------------------------------- os periodos

    [Fact]
    public void PrimeiroPeriodo_VaiDaAdmissaoAVesperaDoAniversario()
    {
        var contrato = Contrato(new DateOnly(2024, 3, 1));

        var primeiro = PeriodosAquisitivos.De(contrato, new DateOnly(2024, 6, 1))[0];

        Assert.Equal(1, primeiro.Numero);
        Assert.Equal(new DateOnly(2024, 3, 1), primeiro.Inicio);
        Assert.Equal(new DateOnly(2025, 2, 28), primeiro.Fim);
        Assert.Equal(30, primeiro.DiasDireito);
    }

    [Fact]
    public void PeriodosSaoContiguos_SemBuracoNemSobreposicao()
    {
        var contrato = Contrato(new DateOnly(2020, 7, 15));

        var periodos = PeriodosAquisitivos.De(contrato, new DateOnly(2026, 8, 28));

        Assert.Equal(7, periodos.Count);

        for (var i = 1; i < periodos.Count; i++)
        {
            // O proximo comeca no dia seguinte ao fim do anterior. Um buraco
            // aqui faria a pessoa perder dias de direito sem nada aparecer.
            Assert.Equal(periodos[i - 1].Fim.AddDays(1), periodos[i].Inicio);
            Assert.Equal(periodos[i - 1].Numero + 1, periodos[i].Numero);
        }
    }

    [Fact]
    public void NovoPeriodo_ComecaNoAniversario()
    {
        var contrato = Contrato(new DateOnly(2024, 3, 1));

        // Vespera do aniversario: so um periodo.
        Assert.Single(PeriodosAquisitivos.De(contrato, new DateOnly(2025, 2, 28)));

        // No aniversario: o segundo ja nasceu.
        Assert.Equal(2, PeriodosAquisitivos.De(contrato, new DateOnly(2025, 3, 1)).Count);
    }

    [Fact]
    public void AntesDaAdmissao_NaoHaPeriodoAlgum() =>
        Assert.Empty(PeriodosAquisitivos.De(
            Contrato(new DateOnly(2026, 9, 1)), new DateOnly(2026, 8, 28)));

    [Fact]
    public void NoDiaDaAdmissao_JaExisteOPrimeiroPeriodoEmAndamento()
    {
        var admissao = new DateOnly(2026, 8, 28);

        var periodos = PeriodosAquisitivos.De(Contrato(admissao), admissao);

        Assert.Equal(SituacaoPeriodoAquisitivo.EmAndamento, Assert.Single(periodos).SituacaoEm(admissao));
    }

    // ------------------------------------------------------------ situacao

    [Fact]
    public void DentroDosDozeMeses_EstaEmAndamento()
    {
        var p = PeriodosAquisitivos.De(Contrato(new DateOnly(2026, 1, 1)), new DateOnly(2026, 8, 28))[0];

        Assert.Equal(SituacaoPeriodoAquisitivo.EmAndamento, p.SituacaoEm(new DateOnly(2026, 8, 28)));
        Assert.False(p.EmDobraSeConcedidoEm(new DateOnly(2026, 8, 28)));
    }

    [Fact]
    public void CompletouDozeMeses_EstaAdquirido()
    {
        var contrato = Contrato(new DateOnly(2025, 1, 1));
        var p = PeriodosAquisitivos.De(contrato, new DateOnly(2026, 8, 28))[0];

        // Aquisitivo 01/01/2025 a 31/12/2025.
        Assert.Equal(new DateOnly(2025, 12, 31), p.Fim);
        Assert.Equal(SituacaoPeriodoAquisitivo.EmAndamento, p.SituacaoEm(new DateOnly(2025, 12, 31)));
        Assert.Equal(SituacaoPeriodoAquisitivo.Adquirido, p.SituacaoEm(new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public void ConcessivoVaiDoDiaSeguinteAteUmAnoDepoisDoFim()
    {
        var p = PeriodosAquisitivos.De(Contrato(new DateOnly(2025, 1, 1)), new DateOnly(2026, 8, 28))[0];

        Assert.Equal(new DateOnly(2026, 1, 1), p.InicioConcessao);
        Assert.Equal(new DateOnly(2026, 12, 31), p.LimiteConcessao);
    }

    [Fact]
    public void NoUltimoDiaDoConcessivo_AindaNaoEDobra()
    {
        var p = PeriodosAquisitivos.De(Contrato(new DateOnly(2025, 1, 1)), new DateOnly(2026, 8, 28))[0];

        // O limite PERTENCE ao prazo. Um erro de <= para < aqui pagaria em
        // dobro ferias concedidas no prazo.
        Assert.Equal(SituacaoPeriodoAquisitivo.Adquirido, p.SituacaoEm(new DateOnly(2026, 12, 31)));
        Assert.False(p.EmDobraSeConcedidoEm(new DateOnly(2026, 12, 31)));
    }

    [Fact]
    public void UmDiaDepoisDoLimite_VenceEViraDobra()
    {
        var p = PeriodosAquisitivos.De(Contrato(new DateOnly(2025, 1, 1)), new DateOnly(2027, 1, 1))[0];

        Assert.Equal(SituacaoPeriodoAquisitivo.Vencido, p.SituacaoEm(new DateOnly(2027, 1, 1)));
        Assert.True(p.EmDobraSeConcedidoEm(new DateOnly(2027, 1, 1)));
    }

    // ---------------------------------------------------------- consultas

    [Fact]
    public void Adquiridos_NaoIncluemOEmAndamento()
    {
        var contrato = Contrato(new DateOnly(2023, 5, 10));
        var hoje = new DateOnly(2026, 8, 28);

        var todos = PeriodosAquisitivos.De(contrato, hoje);
        var adquiridos = PeriodosAquisitivos.Adquiridos(contrato, hoje);

        Assert.Equal(4, todos.Count);
        Assert.Equal(3, adquiridos.Count);
        Assert.DoesNotContain(adquiridos, p => p.SituacaoEm(hoje) == SituacaoPeriodoAquisitivo.EmAndamento);

        // Do mais ANTIGO para o mais novo: quem vence primeiro deve ser
        // baixado primeiro quando a etapa 2 conceder ferias.
        Assert.Equal(1, adquiridos[0].Numero);
    }

    [Fact]
    public void EmAndamento_EOUltimo()
    {
        var contrato = Contrato(new DateOnly(2023, 5, 10));
        var hoje = new DateOnly(2026, 8, 28);

        var atual = PeriodosAquisitivos.EmAndamento(contrato, hoje);

        Assert.NotNull(atual);
        Assert.Equal(4, atual!.Numero);
        Assert.Equal(new DateOnly(2026, 5, 10), atual.Inicio);
    }

    [Fact]
    public void DiasParaCompletar_ContaOProprioDiaDoFim()
    {
        var p = PeriodosAquisitivos.De(Contrato(new DateOnly(2026, 1, 1)), new DateOnly(2026, 8, 28))[0];

        // Fim em 31/12/2026. Em 31/12 falta 1 dia: o proprio.
        Assert.Equal(1, p.DiasParaCompletar(new DateOnly(2026, 12, 31)));
        Assert.Equal(2, p.DiasParaCompletar(new DateOnly(2026, 12, 30)));
        Assert.Equal(0, p.DiasParaCompletar(new DateOnly(2027, 1, 1)));
    }

    // -------------------------------------------------------- desligamento

    [Fact]
    public void ContratoDesligado_ParaDeGerarPeriodos()
    {
        var contrato = Contrato(new DateOnly(2020, 1, 1), desligamento: new DateOnly(2023, 6, 30));

        var periodos = PeriodosAquisitivos.De(contrato, new DateOnly(2026, 8, 28));

        // Periodos 1, 2 e 3 completos, e o 4o (01/01/2023 a 31/12/2023)
        // comecou antes do desligamento e por isso aparece.
        Assert.Equal(4, periodos.Count);
        Assert.Equal(new DateOnly(2023, 1, 1), periodos[^1].Inicio);
    }

    [Fact]
    public void ContratoDesligado_NaoInventaPeriodoDepoisDoFim()
    {
        var contrato = Contrato(new DateOnly(2020, 1, 1), desligamento: new DateOnly(2022, 12, 31));

        var periodos = PeriodosAquisitivos.De(contrato, new DateOnly(2026, 8, 28));

        // O 3o periodo termina exatamente no desligamento. O 4o comecaria em
        // 01/01/2023, depois dele: nao existe.
        Assert.Equal(3, periodos.Count);
        Assert.Equal(new DateOnly(2022, 12, 31), periodos[^1].Fim);
    }

    // ------------------------------------------------------- caso de borda

    [Fact]
    public void AdmissaoEm29DeFevereiro_UsaOAniversarioAjustado()
    {
        // 2024 e bissexto; 2025 nao. AddYears leva 29/02/2024 para 28/02/2025,
        // entao o primeiro periodo termina em 27/02/2025 e o segundo comeca em
        // 28/02/2025.
        //
        // O efeito e de UM DIA e so atinge quem foi admitido em 29/02. A
        // alternativa - fechar em 28/02 e comecar em 01/03 - nao e obviamente
        // mais correta, e a lei nao trata do caso. Registrado como decisao,
        // nao como certeza.
        var contrato = Contrato(new DateOnly(2024, 2, 29));

        var periodos = PeriodosAquisitivos.De(contrato, new DateOnly(2026, 8, 28));

        Assert.Equal(new DateOnly(2024, 2, 29), periodos[0].Inicio);
        Assert.Equal(new DateOnly(2025, 2, 27), periodos[0].Fim);
        Assert.Equal(new DateOnly(2025, 2, 28), periodos[1].Inicio);

        // Continua contiguo, que e a propriedade que nao pode quebrar.
        Assert.Equal(periodos[0].Fim.AddDays(1), periodos[1].Inicio);
    }

    [Fact]
    public void ContratoLongo_NaoPerdeNenhumPeriodo()
    {
        var contrato = Contrato(new DateOnly(2000, 1, 1));

        var periodos = PeriodosAquisitivos.De(contrato, new DateOnly(2026, 8, 28));

        Assert.Equal(27, periodos.Count);
        Assert.Equal(new DateOnly(2026, 1, 1), periodos[^1].Inicio);
    }
}
