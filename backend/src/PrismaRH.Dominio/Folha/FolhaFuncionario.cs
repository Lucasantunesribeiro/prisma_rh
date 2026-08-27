namespace PrismaRH.Dominio.Folha;

/// <summary>
/// O holerite de um contrato dentro de uma folha.
///
/// Guarda os totais em vez de somar os lancamentos toda vez que a tela abre.
/// Nao e cache: e o resultado, e o CLAUDE.md secao 4.3 exige que ele
/// sobreviva a alteracoes cadastrais posteriores. Se o salario da vigencia for
/// corrigido em novembro, a folha de agosto continua mostrando o que pagou.
/// </summary>
public sealed class FolhaFuncionario
{
    private readonly List<LancamentoFolha> _lancamentos = [];
    private readonly List<BaseApurada> _bases = [];

    private FolhaFuncionario()
    {
    }

    internal FolhaFuncionario(Guid idOrganizacao, Guid idFolha, Guid idContrato, Guid idFuncionario)
    {
        Id = Guid.CreateVersion7();
        IdOrganizacao = idOrganizacao;
        IdFolha = idFolha;
        IdContrato = idContrato;
        IdFuncionario = idFuncionario;
        Divisor = MotorCalculoFolha.DivisorMensal;

        // As tres bases nascem com o holerite, zeradas. Cria-las so quando
        // alguem calcula deixaria um holerite recem-aberto sem base alguma, e
        // a tela teria que distinguir "base zero" de "base ausente" - duas
        // coisas que significam o mesmo para quem le.
        foreach (var baseCalculo in BasesDeCalculo.Individuais)
        {
            _bases.Add(new BaseApurada(idOrganizacao, Id, baseCalculo));
        }
    }

    public Guid Id { get; private set; }
    public Guid IdOrganizacao { get; private set; }
    public Guid IdFolha { get; private set; }
    public Guid IdContrato { get; private set; }
    public Guid IdFuncionario { get; private set; }

    /// <summary>Avos do mes a que este contrato teve direito. 30 significa mes cheio.</summary>
    public int Avos { get; private set; }

    public int Divisor { get; private set; }

    /// <summary>
    /// Salario da vigencia usada como referencia, congelado no calculo.
    /// Zero enquanto a folha nunca foi calculada.
    /// </summary>
    public decimal SalarioReferencia { get; private set; }

    /// <summary>Vigencia que valia no fim do periodo trabalhado. Rastreia de onde saiu o salario.</summary>
    public Guid? IdVigenciaReferencia { get; private set; }

    public decimal TotalProventos { get; private set; }
    public decimal TotalDescontos { get; private set; }
    public decimal Liquido { get; private set; }

    /// <summary>
    /// Os lancamentos na ordem do holerite. A ordenacao e feita na leitura, e
    /// nao reorganizando a lista, porque essa lista e a colecao rastreada pelo
    /// EF Core: limpar e repovoar faria o EF entender que todo lancamento foi
    /// apagado e recriado, gerando DELETE e INSERT a cada calculo.
    /// </summary>
    public IReadOnlyList<LancamentoFolha> Lancamentos =>
        [.. _lancamentos.OrderBy(l => l.Ordem).ThenBy(l => l.Id)];

    /// <summary>
    /// As bases de calculo do holerite: INSS, FGTS e IRRF. Sempre as tres,
    /// mesmo zeradas.
    /// </summary>
    public IReadOnlyList<BaseApurada> Bases => [.. _bases.OrderBy(b => b.Base)];

    /// <summary>Quanto vale a base indicada. Zero se ela nunca foi apurada.</summary>
    public decimal BaseDe(BaseCalculo baseCalculo) =>
        _bases.SingleOrDefault(b => b.Base == baseCalculo)?.Valor ?? 0m;

    /// <summary>
    /// Aplica o resultado do motor.
    ///
    /// Remove APENAS os lancamentos calculados. Os manuais sobrevivem ao
    /// recalculo - se nao sobrevivessem, o analista perderia tudo que digitou
    /// a cada clique em "calcular", e a folha viraria um trabalho de Sisifo.
    /// </summary>
    internal void AplicarCalculo(ApuracaoSalarioBase apuracao, Rubrica rubricaSalario)
    {
        ArgumentNullException.ThrowIfNull(apuracao);
        ArgumentNullException.ThrowIfNull(rubricaSalario);

        _lancamentos.RemoveAll(l => l.Origem == OrigemLancamento.Calculado);

        Avos = apuracao.Avos;
        Divisor = apuracao.Divisor;
        SalarioReferencia = apuracao.SalarioReferencia;
        IdVigenciaReferencia = apuracao.IdVigenciaReferencia;

        var salario = new LancamentoFolha(
            IdOrganizacao,
            Id,
            rubricaSalario,
            OrigemLancamento.Calculado,
            apuracao.Valor,
            $"{apuracao.Avos}/{apuracao.Divisor}",
            ordem: 1);

        salario.RegistrarMemoria(apuracao.Passos);

        _lancamentos.Add(salario);

        Reordenar();
        RecalcularTotais();
    }

