using PrismaRH.Dominio.Analises;
using PrismaRH.Dominio.Analises.Regras;
using PrismaRH.Dominio.Contratos;
using PrismaRH.Dominio.Folha;

namespace PrismaRH.Testes.Dominio;

/// <summary>
/// O motor de analises e as seis regras oficiais.
///
/// Tudo em memoria: as regras sao funcoes puras sobre
/// <see cref="ContextoAnalise"/>, e essa e a razao pratica de o retrato existir.
/// </summary>
public class MotorAnalisesTestes
{
    private static readonly Guid Org = Guid.CreateVersion7();
    private static readonly Guid Usuario = Guid.CreateVersion7();
    private static readonly DateTimeOffset Agora = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly Competencia Agosto = new(2026, 8);

    // ------------------------------------------------------------- construcao

    private static ContratoAnalisado Contrato(
        Guid id,
        string matricula,
        string nome,
        DateOnly? admissao = null,
        DateOnly? desligamento = null) =>
        new(id,
            Guid.CreateVersion7(),
            matricula,
            nome,
            admissao ?? new DateOnly(2020, 1, 1),
            desligamento,
            desligamento is null ? SituacaoContrato.Ativo : SituacaoContrato.Desligado);

    private static HoleriteAnalisado Holerite(
        ContratoAnalisado contrato,
        decimal salario = 3_000m,
        decimal proventos = 3_000m,
        decimal descontos = 300m,
        IReadOnlyList<LancamentoAnalisado>? lancamentos = null) =>
        new(Guid.CreateVersion7(),
            contrato.IdFuncionario,
            contrato.IdContrato,
            contrato.Matricula,
            contrato.NomeFuncionario,
            salario,
            proventos,
            descontos,
            proventos - descontos,
            lancamentos ?? []);

    private static ContextoAnalise Contexto(
        IReadOnlyList<HoleriteAnalisado> holerites,
        IReadOnlyList<ContratoAnalisado> contratos,
        TipoFolha tipo = TipoFolha.Mensal,
        IReadOnlyDictionary<Guid, decimal>? anterior = null) =>
        new(Guid.CreateVersion7(),
            Agosto,
            tipo,
            SituacaoFolha.Calculada,
            holerites,
            contratos,
            anterior ?? new Dictionary<Guid, decimal>());

    private static ExecucaoAnalise Rodar(
        ContextoAnalise contexto,
        IReadOnlyDictionary<CodigoRegra, RegraAnalise>? configuracoes = null) =>
        MotorAnalises.Executar(
            Org, contexto, configuracoes ?? new Dictionary<CodigoRegra, RegraAnalise>(),
            versaoCalculoDaFolha: 1, Usuario, Agora);

    private static RegraAnalise Configuracao(
        CodigoRegra codigo,
        bool ativa = true,
        Severidade? severidade = null,
        params (string Chave, string Valor)[] parametros)
    {
        var regra = CatalogoRegras.De(codigo)!;
        var configuracao = new RegraAnalise(Org, codigo, Agora);

        var (valores, erros) = ValoresParametros.Interpretar(
            regra.Parametros,
            parametros.ToDictionary(p => p.Chave, p => (string?)p.Valor));

        Assert.Empty(erros);

        configuracao.Configurar(
            ativa, severidade ?? regra.SeveridadePadrao, valores, Usuario, Agora);

        return configuracao;
    }

    // ------------------------------------------------------------- o catalogo

    [Fact]
    public void OCatalogoNaoTemCodigoRepetidoNemCodigoDeFora()
    {
        // O enum e o catalogo tem que dizer a mesma coisa. Uma regra no enum
        // sem implementacao apareceria na tela e nunca rodaria; uma
        // implementacao fora do enum nunca seria configuravel.
        var doCatalogo = CatalogoRegras.Todas.Select(r => r.Codigo).ToList();
        var doEnum = Enum.GetValues<CodigoRegra>();

        Assert.Equal(doCatalogo.Count, doCatalogo.Distinct().Count());
        Assert.Equal(doEnum.Length, doCatalogo.Count);
        Assert.All(doEnum, c => Assert.True(CatalogoRegras.Conhece(c)));
    }

