using PrismaRH.Dominio.Contratos;
using PrismaRH.Dominio.Rescisao;

namespace PrismaRH.Testes.Dominio;

/// <summary>
/// Fase 4G, etapa 2: as verbas rescisorias.
///
/// FONTES (CLAUDE.md secao 29):
/// - Lei 12.506/2011 - aviso previo proporcional;
/// - TST SDI-1 E-RR-1964-73.2013.5.09.0009 - proporcionalidade so da empresa;
/// - CLT art. 146 par. unico e Sumula 171 do TST - ferias proporcionais;
/// - Lei 8.036/1990 art. 18 - multa de 40% e de 20%;
/// - CLT art. 484-A - acordo: aviso e multa pela metade.
/// </summary>
public class AvisoPrevioTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Org = Guid.CreateVersion7();

    internal static ContratoTrabalho Contrato(
        DateOnly admissao, DateOnly? desligamento = null, MotivoDesligamento? motivo = null)
    {
        var contrato = new ContratoTrabalho(
            Org, Guid.CreateVersion7(), Guid.CreateVersion7(), "1001",
            admissao, 3000m, Guid.CreateVersion7(), Guid.CreateVersion7(), 220, Agora);

        if (desligamento is { } data)
        {
            contrato.Desligar(data, motivo ?? MotivoDesligamento.DispensaSemJustaCausa);
        }

        return contrato;
    }

    // ------------------------------------------------------- anos completos

    [Theory]
    // Admitido em 15/03/2020. O aniversario de 2023 e 15/03.
    [InlineData("2023-03-14", 2)]  // vespera: ainda dois anos
    [InlineData("2023-03-15", 3)]  // no dia: tres
    public void AnosCompletos_ContaAniversarios(string saida, int esperado) =>
        Assert.Equal(esperado, AvisoPrevio.AnosCompletos(
            new DateOnly(2020, 3, 15), DateOnly.Parse(saida)));

    // -------------------------------------------------- proporcionalidade

    [Theory]
    // Lei 12.506/2011: 30 dias base, mais 3 por ano, ate 60 de acrescimo.
    [InlineData(0, 30)]
    [InlineData(1, 33)]
    [InlineData(10, 60)]
    [InlineData(20, 90)]   // 30 + 60: o teto
    [InlineData(30, 90)]   // acima do teto continua 90
    public void EmpregadorDeve_OProporcional(int anos, int esperado)
    {
        var admissao = new DateOnly(2000, 1, 1);
        var saida = admissao.AddYears(anos);

        var a = AvisoPrevio.Apurar(Contrato(admissao), saida, DevedorDoAviso.Empregador);

        Assert.Equal(esperado, a.Dias);
        Assert.True(a.Dias <= AvisoPrevio.MaximoTotal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(30)]
    public void EmpregadoDeve_SEMPRE_TrintaDias(int anos)
    {
        // TST SDI-1: a proporcionalidade so pode ser exigida da EMPRESA.
        // Cobrar do trabalhador 90 dias de aviso seria alteracao prejudicial.
        var admissao = new DateOnly(2000, 1, 1);

        var a = AvisoPrevio.Apurar(
            Contrato(admissao), admissao.AddYears(anos), DevedorDoAviso.Empregado);

        Assert.Equal(30, a.Dias);
        Assert.Equal(0, a.DiasAcrescidos);
    }

    [Fact]
    public void NinguemDeve_ZeroDias() =>
        Assert.Equal(0, AvisoPrevio.Apurar(
            Contrato(new DateOnly(2000, 1, 1)), new DateOnly(2026, 8, 29),
            DevedorDoAviso.Ninguem).Dias);

    [Fact]
    public void AcordoReduzPelaMetade_ArredondandoParaBaixo()
    {
        // 10 anos: 30 + 30 = 60. Metade = 30.
        var admissao = new DateOnly(2016, 1, 1);
        var a = AvisoPrevio.Apurar(
            Contrato(admissao), new DateOnly(2026, 1, 1), DevedorDoAviso.Empregador, reduzido: true);

        Assert.Equal(30, a.Dias);
        Assert.True(a.Reduzido);

        // 1 ano: 33. Metade seria 16,5 - dia de aviso e unidade inteira, e
        // meio dia nao existe: 16.
        var b = AvisoPrevio.Apurar(
            Contrato(admissao), new DateOnly(2017, 1, 1), DevedorDoAviso.Empregador, reduzido: true);

        Assert.Equal(16, b.Dias);
    }
}