    internal LancamentoFolha AdicionarManual(Rubrica rubrica, decimal valor, string? referencia)
    {
        ArgumentNullException.ThrowIfNull(rubrica);

        if (!rubrica.Ativa)
        {
            throw new InvalidOperationException($"A rubrica {rubrica.Codigo} esta inativa.");
        }

        if (rubrica.Estrategia != EstrategiaRubrica.ValorInformado)
        {
            // Deixar alguem digitar o salario-base a mao criaria duas linhas
            // de salario no holerite: a digitada e a que o proximo calculo
            // produz. O total dobraria sem ninguem entender por que.
            throw new InvalidOperationException(
                $"A rubrica {rubrica.Codigo} e calculada pelo sistema e nao aceita valor digitado.");
        }

        var lancamento = new LancamentoFolha(
            IdOrganizacao, Id, rubrica, OrigemLancamento.Manual, valor, referencia, _lancamentos.Count + 1);

        lancamento.RegistrarMemoria([
            new PassoCalculo("Valor informado no lancamento manual", rubrica.Nome, lancamento.Valor)
        ]);

        _lancamentos.Add(lancamento);

        Reordenar();
        RecalcularTotais();

        return lancamento;
    }

    internal bool RemoverLancamento(Guid idLancamento)
    {
        var alvo = _lancamentos.SingleOrDefault(l => l.Id == idLancamento);

        if (alvo is null)
        {
            return false;
        }

        if (alvo.Origem == OrigemLancamento.Calculado)
        {
            throw new InvalidOperationException(
                "Lancamento calculado nao se apaga: ele desaparece sozinho no proximo calculo.");
        }

        _lancamentos.Remove(alvo);

        Reordenar();
        RecalcularTotais();

        return true;
    }

    /// <summary>
    /// Calculados primeiro, manuais depois, cada grupo na ordem em que entrou.
    /// E a ordem que um holerite tem: o salario encabeca, o resto segue.
    ///
    /// Reescreve o campo Ordem sem tocar na lista - ver o comentario em
    /// Lancamentos sobre por que a lista fisica nao pode ser remontada.
    /// </summary>
    private void Reordenar()
    {
        var ordem = 1;

        foreach (var lancamento in _lancamentos
            .OrderBy(l => l.Origem == OrigemLancamento.Calculado ? 0 : 1)
            .ThenBy(l => l.Ordem)
            .ThenBy(l => l.Id))
        {
            lancamento.DefinirOrdem(ordem++);
        }
    }

    private void RecalcularTotais()
    {
        TotalProventos = _lancamentos.Where(l => l.Tipo == TipoRubrica.Provento).Sum(l => l.Valor);
        TotalDescontos = _lancamentos.Where(l => l.Tipo == TipoRubrica.Desconto).Sum(l => l.Valor);
        Liquido = TotalProventos - TotalDescontos;

        ApurarBases();
    }

    /// <summary>
    /// Soma, para cada base, os lancamentos que a compoem.
    ///
    /// Le a incidencia CONGELADA no lancamento, nunca a rubrica atual: e o que
    /// garante que alterar a incidencia de uma rubrica nao mexa em holerite ja
    /// calculado.
    ///
    /// Atualiza a linha existente em vez de limpar e repovoar a lista. Mesmo
    /// motivo de Reordenar, explicado em Lancamentos: remontar a colecao
    /// rastreada faria o EF Core emitir DELETE e INSERT das tres bases a cada
    /// calculo, e o holerite tem tres bases exatamente porque elas sao fixas.
    /// </summary>
    private void ApurarBases()
    {
        foreach (var baseCalculo in BasesDeCalculo.Individuais)
        {
            var valor = _lancamentos
                .Where(l => l.Compoe(baseCalculo))
                .Sum(l => l.EfeitoNaBase);

            var linha = _bases.SingleOrDefault(b => b.Base == baseCalculo);

            if (linha is null)
            {
                // Holerite gravado antes desta base existir. Nasce agora.
                linha = new BaseApurada(IdOrganizacao, Id, baseCalculo);
                _bases.Add(linha);
            }

            linha.DefinirValor(valor);
        }
    }
}