    [Fact]
    public void TodaRegraTemVersaoNomeEExplicacao()
    {
        Assert.All(CatalogoRegras.Todas, regra =>
        {
            Assert.True(regra.Versao >= 1);
            Assert.False(string.IsNullOrWhiteSpace(regra.Nome));
            Assert.False(string.IsNullOrWhiteSpace(regra.Explicacao));

            // Chave de parametro repetida faria uma sobrescrever a outra em
            // silencio.
            var chaves = regra.Parametros.Select(p => p.Chave).ToList();
            Assert.Equal(chaves.Count, chaves.Distinct(StringComparer.OrdinalIgnoreCase).Count());

            // Faixa invertida deixaria a regra impossivel de configurar.
            Assert.All(regra.Parametros, p =>
            {
                Assert.True(p.Minimo <= p.Maximo);
                Assert.InRange(p.Padrao, p.Minimo, p.Maximo);
            });
        });
    }

    // --------------------------------------------------- desligado na folha

    [Fact]
    public void DesligadoAntesDaCompetencia_ComHolerite_EhAcusado()
    {
        var saiu = Contrato(Guid.CreateVersion7(), "001", "Quem Saiu",
            desligamento: new DateOnly(2026, 7, 20));

        var execucao = Rodar(Contexto([Holerite(saiu)], [saiu]));

        var achado = Assert.Single(
            execucao.Resultados,
            r => r.Codigo == CodigoRegra.DesligadoNaFolha);

        Assert.Equal(Severidade.Alta, achado.Severidade);
        Assert.Equal("001", achado.Matricula);
        Assert.Contains("20/07/2026", achado.Descricao, StringComparison.Ordinal);
    }

    [Fact]
    public void DesligadoDENTRODaCompetencia_NaoEhAcusado()
    {
        // Quem saiu no dia 15 trabalhou ate o dia 15: o holerite mensal dele e
        // o comportamento correto, e acusa-lo seria alarme falso.
        var saiu = Contrato(Guid.CreateVersion7(), "002", "Saiu No Meio",
            desligamento: new DateOnly(2026, 8, 15));

        var execucao = Rodar(Contexto([Holerite(saiu)], [saiu]));

        Assert.DoesNotContain(
            execucao.Resultados, r => r.Codigo == CodigoRegra.DesligadoNaFolha);
    }

    [Theory]
    [InlineData(TipoFolha.Rescisao)]
    [InlineData(TipoFolha.Ferias)]
    [InlineData(TipoFolha.DecimoTerceiro)]
    public void DesligadoEmFolhaQueNaoEMensal_NaoEhAcusado(TipoFolha tipo)
    {
        // Rescisao, ferias e 13o DEVEM conter quem saiu - e para isso que
        // existem. Acusa-las transformaria a regra em ruido.
        var saiu = Contrato(Guid.CreateVersion7(), "003", "Rescindido",
            desligamento: new DateOnly(2026, 7, 20));

        var execucao = Rodar(Contexto([Holerite(saiu)], [saiu], tipo));

        Assert.DoesNotContain(
            execucao.Resultados, r => r.Codigo == CodigoRegra.DesligadoNaFolha);
    }

    // ------------------------------------------------------ ausente da folha

    [Fact]
    public void ContratoVigenteSemHolerite_EhAcusado()
    {
        var presente = Contrato(Guid.CreateVersion7(), "010", "Esta Na Folha");
        var esquecido = Contrato(Guid.CreateVersion7(), "011", "Ficou De Fora");

        var execucao = Rodar(Contexto([Holerite(presente)], [presente, esquecido]));

        var achado = Assert.Single(
            execucao.Resultados,
            r => r.Codigo == CodigoRegra.AusenteDaFolha);

        Assert.Equal("011", achado.Matricula);
        Assert.Null(achado.IdFolhaFuncionario);
    }

    [Fact]
    public void AdmitidoNoULTIMODiaDaCompetencia_ContaComoVigente()
    {
        // Um dia basta: quem foi admitido no dia 31 tem direito a um dia de
        // salario. Exigir o mes inteiro deixaria de fora exatamente as
        // admissoes, que sao onde a folha mais erra.
        var novo = Contrato(Guid.CreateVersion7(), "012", "Entrou No Fim",
            admissao: new DateOnly(2026, 8, 31));

        var execucao = Rodar(Contexto([], [novo]));

        Assert.Contains(execucao.Resultados, r => r.Codigo == CodigoRegra.AusenteDaFolha);
    }

