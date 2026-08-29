using PrismaRH.Dominio.Contratos;

using PrismaRH.Dominio.DecimoTerceiro;
using PrismaRH.Dominio.Ferias;
using PrismaRH.Dominio.Rescisao;
using PrismaRH.Dominio.Parametros;

namespace PrismaRH.Dominio.Folha;

/// <summary>
/// Um processamento de folha: uma empresa, uma competencia.
///
/// E o agregado. Ninguem cria FolhaFuncionario nem LancamentoFolha por fora -
/// os construtores deles sao internal pelo mesmo motivo que os de
/// VigenciaContrato: um holerite solto, criado sem passar pelas regras daqui,
/// entraria nos totais sem nunca ter sido calculado.
/// </summary>
public sealed class FolhaPagamento
{
    private readonly List<FolhaFuncionario> _funcionarios = [];

    private FolhaPagamento()
    {
    }

    public FolhaPagamento(
        Guid idOrganizacao,
        Guid idEmpresa,
        Competencia competencia,
        DateTimeOffset criadoEm,
        TipoFolha tipo = TipoFolha.Mensal)
    {
        if (!Enum.IsDefined(tipo))
        {
            throw new ArgumentException("Tipo de folha desconhecido.", nameof(tipo));
        }

        if (idOrganizacao == Guid.Empty)
        {
            throw new ArgumentException("Folha precisa pertencer a uma organizacao.", nameof(idOrganizacao));
        }

        if (idEmpresa == Guid.Empty)
        {
            throw new ArgumentException("Folha precisa de uma empresa.", nameof(idEmpresa));
        }

        Id = Guid.CreateVersion7();
        IdOrganizacao = idOrganizacao;
        IdEmpresa = idEmpresa;
        Competencia = competencia;
        Tipo = tipo;
        Situacao = SituacaoFolha.Rascunho;
        CriadoEm = criadoEm;
    }

    public Guid Id { get; private set; }
    public Guid IdOrganizacao { get; private set; }
    public Guid IdEmpresa { get; private set; }
    public Competencia Competencia { get; private set; }

    /// <summary>
    /// Que processamento esta folha representa. Imutavel: uma folha mensal
    /// nao vira folha de ferias - abre-se outra.
    /// </summary>
    public TipoFolha Tipo { get; private set; }
    public SituacaoFolha Situacao { get; private set; }

    /// <summary>Quantas vezes esta folha foi calculada. Reprocessar e visivel, nao silencioso.</summary>
    public int VersaoCalculo { get; private set; }

    public DateTimeOffset CriadoEm { get; private set; }
    public DateTimeOffset? CalculadaEm { get; private set; }
    public DateTimeOffset? FechadaEm { get; private set; }

    public decimal TotalProventos { get; private set; }
    public decimal TotalDescontos { get; private set; }
    public decimal TotalLiquido { get; private set; }

    public IReadOnlyList<FolhaFuncionario> Funcionarios => _funcionarios;

    public bool EstaFechada => Situacao == SituacaoFolha.Fechada;