/// <summary>Os avos de ferias proporcionais do periodo incompleto.</summary>
public class FeriasProporcionaisTestes
{
    private static ContratoTrabalho Contrato(DateOnly admissao, DateOnly desligamento) =>
        AvisoPrevioTestes.Contrato(admissao, desligamento);

    [Fact]
    public void PeriodoIncompleto_ContaMesAMes()
    {
        // Admitido 15/01/2026, sai 20/07/2026. Periodo aquisitivo comeca em
        // 15/01, entao os meses vao de 15 a 14.
        var a = AvosFeriasProporcionais.Apurar(
            Contrato(new DateOnly(2026, 1, 15), new DateOnly(2026, 7, 20)),
            new DateOnly(2026, 7, 20))!;

        Assert.Equal(new DateOnly(2026, 1, 15), a.InicioPeriodo);

        // 15/01 a 14/02, ... , 15/06 a 14/07 sao seis meses cheios.
        // 15/07 a 20/07 sao 6 dias - nao passa de 14.
        Assert.Equal(6, a.Avos);
        Assert.Equal("6/12", a.Fracao);
        Assert.Equal(7, a.Meses.Count);
        Assert.False(a.Meses[^1].Conta);
    }

    [Theory]
    // "Fracao SUPERIOR a 14 dias" (art. 146, par. unico). 15 conta, 14 nao.
    // Admitido em 01/01; o setimo mes vai de 01/07 em diante.
    [InlineData("2026-07-15", 7)]
    [InlineData("2026-07-14", 6)]
    public void QuinzeDiasContam_QuatorzeNao(string saida, int esperado)
    {
        var data = DateOnly.Parse(saida);

        var a = AvosFeriasProporcionais.Apurar(
            Contrato(new DateOnly(2026, 1, 1), data), data)!;

        Assert.Equal(esperado, a.Avos);
    }

    [Fact]
    public void OsMesesSeguemOPERIODO_NaoOCalendario()
    {
        // Admitido em 20/03: os meses vao de 20 a 19, e nao de 1o a 30.
        var a = AvosFeriasProporcionais.Apurar(
            Contrato(new DateOnly(2026, 3, 20), new DateOnly(2026, 6, 25)),
            new DateOnly(2026, 6, 25))!;

        Assert.Equal(new DateOnly(2026, 3, 20), a.Meses[0].Inicio);
        Assert.Equal(new DateOnly(2026, 4, 19), a.Meses[0].Fim);

        // 20/06 a 25/06 sao 6 dias: nao contam.
        Assert.Equal(3, a.Avos);
    }

    [Fact]
    public void ContratoLongo_SoOPeriodoINCOMPLETOEntra()
    {
        // Admitido em 2020, sai em 2026. Os periodos completos sao ferias
        // VENCIDAS - aqui so entra o pedaco que ficou pela metade.
        var a = AvosFeriasProporcionais.Apurar(
            Contrato(new DateOnly(2020, 1, 10), new DateOnly(2026, 5, 20)),
            new DateOnly(2026, 5, 20))!;

        Assert.Equal(new DateOnly(2026, 1, 10), a.InicioPeriodo);
        Assert.Equal(4, a.Avos);
    }