    [Fact]
    public void AdmitidoDEPOISDaCompetencia_NaoEhAcusado()
    {
        var futuro = Contrato(Guid.CreateVersion7(), "013", "Entra Em Setembro",
            admissao: new DateOnly(2026, 9, 1));

        var execucao = Rodar(Contexto([], [futuro]));

        Assert.DoesNotContain(execucao.Resultados, r => r.Codigo == CodigoRegra.AusenteDaFolha);
    }

    [Fact]
    public void DesligadoAntesDaCompetencia_SemHolerite_NaoEhAcusadoDeAusencia()
    {
        var saiu = Contrato(Guid.CreateVersion7(), "014", "Ja Foi",
            desligamento: new DateOnly(2026, 7, 31));

        var execucao = Rodar(Contexto([], [saiu]));

        // As duas regras precisam concordar: quem saiu nao deveria estar na
        // folha, entao a ausencia dele nao e problema.
        Assert.Empty(execucao.Resultados);
    }

    // ------------------------------------------------------ liquido negativo

    [Fact]
    public void LiquidoNegativo_EhAcusadoComEsperadoZero()
    {
        var contrato = Contrato(Guid.CreateVersion7(), "020", "Deve Para A Empresa");
        var holerite = Holerite(contrato, proventos: 1_000m, descontos: 1_200m);

        var execucao = Rodar(Contexto([holerite], [contrato]));

        var achado = Assert.Single(
            execucao.Resultados,
            r => r.Codigo == CodigoRegra.LiquidoNegativo);

        Assert.Equal(0m, achado.ValorEsperado);
        Assert.Equal(-200m, achado.ValorEncontrado);
        Assert.Equal(-200m, achado.Diferenca);
    }

    [Fact]
    public void ToleranciaConfigurada_SegurAOCentavoDeArredondamento()
    {
        var contrato = Contrato(Guid.CreateVersion7(), "021", "Um Centavo");
        var holerite = Holerite(contrato, proventos: 1_000m, descontos: 1_000.01m);

        var comTolerancia = new Dictionary<CodigoRegra, RegraAnalise>
        {
            [CodigoRegra.LiquidoNegativo] = Configuracao(
                CodigoRegra.LiquidoNegativo,
                parametros: (LiquidoNegativoRegra.ParametroTolerancia, "0.05")),
        };

        Assert.Contains(
            Rodar(Contexto([holerite], [contrato])).Resultados,
            r => r.Codigo == CodigoRegra.LiquidoNegativo);

        Assert.DoesNotContain(
            Rodar(Contexto([holerite], [contrato]), comTolerancia).Resultados,
            r => r.Codigo == CodigoRegra.LiquidoNegativo);
    }

    // ---------------------------------------------------- rubrica duplicada

    [Fact]
    public void MesmaRubricaLancadaAMaoDuasVezes_EhAcusada()
    {
        var contrato = Contrato(Guid.CreateVersion7(), "030", "Digitou Duas Vezes");

        var holerite = Holerite(contrato, lancamentos:
        [
            new("HE50", "Hora extra 50%", TipoRubrica.Provento, OrigemLancamento.Manual, 120m),
            new("HE50", "Hora extra 50%", TipoRubrica.Provento, OrigemLancamento.Manual, 120m),
        ]);

        var achado = Assert.Single(
            Rodar(Contexto([holerite], [contrato])).Resultados,
            r => r.Codigo == CodigoRegra.RubricaDuplicada);

        Assert.Equal(240m, achado.ValorEncontrado);
        Assert.Contains("HE50", achado.Descricao, StringComparison.Ordinal);
    }

    [Fact]
    public void RubricaRepetidaPeloCALCULO_NaoEhAcusada()
    {
        // O motor repete rubrica de proposito: duas concessoes de ferias no
        // mesmo mes, as parcelas de 13o. Acusar o que o proprio sistema produz
        // seria acusar o comportamento correto.
        var contrato = Contrato(Guid.CreateVersion7(), "031", "Duas Ferias");

        var holerite = Holerite(contrato, lancamentos:
        [
            new("FER", "Ferias", TipoRubrica.Provento, OrigemLancamento.Calculado, 1_000m),
            new("FER", "Ferias", TipoRubrica.Provento, OrigemLancamento.Calculado, 800m),
        ]);

        Assert.DoesNotContain(
            Rodar(Contexto([holerite], [contrato])).Resultados,
            r => r.Codigo == CodigoRegra.RubricaDuplicada);
    }

