using PrismaRH.Dominio.Parametros;

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

    /// <summary>
    /// Quantos dependentes abatiam IRRF quando este holerite foi calculado.
    ///
    /// CONGELADO, como o codigo e a incidencia das rubricas (CLAUDE.md secao
    /// 4.3). Cadastrar um filho hoje nao pode mudar o imposto de uma folha
    /// fechada em marco - a pessoa nao era dependente naquela competencia.
    /// </summary>
    public int QuantidadeDependentesIrrf { get; private set; }

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
    internal void AplicarCalculo(
        ApuracaoSalarioBase apuracao,
        Rubrica rubricaSalario,
        ParametrosEncargos encargos,
        int quantidadeDependentesIrrf)
    {
        ArgumentNullException.ThrowIfNull(apuracao);
        ArgumentNullException.ThrowIfNull(rubricaSalario);
        ArgumentNullException.ThrowIfNull(encargos);

        if (quantidadeDependentesIrrf < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantidadeDependentesIrrf), quantidadeDependentesIrrf,
                "Quantidade de dependentes nao pode ser negativa.");
        }

        // Recalcular e o unico momento em que a quantidade e relida do
        // cadastro. Depois disso ela fica congelada neste holerite.
        QuantidadeDependentesIrrf = quantidadeDependentesIrrf;

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
        ApurarEncargos(encargos);
    }

    /// <summary>
    /// Apura os encargos calculados, na ordem que a dependencia exige.
    ///
    /// INSS primeiro porque o IRRF o DEDUZ da base. FGTS no meio porque nao
    /// depende de nenhum dos dois. IRRF por ultimo, e essa ordem nao e
    /// estetica: apurar o IRRF antes do INSS usaria a deducao do calculo
    /// anterior, e o imposto sairia errado sem nenhuma linha parecer errada.
    /// </summary>
    private void ApurarEncargos(ParametrosEncargos encargos)
    {
        ApurarInss(encargos.Inss);
        ApurarFgts(encargos.Fgts);
        ApurarIrrf(encargos.Irrf);
    }

    /// <summary>
    /// Recalcula o desconto de INSS sobre a base ja apurada.
    ///
    /// Roda DEPOIS de RecalcularTotais, porque depende da base de INSS, que so
    /// existe depois que os lancamentos foram somados. E nao gera laco: o
    /// lancamento de INSS e desconto e nao compoe base alguma (a invariante da
    /// Fase 4A garante), entao apura-lo nao muda a base que o originou.
    ///
    /// Sem parametros - organizacao sem INSS configurado, ou competencia
    /// anterior a qualquer tabela - o lancamento anterior e removido e nada
    /// entra no lugar. Deixar um valor velho ali seria pior que nao ter.
    /// </summary>
    private void ApurarInss(ParametrosInss? inss)
    {
        // Identifica pela estrategia CONGELADA no lancamento, e nao por
        // estado em memoria: o holerite vem do banco sem memoria nenhuma, e um
        // INSS velho sobrevivendo ali deixaria o liquido errado.
        var removidos = _lancamentos.RemoveAll(l => l.Estrategia == EstrategiaRubrica.InssProgressivo);

        if (inss is null)
        {
            if (removidos > 0)
            {
                Reordenar();
                RecalcularTotais();
            }

            return;
        }

        var apuracao = CalculadoraInss.Apurar(BaseDe(BaseCalculo.Inss), inss.Tabela);

        var lancamento = new LancamentoFolha(
            IdOrganizacao,
            Id,
            inss.Rubrica,
            OrigemLancamento.Calculado,
            apuracao.Valor,
            referencia: null,
            ordem: _lancamentos.Count + 1);

        lancamento.RegistrarMemoria(apuracao.Passos);

        _lancamentos.Add(lancamento);

        Reordenar();

        // Os descontos mudaram: refaz os totais. As bases NAO mudam, porque o
        // INSS e desconto e desconto nao compoe base.
        RecalcularTotais();
    }

    /// <summary>
    /// Reaplica nos lancamentos manuais a incidencia atual de cada rubrica.
    ///
    /// Rubrica que nao esta mais no catalogo mantem o que tinha - apagar a
    /// incidencia de um lancamento cuja rubrica foi removida zeraria a base
    /// sem que ninguem tivesse pedido isso.
    /// </summary>
    internal void AtualizarIncidenciasManuais(IReadOnlyDictionary<Guid, Rubrica> catalogo)
    {
        ArgumentNullException.ThrowIfNull(catalogo);

        foreach (var lancamento in _lancamentos.Where(l => l.Origem == OrigemLancamento.Manual))
        {
            if (catalogo.TryGetValue(lancamento.IdRubrica, out var rubrica))
            {
                lancamento.AtualizarIncidencias(rubrica.BasesIncidentes);
            }
        }
    }

    internal LancamentoFolha AdicionarManual(
        Rubrica rubrica,
        decimal valor,
        string? referencia,
        ParametrosEncargos encargos)
    {
        ArgumentNullException.ThrowIfNull(rubrica);
        ArgumentNullException.ThrowIfNull(encargos);

        if (!rubrica.Ativa)
        {
            throw new InvalidOperationException($"A rubrica {rubrica.Codigo} esta inativa.");
        }

        if (rubrica.Estrategia != EstrategiaRubrica.ValorInformado)
        {
            // Vale para salario-base e para INSS. Digitar a mao criaria uma
            // segunda linha alem da que o sistema calcula, e o total dobraria
            // sem ninguem entender por que.
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

        // Sem reler o cadastro: usa a quantidade JA congelada. Lancar uma
        // comissao nao e momento de trocar os dependentes do holerite.
        ApurarEncargos(encargos);

        return lancamento;
    }

    internal bool RemoverLancamento(Guid idLancamento, ParametrosEncargos encargos)
    {
        ArgumentNullException.ThrowIfNull(encargos);

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
        ApurarEncargos(encargos);

        return true;
    }

    /// <summary>
    /// Recalcula o deposito de FGTS sobre a base ja apurada.
    ///
    /// Roda DEPOIS do INSS por clareza de leitura do holerite, nao por
    /// dependencia: o FGTS incide sobre a base de FGTS, e o INSS e desconto,
    /// que nao compoe base alguma. A ordem das duas apuracoes nao muda
    /// resultado nenhum.
    ///
    /// Nao mexe nos totais: a rubrica e informativa e EfeitoNoLiquido devolve
    /// zero para ela. Chamar RecalcularTotais aqui seria inofensivo, e nao
    /// chamar deixa explicito que FGTS nao entra no liquido.
    /// </summary>
    private void ApurarFgts(ParametrosFgts? fgts)
    {
        // Identifica pela estrategia congelada, como o INSS: o holerite vem do
        // banco sem memoria nenhuma.
        _lancamentos.RemoveAll(l => l.Estrategia == EstrategiaRubrica.FgtsMensal);

        if (fgts is null)
        {
            Reordenar();
            return;
        }

        var apuracao = CalculadoraFgts.Apurar(BaseDe(BaseCalculo.Fgts), fgts.Tabela);

        var lancamento = new LancamentoFolha(
            IdOrganizacao,
            Id,
            fgts.Rubrica,
            OrigemLancamento.Calculado,
            apuracao.Valor,
            referencia: null,
            ordem: _lancamentos.Count + 1);

        lancamento.RegistrarMemoria(apuracao.Passos);

        _lancamentos.Add(lancamento);

        Reordenar();
    }

    /// <summary>
    /// Recalcula o IRRF sobre a base ja apurada, deduzidos INSS e dependentes.
    ///
    /// Roda por ULTIMO, e aqui a ordem importa de verdade: o IRRF deduz o
    /// INSS do mes, entao precisa do valor que ApurarInss acabou de gravar.
    /// Le esse valor do LANCAMENTO, e nao de um campo em memoria, porque o
    /// holerite pode ter vindo do banco.
    ///
    /// Nao gera laco: o IRRF e desconto e desconto nao compoe base alguma
    /// (invariante da Fase 4A), entao apura-lo nao muda a base que o originou
    /// nem o INSS que ele deduziu.
    /// </summary>
    private void ApurarIrrf(ParametrosIrrf? irrf)
    {
        var removidos = _lancamentos.RemoveAll(l => l.Estrategia == EstrategiaRubrica.IrrfMensal);

        if (irrf is null)
        {
            if (removidos > 0)
            {
                Reordenar();
                RecalcularTotais();
            }

            return;
        }

        var deducaoInss = _lancamentos
            .Where(l => l.Estrategia == EstrategiaRubrica.InssProgressivo)
            .Sum(l => l.Valor);

        var apuracao = CalculadoraIrrf.Apurar(
            BaseDe(BaseCalculo.Irrf), deducaoInss, QuantidadeDependentesIrrf, irrf.Tabela);

        var lancamento = new LancamentoFolha(
            IdOrganizacao,
            Id,
            irrf.Rubrica,
            OrigemLancamento.Calculado,
            apuracao.Valor,
            referencia: null,
            ordem: _lancamentos.Count + 1);

        lancamento.RegistrarMemoria(apuracao.Passos);

        _lancamentos.Add(lancamento);

        Reordenar();

        // Os descontos mudaram. As bases nao: IRRF e desconto.
        RecalcularTotais();
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