    [Fact]
    public void SemPeriodoEmAndamento_DevolveNulo() =>
        // Desligado antes da admissao nao existe, mas a data anterior sim.
        Assert.Null(AvosFeriasProporcionais.Apurar(
            AvisoPrevioTestes.Contrato(new DateOnly(2026, 9, 1)), new DateOnly(2026, 8, 1)));

    [Fact]
    public void ODivisorNaoEODoDecimoTerceiro()
    {
        // As duas regras dao o mesmo numero em dias inteiros, mas vem de leis
        // diferentes: 13o e "igual ou superior a 15" (Lei 4.090/1962), ferias
        // sao "superior a 14" (CLT art. 146). Se uma mudar, a outra nao muda
        // junto - e por isso as constantes sao separadas.
        Assert.Equal(14, AvosFeriasProporcionais.DiasMinimosDoMes);
        Assert.Equal(15, PrismaRH.Dominio.DecimoTerceiro.AvosDecimoTerceiro.DiasMinimosDoMes);
    }
}

/// <summary>
/// A matriz de verbas por motivo.
///
/// E a tabela mais perigosa do produto: uma celula errada muda quanto uma
/// pessoa recebe ao perder o emprego. Por isso cada linha e travada.
/// </summary>
public class MatrizVerbasTestes
{
    [Fact]
    public void DispensaSemJustaCausa_GeraTudo()
    {
        var v = MatrizVerbasRescisorias.De(MotivoDesligamento.DispensaSemJustaCausa);

        Assert.True(v.Suportado);
        Assert.Equal(DevedorDoAviso.Empregador, v.DevedorDoAviso);
        Assert.False(v.AvisoPelaMetade);
        Assert.True(v.FeriasProporcionais);
        Assert.Equal(0.40m, v.PercentualMultaFgts);
        Assert.Contains("art. 18", v.Fonte);
    }

    [Fact]
    public void RescisaoIndireta_TemOsMesmosEfeitosDaDispensa()
    {
        // Quem deu causa foi o empregador (art. 483): a diferenca esta em
        // quem deu causa, nao no que e devido.
        var indireta = MatrizVerbasRescisorias.De(MotivoDesligamento.RescisaoIndireta);
        var dispensa = MatrizVerbasRescisorias.De(MotivoDesligamento.DispensaSemJustaCausa);

        Assert.Equal(dispensa.DevedorDoAviso, indireta.DevedorDoAviso);
        Assert.Equal(dispensa.FeriasProporcionais, indireta.FeriasProporcionais);
        Assert.Equal(dispensa.PercentualMultaFgts, indireta.PercentualMultaFgts);

        // Mas a FONTE e diferente: a multa da indireta vem do Manual do FGTS
        // Digital, e nao do texto do art. 18.
        Assert.Contains("FGTS Digital", indireta.Fonte);
    }

    [Fact]
    public void PedidoDeDemissao_QuemAvisaEOEmpregado_ESemMulta()
    {
        var v = MatrizVerbasRescisorias.De(MotivoDesligamento.PedidoDeDemissao);

        Assert.True(v.Suportado);
        Assert.Equal(DevedorDoAviso.Empregado, v.DevedorDoAviso);

        // Sumula 171: proporcionais devidas SALVO justa causa. Pedido de
        // demissao nao e a excecao.
        Assert.True(v.FeriasProporcionais);
        Assert.Equal(0m, v.PercentualMultaFgts);
    }

    [Fact]
    public void JustaCausa_EAUnicaQuePerdeAsFeriasProporcionais()
    {
        var justaCausa = MatrizVerbasRescisorias.De(MotivoDesligamento.DispensaPorJustaCausa);

        Assert.False(justaCausa.FeriasProporcionais);
        Assert.Equal(DevedorDoAviso.Ninguem, justaCausa.DevedorDoAviso);
        Assert.Equal(0m, justaCausa.PercentualMultaFgts);
        Assert.Contains("Sumula 171", justaCausa.Fonte);

        // E e a UNICA entre as suportadas.
        var outras = MatrizVerbasRescisorias.Todas
            .Where(v => v.Suportado && v.Motivo != MotivoDesligamento.DispensaPorJustaCausa);

        Assert.All(outras, v => Assert.True(v.FeriasProporcionais));
    }