    // ------------------------------------------------ desconto acima do limite

    [Fact]
    public void DescontoAcimaDoPadraoDe70Porcento_EhAcusado()
    {
        var contrato = Contrato(Guid.CreateVersion7(), "040", "Desconto Alto");
        var holerite = Holerite(contrato, proventos: 1_000m, descontos: 800m);

        var achado = Assert.Single(
            Rodar(Contexto([holerite], [contrato])).Resultados,
            r => r.Codigo == CodigoRegra.DescontoAcimaDoLimite);

        Assert.Equal(700m, achado.ValorEsperado);
        Assert.Equal(800m, achado.ValorEncontrado);
        Assert.Equal(100m, achado.Diferenca);
    }

    [Fact]
    public void ExatamenteNoLimite_NaoEhAcusado()
    {
        var contrato = Contrato(Guid.CreateVersion7(), "041", "No Limite");
        var holerite = Holerite(contrato, proventos: 1_000m, descontos: 700m);

        Assert.DoesNotContain(
            Rodar(Contexto([holerite], [contrato])).Resultados,
            r => r.Codigo == CodigoRegra.DescontoAcimaDoLimite);
    }

    [Fact]
    public void SemProventos_NaoViraDivisaoPorZero()
    {
        var contrato = Contrato(Guid.CreateVersion7(), "042", "Zero Provento");
        var holerite = Holerite(contrato, proventos: 0m, descontos: 0m);

        var execucao = Rodar(Contexto([holerite], [contrato]));

        Assert.DoesNotContain(
            execucao.Resultados, r => r.Codigo == CodigoRegra.DescontoAcimaDoLimite);
    }

    [Fact]
    public void PercentualConfigurado_MudaOQueEhAcusado()
    {
        var contrato = Contrato(Guid.CreateVersion7(), "043", "Meio A Meio");
        var holerite = Holerite(contrato, proventos: 1_000m, descontos: 500m);

        var apertado = new Dictionary<CodigoRegra, RegraAnalise>
        {
            [CodigoRegra.DescontoAcimaDoLimite] = Configuracao(
                CodigoRegra.DescontoAcimaDoLimite,
                parametros: (DescontoAcimaDoLimiteRegra.ParametroPercentual, "40")),
        };

        Assert.DoesNotContain(
            Rodar(Contexto([holerite], [contrato])).Resultados,
            r => r.Codigo == CodigoRegra.DescontoAcimaDoLimite);

        Assert.Contains(
            Rodar(Contexto([holerite], [contrato]), apertado).Resultados,
            r => r.Codigo == CodigoRegra.DescontoAcimaDoLimite);
    }

    // -------------------------------------------------------- variacao salarial

    [Fact]
    public void SalarioQueDOBRA_EhAcusadoComOsDoisValores()
    {
        var contrato = Contrato(Guid.CreateVersion7(), "050", "Erro De Digitacao");
        var holerite = Holerite(contrato, salario: 7_000m);

        var anterior = new Dictionary<Guid, decimal> { [contrato.IdContrato] = 3_500m };

        var achado = Assert.Single(
            Rodar(Contexto([holerite], [contrato], anterior: anterior)).Resultados,
            r => r.Codigo == CodigoRegra.VariacaoSalarial);

        Assert.Equal(3_500m, achado.ValorEsperado);
        Assert.Equal(7_000m, achado.ValorEncontrado);
        Assert.Equal(3_500m, achado.Diferenca);
        Assert.Contains("subiu", achado.Descricao, StringComparison.Ordinal);
    }

    [Fact]
    public void QuedaAlemDaTolerancia_TambemEhAcusada()
    {
        // Para baixo tambem: o salario que caiu pela metade e tao suspeito
        // quanto o que dobrou.
        var contrato = Contrato(Guid.CreateVersion7(), "051", "Caiu");
        var holerite = Holerite(contrato, salario: 1_500m);

        var anterior = new Dictionary<Guid, decimal> { [contrato.IdContrato] = 3_000m };

        var achado = Assert.Single(
            Rodar(Contexto([holerite], [contrato], anterior: anterior)).Resultados,
            r => r.Codigo == CodigoRegra.VariacaoSalarial);

        Assert.Contains("caiu", achado.Descricao, StringComparison.Ordinal);
    }

