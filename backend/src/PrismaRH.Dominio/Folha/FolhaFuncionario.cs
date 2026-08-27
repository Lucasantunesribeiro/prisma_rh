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
        ApuracaoSalarioBase apuracao, Rubrica rubricaSalario, ParametrosInss? inss)
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
        ApurarInss(inss);
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
        Rubrica rubrica, decimal valor, string? referencia, ParametrosInss? inss)
    {
        ArgumentNullException.ThrowIfNull(rubrica);

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
        ApurarInss(inss);

        return lancamento;
    }

    internal bool RemoverLancamento(Guid idLancamento, ParametrosInss? inss)
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
        ApurarInss(inss);

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