    /// <summary>
    /// Inclui os contratos elegiveis, calcula cada um e atualiza os totais.
    ///
    /// Recebe os contratos ja carregados de proposito: o CLAUDE.md secao 10
    /// proibe o motor de acessar banco durante o calculo. Quem chama e
    /// responsavel por trazer os contratos COM as vigencias.
    ///
    /// Chamar de novo reprocessa. Os lancamentos manuais permanecem; os
    /// calculados sao refeitos do zero.
    ///
    /// O catalogo serve para reaplicar a incidencia ATUAL nos lancamentos
    /// manuais. Numa folha aberta, recalcular significa aplicar as regras de
    /// agora, e incidencia e regra do catalogo - o que e do analista sao a
    /// rubrica e o valor, que continuam intocados. Folha fechada nao chega
    /// aqui: GarantirAberta recusa antes.
    /// </summary>
    public void Calcular(
        IEnumerable<ContratoTrabalho> contratosDaEmpresa,
        Rubrica rubricaSalario,
        IEnumerable<Rubrica> catalogoRubricas,
        ParametrosEncargos encargos,
        IReadOnlyDictionary<Guid, int> dependentesPorFuncionario,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(contratosDaEmpresa);
        ArgumentNullException.ThrowIfNull(rubricaSalario);
        ArgumentNullException.ThrowIfNull(catalogoRubricas);
        ArgumentNullException.ThrowIfNull(encargos);
        ArgumentNullException.ThrowIfNull(dependentesPorFuncionario);

        GarantirAberta("calcular");

        if (Tipo != TipoFolha.Mensal)
        {
            throw new InvalidOperationException(
                "Esta folha nao e mensal: use o calculo do tipo dela.");
        }

        if (rubricaSalario.Estrategia != EstrategiaRubrica.SalarioBaseProporcional)
        {
            throw new ArgumentException(
                $"A rubrica {rubricaSalario.Codigo} nao e a rubrica de salario-base.", nameof(rubricaSalario));
        }

        var catalogo = catalogoRubricas.ToDictionary(r => r.Id);

        var elegiveis = contratosDaEmpresa
            .Where(c => c.IdEmpresa == IdEmpresa && MotorCalculoFolha.Elegivel(c, Competencia))
            .ToDictionary(c => c.Id);

        // Quem deixou de ser elegivel sai da folha. Acontece quando um
        // desligamento anterior a competencia e registrado depois da abertura:
        // manter a pessoa produziria pagamento para quem nao trabalhou.
        _funcionarios.RemoveAll(f => !elegiveis.ContainsKey(f.IdContrato));

        foreach (var contrato in elegiveis.Values)
        {
            var holerite = _funcionarios.SingleOrDefault(f => f.IdContrato == contrato.Id);

            if (holerite is null)
            {
                holerite = new FolhaFuncionario(IdOrganizacao, Id, contrato.Id, contrato.IdFuncionario);
                _funcionarios.Add(holerite);
            }

            var apuracao = MotorCalculoFolha.Apurar(contrato, Competencia)
                ?? throw new InvalidOperationException(
                    $"Contrato {contrato.Matricula} passou na elegibilidade mas nao apurou: estado inconsistente.");

            holerite.AtualizarIncidenciasManuais(catalogo);

            // Funcionario sem dependente nao precisa estar no dicionario:
            // ausencia e zero, e exigir a chave obrigaria quem chama a montar
            // uma entrada para cada pessoa da empresa.
            dependentesPorFuncionario.TryGetValue(contrato.IdFuncionario, out var dependentes);

            holerite.AplicarCalculo(apuracao, rubricaSalario, encargos, dependentes);
        }

        VersaoCalculo++;
        CalculadaEm = agora;
        Situacao = SituacaoFolha.Calculada;

        RecalcularTotais();
    }