    [Fact]
    public void SemFolhaAnterior_NaoAcusaAdmissaoNova()
    {
        // Tratar a ausencia como zero produziria "variacao de 100%" em cada
        // admissao - o oposto de util, justo no mes com mais gente nova.
        var contrato = Contrato(Guid.CreateVersion7(), "052", "Admitido Agora");

        Assert.DoesNotContain(
            Rodar(Contexto([Holerite(contrato)], [contrato])).Resultados,
            r => r.Codigo == CodigoRegra.VariacaoSalarial);
    }

    [Fact]
    public void ReajusteDentroDaTolerancia_NaoEhAcusado()
    {
        var contrato = Contrato(Guid.CreateVersion7(), "053", "Reajuste Normal");
        var holerite = Holerite(contrato, salario: 3_300m);

        var anterior = new Dictionary<Guid, decimal> { [contrato.IdContrato] = 3_000m };

        Assert.DoesNotContain(
            Rodar(Contexto([holerite], [contrato], anterior: anterior)).Resultados,
            r => r.Codigo == CodigoRegra.VariacaoSalarial);
    }

    // ------------------------------------------------------------- o motor

    [Fact]
    public void RegraDESLIGADA_NaoRodaENaoContaComoExecutada()
    {
        var saiu = Contrato(Guid.CreateVersion7(), "060", "Desligado",
            desligamento: new DateOnly(2026, 7, 20));

        var desligada = new Dictionary<CodigoRegra, RegraAnalise>
        {
            [CodigoRegra.DesligadoNaFolha] = Configuracao(CodigoRegra.DesligadoNaFolha, ativa: false),
        };

        var execucao = Rodar(Contexto([Holerite(saiu)], [saiu]), desligada);

        Assert.DoesNotContain(
            execucao.Resultados, r => r.Codigo == CodigoRegra.DesligadoNaFolha);

        Assert.Equal(CatalogoRegras.Todas.Count - 1, execucao.RegrasExecutadas);
    }

    [Fact]
    public void SemConfiguracaoNENHUMA_TodasAsRegrasRodam()
    {
        // Secure by default: organizacao nova nasce conferida. So rodar o que
        // foi configurado deixaria uma organizacao nova sem conferencia alguma.
        var execucao = Rodar(Contexto([], []));

        Assert.Equal(CatalogoRegras.Todas.Count, execucao.RegrasExecutadas);
    }

    [Fact]
    public void SeveridadeConfigurada_EhACONGELADANoResultado()
    {
        var saiu = Contrato(Guid.CreateVersion7(), "061", "Desligado",
            desligamento: new DateOnly(2026, 7, 20));

        var rebaixada = new Dictionary<CodigoRegra, RegraAnalise>
        {
            [CodigoRegra.DesligadoNaFolha] = Configuracao(
                CodigoRegra.DesligadoNaFolha, severidade: Severidade.Baixa),
        };

        var execucao = Rodar(Contexto([Holerite(saiu)], [saiu]), rebaixada);

        var achado = Assert.Single(
            execucao.Resultados,
            r => r.Codigo == CodigoRegra.DesligadoNaFolha);

        // Congelada no resultado: quando alguem mudar a severidade de volta, o
        // que ja foi gravado continua dizendo o que dizia.
        Assert.Equal(Severidade.Baixa, achado.Severidade);
        Assert.Equal(1, execucao.ResultadosBaixos);
        Assert.Equal(0, execucao.ResultadosAltos);
    }

    [Fact]
    public void AVersaoDaRegraEhCONGELADANoResultado()
    {
        var saiu = Contrato(Guid.CreateVersion7(), "062", "Desligado",
            desligamento: new DateOnly(2026, 7, 20));

        var achado = Assert.Single(
            Rodar(Contexto([Holerite(saiu)], [saiu])).Resultados,
            r => r.Codigo == CodigoRegra.DesligadoNaFolha);

        Assert.Equal(
            CatalogoRegras.De(CodigoRegra.DesligadoNaFolha)!.Versao, achado.VersaoRegra);
    }