    [Fact]
    public void Acordo_EMetadeDoAvisoEMetadeDaMulta()
    {
        var v = MatrizVerbasRescisorias.De(MotivoDesligamento.AcordoEntreAsPartes);

        Assert.True(v.AvisoPelaMetade);
        Assert.Equal(0.20m, v.PercentualMultaFgts);
        Assert.Contains("484-A", v.Fonte);
    }

    [Theory]
    [InlineData(MotivoDesligamento.TerminoDeContratoPorPrazoDeterminado)]
    [InlineData(MotivoDesligamento.FalecimentoDoEmpregado)]
    [InlineData(MotivoDesligamento.Aposentadoria)]
    public void TresMotivos_EstaoBLOQUEADOS_ComARazao(MotivoDesligamento motivo)
    {
        var v = MatrizVerbasRescisorias.De(motivo);

        // Bloqueado e diferente de "gera zero": o produto NAO sabe, e diz.
        Assert.False(v.Suportado);
        Assert.NotNull(v.MotivoDoBloqueio);
        Assert.NotEmpty(v.MotivoDoBloqueio!);
        Assert.Contains("sem fonte oficial", v.Fonte);
    }

    [Fact]
    public void OsOitoMotivos_EstaoNaMatriz()
    {
        // Um motivo novo no enum sem linha na matriz cairia em "desconhecido"
        // em tempo de execucao. Este teste faz isso falhar na compilacao dos
        // testes, que e cedo o bastante.
        foreach (var motivo in Enum.GetValues<MotivoDesligamento>())
        {
            var v = MatrizVerbasRescisorias.De(motivo);

            Assert.Equal(motivo, v.Motivo);
            Assert.NotEqual("Motivo desconhecido pela matriz.", v.MotivoDoBloqueio);
        }

        Assert.Equal(8, MatrizVerbasRescisorias.Todas.Count);
    }

    [Fact]
    public void CincoSuportados_TresBloqueados()
    {
        Assert.Equal(5, MatrizVerbasRescisorias.Todas.Count(v => v.Suportado));
        Assert.Equal(3, MatrizVerbasRescisorias.Todas.Count(v => !v.Suportado));
    }
}

/// <summary>A apuracao completa de uma rescisao.</summary>
public class CalculadoraRescisaoTestes
{
    private static ContratoTrabalho Desligado(MotivoDesligamento motivo) =>
        AvisoPrevioTestes.Contrato(
            new DateOnly(2024, 1, 10), new DateOnly(2026, 5, 20), motivo);

    private static Rescisao Apurar(
        MotivoDesligamento motivo, int feriasVencidas = 30, decimal baseFgts = 10000m) =>
        CalculadoraRescisao.Apurar(
            Desligado(motivo), 3000m, feriasVencidas,
            new ValorBaseFgts(baseFgts, ConhecidoPeloSistema: 8000m));

    private static decimal Valor(Rescisao r, string codigo) =>
        r.Verbas.Single(v => v.Codigo == codigo).Valor;

