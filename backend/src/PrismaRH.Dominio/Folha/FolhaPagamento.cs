using PrismaRH.Dominio.Contratos;

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

    public FolhaPagamento(Guid idOrganizacao, Guid idEmpresa, Competencia competencia, DateTimeOffset criadoEm)
    {
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
        Situacao = SituacaoFolha.Rascunho;
        CriadoEm = criadoEm;
    }

    public Guid Id { get; private set; }
    public Guid IdOrganizacao { get; private set; }
    public Guid IdEmpresa { get; private set; }
    public Competencia Competencia { get; private set; }
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