    /// <summary>
    /// Execucao reproduzivel - o criterio de aceite da fase.
    ///
    /// Mesmo retrato, mesma configuracao, mesmos achados na mesma ordem.
    /// </summary>
    [Fact]
    public void MesmoRetrato_ProduzOMESMORESULTADO()
    {
        var a = Contrato(Guid.CreateVersion7(), "070", "Alguem",
            desligamento: new DateOnly(2026, 7, 1));
        var b = Contrato(Guid.CreateVersion7(), "071", "Ausente");

        var contexto = Contexto(
            [Holerite(a, proventos: 1_000m, descontos: 1_500m)], [a, b]);

        var primeira = Rodar(contexto);
        var segunda = Rodar(contexto);

        Assert.Equal(primeira.TotalResultados, segunda.TotalResultados);
        Assert.Equal(
            primeira.Resultados.Select(r => (r.Codigo, r.Matricula, r.Descricao)),
            segunda.Resultados.Select(r => (r.Codigo, r.Matricula, r.Descricao)));
    }

    [Fact]
    public void OsContadoresDaExecucaoBatemComOsResultados()
    {
        var a = Contrato(Guid.CreateVersion7(), "080", "Desligado",
            desligamento: new DateOnly(2026, 7, 1));
        var b = Contrato(Guid.CreateVersion7(), "081", "Ausente");

        var execucao = Rodar(Contexto(
            [Holerite(a, proventos: 1_000m, descontos: 900m)], [a, b]));

        Assert.Equal(execucao.Resultados.Count, execucao.TotalResultados);
        Assert.Equal(
            execucao.TotalResultados,
            execucao.ResultadosAltos + execucao.ResultadosMedios + execucao.ResultadosBaixos);
    }

    /// <summary>
    /// Uma regra que estoura vira achado, e nao derruba as outras.
    ///
    /// Regra e codigo do sistema, e codigo do sistema tem defeito. Deixar a
    /// excecao subir transformaria um defeito numa regra em "a folha nao pode
    /// ser analisada" - indisponibilidade desproporcional ao problema.
    ///
    /// O catalogo e fechado, entao nao ha caminho publico para injetar uma
    /// regra defeituosa. Este teste chama `MotorAnalises.Rodar` direto, que e
    /// `internal` exatamente para isto: defesa sem teste e hipotese.
    /// </summary>
    [Fact]
    public void RegraQueLANCA_ViraAchadoEmVezDeExcecao()
    {
        var achados = MotorAnalises.Rodar(
            new RegraDefeituosa(), Contexto([], []), ValoresParametros.Padrao([]));

        var achado = Assert.Single(achados);

        Assert.Contains("falhou", achado.Descricao, StringComparison.Ordinal);
        Assert.Contains("As demais regras", achado.Descricao, StringComparison.Ordinal);

        // O nome do tipo da excecao vai para o contexto tecnico - e a mensagem
        // dela NAO vai. Mensagem de excecao carrega caminho de arquivo, nome de
        // coluna e as vezes o proprio dado (`CLAUDE.md secao 24.16`).
        Assert.Equal("falha=InvalidOperationException", achado.Contexto);
        Assert.DoesNotContain("segredo", achado.Descricao, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("segredo", achado.Contexto, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ARegraDefeituosaSOAFETAELAMESMA()
    {
        // A folha inteira continua analisavel: as seis regras do catalogo
        // rodam, e nenhuma delas e a defeituosa.
        var execucao = Rodar(Contexto([], []));

        Assert.Equal(CatalogoRegras.Todas.Count, execucao.RegrasExecutadas);
    }

    /// <summary>
    /// Uma regra que so serve para falhar.
    ///
    /// Estoura DURANTE a enumeracao, e nao na chamada - que e o caso real: as
    /// regras usam `yield`, entao o corpo delas so roda quando alguem percorre
    /// o resultado. Um `try` que nao materializasse a lista nao pegaria nada.
    /// </summary>
    private sealed class RegraDefeituosa : IRegraAnalise
    {
        public CodigoRegra Codigo => CodigoRegra.LiquidoNegativo;
        public int Versao => 99;
        public CategoriaRegra Categoria => CategoriaRegra.Valores;
        public Severidade SeveridadePadrao => Severidade.Alta;
        public string Nome => "Regra defeituosa";
        public string Explicacao => "Existe so para o teste da defesa.";
        public IReadOnlyList<DefinicaoParametro> Parametros => [];

        public IEnumerable<Achado> Executar(ContextoAnalise contexto, ValoresParametros parametros)
        {
            yield return new Achado("este nunca chega");

            throw new InvalidOperationException("segredo que nao pode vazar para a tela");
        }
    }
}