    /// <summary>
    /// Calcula a folha de FERIAS: paga as concessoes que comecam nesta
    /// competencia.
    ///
    /// O criterio e a DATA DE INICIO DO GOZO, e nao o periodo aquisitivo: e
    /// quando a pessoa sai de ferias que o pagamento e devido (CLT art. 145
    /// manda pagar antes do inicio). Uma concessao que comeca em 02/01 e paga
    /// na folha de ferias de janeiro, mesmo que o periodo aquisitivo seja de
    /// dois anos atras.
    ///
    /// Quem nao tem concessao na competencia simplesmente nao entra: uma folha
    /// de ferias so tem quem sai de ferias.
    /// </summary>
    public void CalcularFerias(
        IEnumerable<ContratoTrabalho> contratosDaEmpresa,
        IEnumerable<ConcessaoFerias> concessoes,
        IReadOnlyDictionary<EstrategiaRubrica, Rubrica> rubricasDeFerias,
        ParametrosEncargos encargos,
        IReadOnlyDictionary<Guid, int> dependentesPorFuncionario,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(contratosDaEmpresa);
        ArgumentNullException.ThrowIfNull(concessoes);
        ArgumentNullException.ThrowIfNull(rubricasDeFerias);
        ArgumentNullException.ThrowIfNull(encargos);
        ArgumentNullException.ThrowIfNull(dependentesPorFuncionario);

        GarantirAberta("calcular");

        if (Tipo != TipoFolha.Ferias)
        {
            throw new InvalidOperationException(
                "Esta folha nao e de ferias: use o calculo do tipo dela.");
        }

        var contratos = contratosDaEmpresa
            .Where(c => c.IdEmpresa == IdEmpresa)
            .ToDictionary(c => c.Id);

        // Agrupa por contrato as concessoes que COMECAM nesta competencia.
        var porContrato = concessoes
            .Where(c => contratos.ContainsKey(c.IdContrato) && Competencia.Contem(c.Inicio))
            .GroupBy(c => c.IdContrato)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.Inicio).ToList());

        // Quem nao sai mais de ferias sai da folha - mesmo motivo do
        // recalculo da mensal: manter produziria pagamento indevido.
        _funcionarios.RemoveAll(f => !porContrato.ContainsKey(f.IdContrato));

        foreach (var (idContrato, doContrato) in porContrato)
        {
            var contrato = contratos[idContrato];

            var holerite = _funcionarios.SingleOrDefault(f => f.IdContrato == idContrato);

            if (holerite is null)
            {
                holerite = new FolhaFuncionario(IdOrganizacao, Id, idContrato, contrato.IdFuncionario);
                _funcionarios.Add(holerite);
            }

            var apuracoes = doContrato
                .Select(c =>
                {
                    // CLT art. 142: a remuneracao devida na DATA DA CONCESSAO.
                    // Por isso a vigencia e procurada pela data de inicio do
                    // gozo, e nao pela competencia da folha.
                    var vigencia = contrato.VigenciaEm(c.Inicio) ?? contrato.VigenciaAtual;
                    var salario = vigencia?.Salario ?? 0m;

                    return (
                        Apuracao: CalculadoraFerias.Apurar(salario, c.Dias, c.DiasAbonoPecuniario),
                        Rubricas: rubricasDeFerias);
                })
                .ToList();

            dependentesPorFuncionario.TryGetValue(contrato.IdFuncionario, out var dependentes);

            holerite.AplicarCalculoFerias(apuracoes, encargos, dependentes);
        }

        VersaoCalculo++;
        CalculadaEm = agora;
        Situacao = SituacaoFolha.Calculada;

        RecalcularTotais();
    }

    /// <summary>
    /// Calcula a folha do 13o SALARIO - o adiantamento ou a anual.
    ///
    /// UM metodo para os dois tipos, porque o que muda entre eles e a conta,
    /// nao a mecanica: quem entra na folha, como o holerite e montado e como os
    /// encargos rodam sao identicos. Dois metodos seriam duas copias da mesma
    /// varredura de contratos.
    ///
    /// ## Quem entra
    ///
    /// Todo contrato da empresa com pelo menos UM avo no ano. Nao e a
    /// elegibilidade da folha mensal: quem foi admitido em marco e saiu em
    /// setembro nao aparece na mensal de dezembro, mas tem 13o a receber.
    ///
    /// ## O ano, e nao a competencia
    ///
    /// Os avos sao do ANO CALENDARIO. A competencia da folha diz apenas quando
    /// se paga - novembro para o adiantamento, dezembro para a anual. Por isso
    /// o ano vem de Competencia.Ano e a apuracao dos avos nao usa o mes.
    ///
    /// ## Salario de referencia
    ///
    /// Lei 4.090/1962, art. 1o: 1/12 da remuneracao DEVIDA EM DEZEMBRO. Por
    /// isso a vigencia e procurada pela data de pagamento desta folha - que na
    /// anual e dezembro. Reajuste em dezembro alcanca o 13o inteiro, inclusive
    /// os avos de janeiro.
    /// </summary>
    /// <param name="adiantamentosPorContrato">
    /// Quanto de adiantamento cada contrato ja recebeu no ano. Vem das folhas
    /// de adiantamento ja calculadas - estado DERIVADO, nao um campo digitado.
    /// Vazio na folha de adiantamento.
    /// </param>
    public void Calcular13(
        IEnumerable<ContratoTrabalho> contratosDaEmpresa,
        IReadOnlyDictionary<EstrategiaRubrica, Rubrica> rubricasDe13,
        IReadOnlyDictionary<Guid, decimal> adiantamentosPorContrato,
        ParametrosEncargos encargos,
        IReadOnlyDictionary<Guid, int> dependentesPorFuncionario,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(contratosDaEmpresa);
        ArgumentNullException.ThrowIfNull(rubricasDe13);
        ArgumentNullException.ThrowIfNull(adiantamentosPorContrato);
        ArgumentNullException.ThrowIfNull(encargos);
        ArgumentNullException.ThrowIfNull(dependentesPorFuncionario);

        GarantirAberta("calcular");

        var adiantamento = Tipo == TipoFolha.DecimoTerceiroAdiantamento;

        if (!adiantamento && Tipo != TipoFolha.DecimoTerceiro)
        {
            throw new InvalidOperationException(
                "Esta folha nao e de 13o salario: use o calculo do tipo dela.");
        }

        var contratos = contratosDaEmpresa
            .Where(c => c.IdEmpresa == IdEmpresa)
            .ToList();

        var comDireito = new List<(ContratoTrabalho Contrato, int Avos, decimal Salario)>();

        foreach (var contrato in contratos)
        {
            var avos = AvosDecimoTerceiro.Apurar(contrato, Competencia.Ano).Avos;

            if (avos == 0)
            {
                continue;
            }

            var vigencia = contrato.VigenciaEm(Competencia.UltimoDia) ?? contrato.VigenciaAtual;

            comDireito.Add((contrato, avos, vigencia?.Salario ?? 0m));
        }

        var elegiveis = comDireito.Select(x => x.Contrato.Id).ToHashSet();

        // Quem perdeu o direito sai da folha no recalculo - mesmo motivo da
        // mensal: manter o holerite produziria pagamento indevido.
        _funcionarios.RemoveAll(f => !elegiveis.Contains(f.IdContrato));

        foreach (var (contrato, avos, salario) in comDireito)
        {
            var holerite = _funcionarios.SingleOrDefault(f => f.IdContrato == contrato.Id);

            if (holerite is null)
            {
                holerite = new FolhaFuncionario(
                    IdOrganizacao, Id, contrato.Id, contrato.IdFuncionario);

                _funcionarios.Add(holerite);
            }

            adiantamentosPorContrato.TryGetValue(contrato.Id, out var jaPago);

            var parcelas = adiantamento
                ? CalculadoraDecimoTerceiro
                    .ApurarAdiantamento(Competencia.Ano, avos, salario).Parcelas
                : CalculadoraDecimoTerceiro
                    .ApurarAnual(Competencia.Ano, avos, salario, jaPago).Parcelas;

            dependentesPorFuncionario.TryGetValue(contrato.IdFuncionario, out var dependentes);

            holerite.AplicarCalculo13(parcelas, rubricasDe13, avos, salario, encargos, dependentes);
        }

        VersaoCalculo++;
        CalculadaEm = agora;
        Situacao = SituacaoFolha.Calculada;

        RecalcularTotais();
    }

    /// <summary>
    /// Calcula a folha de RESCISAO: paga os acertos dos contratos desligados
    /// na competencia.
    ///
    /// O criterio e a DATA DO DESLIGAMENTO, e nao a elegibilidade normal: uma
    /// folha de rescisao so tem quem saiu.
    ///
    /// Contrato cujo motivo esta BLOQUEADO nao entra - a apuracao devolve zero
    /// verbas, e um holerite vazio no meio da folha pareceria erro de calculo
    /// em vez de motivo sem fonte. Quem chama recebe a lista dos ignorados.
    /// </summary>
    public IReadOnlyList<Guid> CalcularRescisao(
        IEnumerable<ContratoTrabalho> contratosDaEmpresa,
        IReadOnlyDictionary<string, Rubrica> rubricasDeRescisao,
        IReadOnlyDictionary<Guid, int> diasFeriasVencidasPorContrato,
        IReadOnlyDictionary<Guid, ValorBaseFgts> valoresBaseFgts,
        ParametrosEncargos encargos,
        IReadOnlyDictionary<Guid, int> dependentesPorFuncionario,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(contratosDaEmpresa);
        ArgumentNullException.ThrowIfNull(rubricasDeRescisao);
        ArgumentNullException.ThrowIfNull(diasFeriasVencidasPorContrato);
        ArgumentNullException.ThrowIfNull(valoresBaseFgts);
        ArgumentNullException.ThrowIfNull(encargos);
        ArgumentNullException.ThrowIfNull(dependentesPorFuncionario);

        GarantirAberta("calcular");

        if (Tipo != TipoFolha.Rescisao)
        {
            throw new InvalidOperationException(
                "Esta folha nao e de rescisao: use o calculo do tipo dela.");
        }

        var desligados = contratosDaEmpresa
            .Where(c => c.IdEmpresa == IdEmpresa
                        && c.DataDesligamento is { } d && Competencia.Contem(d))
            .ToList();

        var ignorados = new List<Guid>();
        var calculados = new HashSet<Guid>();

        foreach (var contrato in desligados)
        {
            var desligamento = contrato.DataDesligamento!.Value;

            // CLT art. 477: a remuneracao da data da saida.
            var salario = (contrato.VigenciaEm(desligamento) ?? contrato.VigenciaAtual)?.Salario ?? 0m;

            diasFeriasVencidasPorContrato.TryGetValue(contrato.Id, out var vencidas);
            valoresBaseFgts.TryGetValue(contrato.Id, out var baseFgts);

            var apuracao = CalculadoraRescisao.Apurar(contrato, salario, vencidas, baseFgts);

            if (!apuracao.Suportado || apuracao.Verbas.Count == 0)
            {
                ignorados.Add(contrato.Id);
                continue;
            }

            var holerite = _funcionarios.SingleOrDefault(f => f.IdContrato == contrato.Id);

            if (holerite is null)
            {
                holerite = new FolhaFuncionario(IdOrganizacao, Id, contrato.Id, contrato.IdFuncionario);
                _funcionarios.Add(holerite);
            }

            dependentesPorFuncionario.TryGetValue(contrato.IdFuncionario, out var dependentes);

            holerite.AplicarCalculoRescisao(
                apuracao.Verbas, rubricasDeRescisao, salario, encargos, dependentes);

            calculados.Add(contrato.Id);
        }

        // Quem deixou de ser calculado sai da folha - mesmo motivo do
        // recalculo da mensal.
        _funcionarios.RemoveAll(f => !calculados.Contains(f.IdContrato));

        VersaoCalculo++;
        CalculadaEm = agora;
        Situacao = SituacaoFolha.Calculada;

        RecalcularTotais();

        return ignorados;
    }

    public LancamentoFolha AdicionarLancamentoManual(
        Guid idFolhaFuncionario,
        Rubrica rubrica,
        decimal valor,
        string? referencia,
        ParametrosEncargos encargos)
    {
        GarantirAberta("lancar");

        var holerite = ObterHolerite(idFolhaFuncionario);
        var lancamento = holerite.AdicionarManual(rubrica, valor, referencia, encargos);

        RecalcularTotais();

        return lancamento;
    }

    public bool RemoverLancamento(
        Guid idFolhaFuncionario, Guid idLancamento, ParametrosEncargos encargos)
    {
        GarantirAberta("remover lancamento de");

        var removeu = ObterHolerite(idFolhaFuncionario).RemoverLancamento(idLancamento, encargos);

        if (removeu)
        {
            RecalcularTotais();
        }

        return removeu;
    }

    /// <summary>
    /// Fecha a folha. A partir daqui ela e um fato historico.
    ///
    /// Nao existe reabertura nesta fase, e isso e deliberado: o ROADMAP manda
    /// exigir "fluxo explicito futuro" depois do fechamento. Um metodo
    /// Reabrir() sem esse fluxo seria exatamente a sobrescrita silenciosa que
    /// o documento proibe.
    /// </summary>
    public void Fechar(DateTimeOffset agora)
    {
        if (EstaFechada)
        {
            throw new InvalidOperationException($"A folha de {Competencia} ja esta fechada.");
        }

        if (Situacao != SituacaoFolha.Calculada)
        {
            throw new InvalidOperationException(
                $"A folha de {Competencia} precisa ser calculada antes de fechar.");
        }

        if (_funcionarios.Count == 0)
        {
            throw new InvalidOperationException(
                $"A folha de {Competencia} nao tem nenhum funcionario e nao faz sentido fechar.");
        }

        Situacao = SituacaoFolha.Fechada;
        FechadaEm = agora;
    }

    private FolhaFuncionario ObterHolerite(Guid idFolhaFuncionario) =>
        _funcionarios.SingleOrDefault(f => f.Id == idFolhaFuncionario)
        ?? throw new InvalidOperationException("Funcionario nao pertence a esta folha.");

    private void GarantirAberta(string acao)
    {
        if (EstaFechada)
        {
            throw new InvalidOperationException(
                $"Nao da para {acao} uma folha fechada. A de {Competencia} foi fechada em "
                + $"{FechadaEm:dd/MM/yyyy}.");
        }
    }

    private void RecalcularTotais()
    {
        TotalProventos = _funcionarios.Sum(f => f.TotalProventos);
        TotalDescontos = _funcionarios.Sum(f => f.TotalDescontos);
        TotalLiquido = _funcionarios.Sum(f => f.Liquido);
    }
}