    [Fact]
    public void DispensaSemJustaCausa_GeraTodasAsVerbas()
    {
        var r = Apurar(MotivoDesligamento.DispensaSemJustaCausa);

        Assert.True(r.Suportado);

        // Salario 3.000, diario 100.
        // Saldo: 01/05 a 20/05 = 20 dias -> 2.000
        Assert.Equal(2000.00m, Valor(r, "SALDO"));

        // Aviso: admitido 10/01/2024, saiu 20/05/2026 -> 2 anos completos.
        // 30 + 6 = 36 dias -> 3.600
        Assert.Equal(36, r.Aviso!.Dias);
        Assert.Equal(3600.00m, Valor(r, "AVISO"));

        // Ferias vencidas 30 dias -> 3.000, mais 1/3 -> 1.000
        Assert.Equal(3000.00m, Valor(r, "FERVEN"));
        Assert.Equal(1000.00m, Valor(r, "FERVEN13"));

        // Proporcionais: periodo comeca 10/01/2026, sai 20/05/2026.
        // 10/01-09/02, 10/02-09/03, 10/03-09/04, 10/04-09/05 = 4 meses cheios;
        // 10/05-20/05 = 11 dias, nao conta. 4 avos.
        Assert.Equal(4, r.FeriasProporcionais!.Avos);
        Assert.Equal(1000.00m, Valor(r, "FERPROP"));   // 3.000 x 4/12
        Assert.Equal(333.33m, Valor(r, "FERPROP13"));

        // Multa: 40% sobre o valor INFORMADO, nao sobre o conhecido.
        Assert.Equal(4000.00m, Valor(r, "MULTAFGTS"));
    }

    [Fact]
    public void PedidoDeDemissao_NaoGeraAvisoAPagarNemMulta()
    {
        var r = Apurar(MotivoDesligamento.PedidoDeDemissao);

        Assert.True(r.Suportado);

        // O aviso e devido PELO empregado: nao vira verba a pagar a ele.
        Assert.Equal(DevedorDoAviso.Empregado, r.Aviso!.Devedor);
        Assert.Equal(30, r.Aviso.Dias);
        Assert.DoesNotContain(r.Verbas, v => v.Codigo == "AVISO");

        Assert.DoesNotContain(r.Verbas, v => v.Codigo == "MULTAFGTS");

        // Mas as ferias continuam devidas (Sumula 171).
        Assert.Contains(r.Verbas, v => v.Codigo == "FERPROP");
    }

    [Fact]
    public void JustaCausa_PerdeProporcionais_MasNaoAsVencidas()
    {
        var r = Apurar(MotivoDesligamento.DispensaPorJustaCausa);

        Assert.Contains(r.Verbas, v => v.Codigo == "SALDO");
        Assert.Contains(r.Verbas, v => v.Codigo == "FERVEN");

        // A excecao da Sumula 171 atinge as PROPORCIONAIS. As vencidas ja
        // eram direito adquirido.
        Assert.DoesNotContain(r.Verbas, v => v.Codigo == "FERPROP");
        Assert.DoesNotContain(r.Verbas, v => v.Codigo == "AVISO");
        Assert.DoesNotContain(r.Verbas, v => v.Codigo == "MULTAFGTS");
    }

    [Fact]
    public void Acordo_PagaMetadeDoAvisoEVintePorCento()
    {
        var r = Apurar(MotivoDesligamento.AcordoEntreAsPartes);

        // 36 dias pela metade: 18 -> 1.800
        Assert.Equal(18, r.Aviso!.Dias);
        Assert.True(r.Aviso.Reduzido);
        Assert.Equal(1800.00m, Valor(r, "AVISO"));

        // 20% de 10.000
        Assert.Equal(2000.00m, Valor(r, "MULTAFGTS"));
    }

    [Theory]
    [InlineData(MotivoDesligamento.TerminoDeContratoPorPrazoDeterminado)]
    [InlineData(MotivoDesligamento.FalecimentoDoEmpregado)]
    [InlineData(MotivoDesligamento.Aposentadoria)]
    public void MotivoBloqueado_NaoGeraVerbaAlguma_MasExplicaOContexto(MotivoDesligamento motivo)
    {
        var r = Apurar(motivo);

        Assert.False(r.Suportado);
        Assert.Empty(r.Verbas);
        Assert.Equal(0m, r.Total);
        Assert.NotNull(r.MotivoDoBloqueio);

        // O contexto vem mesmo assim: quem le precisa entender o que falta,
        // nao so receber um erro seco.
        Assert.NotNull(r.FeriasProporcionais);
        Assert.NotNull(r.Avos13);
        Assert.Equal(30, r.DiasFeriasVencidas);
    }

    [Fact]
    public void ODecimoTerceiro_APARECE_EmAvos_MasNaoViraDinheiro()
    {
        var r = Apurar(MotivoDesligamento.DispensaSemJustaCausa);

        // Admitido 10/01/2024, sai 20/05/2026: janeiro tem 20 dias e conta;
        // maio tem 20 e conta. Janeiro a maio = 5 avos.
        Assert.Equal(5, r.Avos13!.Avos);

        // Mas NAO ha verba de 13o: a Fase 4F esta bloqueada por contradicao
        // entre fontes sobre quando INSS e IRRF incidem, e a rescisao herda a
        // duvida. Converter em reais aqui contornaria aquela pendencia por
        // outro caminho.
        Assert.DoesNotContain(r.Verbas, v => v.Codigo.Contains("13", StringComparison.Ordinal)
            && !v.Codigo.StartsWith("FER", StringComparison.Ordinal));
    }

    [Fact]
    public void MultaIncideSobreOINFORMADO_NaoSobreOConhecido()
    {
        // E a decisao central sobre o FGTS: o produto conhece 8.000 de
        // depositos, mas a conta vinculada tem 10.000 com juros e correcao.
        // Calcular sobre o conhecido daria 3.200 - menos que o devido, e com
        // cara de exato.
        var r = Apurar(MotivoDesligamento.DispensaSemJustaCausa, baseFgts: 10000m);

        Assert.Equal(4000.00m, Valor(r, "MULTAFGTS"));
        Assert.NotEqual(3200.00m, Valor(r, "MULTAFGTS"));
    }

    [Fact]
    public void ValorAbaixoDoConhecido_ELevantadoComoAviso()
    {
        var abaixo = new ValorBaseFgts(Informado: 5000m, ConhecidoPeloSistema: 8000m);
        var acima = new ValorBaseFgts(Informado: 10000m, ConhecidoPeloSistema: 8000m);

        // Aviso, e nao recusa: o sistema nao sabe o saldo real e nao pode
        // afirmar que o analista errou.
        Assert.True(abaixo.AbaixoDoConhecido);
        Assert.False(acima.AbaixoDoConhecido);
    }

    [Fact]
    public void SemValorBaseInformado_NaoHaMulta()
    {
        var r = CalculadoraRescisao.Apurar(
            Desligado(MotivoDesligamento.DispensaSemJustaCausa), 3000m, 30, valorBaseFgts: null);

        // Melhor nenhuma linha do que uma linha calculada sobre um numero que
        // o produto nao tem.
        Assert.DoesNotContain(r.Verbas, v => v.Codigo == "MULTAFGTS");
        Assert.Contains(r.Verbas, v => v.Codigo == "SALDO");
    }

    [Fact]
    public void ContratoAtivo_NaoTemRescisao()
    {
        var ativo = AvisoPrevioTestes.Contrato(new DateOnly(2024, 1, 10));

        Assert.Throws<InvalidOperationException>(() =>
            CalculadoraRescisao.Apurar(ativo, 3000m, 0, null));
    }

    [Fact]
    public void MemoriaExplicaCadaVerba()
    {
        var r = Apurar(MotivoDesligamento.DispensaSemJustaCausa);

        Assert.All(r.Verbas, v =>
        {
            Assert.NotEmpty(v.Passos);
            Assert.NotEmpty(v.Referencia);
        });

        var aviso = r.Verbas.Single(v => v.Codigo == "AVISO");
        Assert.Contains(aviso.Passos, p => p.Expressao == "30 + 2 x 3");
    }

    [Fact]
    public void TotalEASomaDasVerbas()
    {
        var r = Apurar(MotivoDesligamento.DispensaSemJustaCausa);

        // 2.000 + 3.600 + 3.000 + 1.000 + 1.000 + 333,33 + 4.000
        Assert.Equal(14933.33m, r.Total);
    }
}
