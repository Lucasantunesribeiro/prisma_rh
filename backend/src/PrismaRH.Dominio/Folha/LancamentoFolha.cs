using PrismaRH.Dominio.Contratos;

namespace PrismaRH.Dominio.Folha;

/// <summary>
/// Uma linha do holerite: qual rubrica, quanto, e por que.
///
/// Copia codigo, nome e tipo da rubrica em vez de so apontar para ela. Isso
/// parece duplicacao e nao e: uma folha fechada e um fato historico, e se
/// alguem renomear a rubrica "Comissao" para "Comissao sobre vendas" no ano
/// que vem, o holerite de agosto tem que continuar dizendo o que dizia quando
/// foi fechado. O CLAUDE.md secao 4.3 chama isso de nao destruir o passado.
/// </summary>
public sealed class LancamentoFolha
{
    public const int TamanhoMaximoReferencia = 40;

    private readonly List<LinhaMemoriaCalculo> _memoria = [];

    private LancamentoFolha()
    {
    }

    internal LancamentoFolha(
        Guid idOrganizacao,
        Guid idFolhaFuncionario,
        Rubrica rubrica,
        OrigemLancamento origem,
        decimal valor,
        string? referencia,
        int ordem)
    {
        ArgumentNullException.ThrowIfNull(rubrica);

        if (valor < 0)
        {
            // Valor negativo tornaria o tipo da rubrica irrelevante: um
            // desconto de -100 viraria um provento disfarcado, e o total de
            // descontos do holerite deixaria de bater com a soma da coluna.
            throw new ArgumentException(
                "Valor do lancamento nao pode ser negativo. Use o tipo da rubrica para descontar.",
                nameof(valor));
        }

        Id = Guid.CreateVersion7();
        IdOrganizacao = idOrganizacao;
        IdFolhaFuncionario = idFolhaFuncionario;
        IdRubrica = rubrica.Id;
        CodigoRubrica = rubrica.Codigo;
        NomeRubrica = rubrica.Nome;
        Tipo = rubrica.Tipo;
        Estrategia = rubrica.Estrategia;
        BasesIncidentes = rubrica.BasesIncidentes;
        Origem = origem;
        Valor = Dinheiro.Arredondar(valor);
        Referencia = string.IsNullOrWhiteSpace(referencia)
            ? null
            : Cargo.ValidarTexto(referencia, TamanhoMaximoReferencia, "Referencia", nameof(referencia));
        Ordem = ordem;
    }

    public Guid Id { get; private set; }
    public Guid IdOrganizacao { get; private set; }
    public Guid IdFolhaFuncionario { get; private set; }
    public Guid IdRubrica { get; private set; }

    /// <summary>Codigo da rubrica no momento do calculo. Congelado de proposito.</summary>
    public string CodigoRubrica { get; private set; } = string.Empty;

    /// <summary>Nome da rubrica no momento do calculo. Congelado de proposito.</summary>
    public string NomeRubrica { get; private set; } = string.Empty;

    public TipoRubrica Tipo { get; private set; }

    /// <summary>
    /// Como a rubrica produziu este valor, congelado no calculo. Serve para
    /// saber, ao reprocessar, qual linha o sistema gerou e precisa refazer -
    /// sem depender de estado em memoria, que nao sobrevive a leitura do banco.
    /// </summary>
    public EstrategiaRubrica Estrategia { get; private set; }

    /// <summary>
    /// Bases que a rubrica compunha no momento do calculo. Congelado de
    /// proposito, como o codigo e o nome: se a lei mudar e o vale-transporte
    /// passar a compor a base de FGTS, a folha de agosto ja fechada tem que
    /// continuar dizendo qual base ELA usou.
    /// </summary>
    public BaseCalculo BasesIncidentes { get; private set; }

    public OrigemLancamento Origem { get; private set; }
    public decimal Valor { get; private set; }

    /// <summary>A quantidade que originou o valor, para leitura: "30/30", "10h".</summary>
    public string? Referencia { get; private set; }

    public int Ordem { get; private set; }

    public IReadOnlyList<LinhaMemoriaCalculo> Memoria => _memoria;

    /// <summary>Quanto este lancamento soma (+) ou tira (-) do liquido.</summary>
    public decimal EfeitoNoLiquido => Tipo switch
    {
        TipoRubrica.Provento => Valor,
        TipoRubrica.Desconto => -Valor,
        _ => 0m,
    };

    /// <summary>
    /// Quanto este lancamento soma nas bases em que incide.
    ///
    /// Desconto vale zero mesmo que alguem consiga marca-lo: a invariante em
    /// Rubrica ja recusa isso na entrada, e aqui e a segunda tranca. Um
    /// desconto somando na base faria o INSS incidir sobre o vale-transporte.
    /// </summary>
    public decimal EfeitoNaBase => Tipo == TipoRubrica.Desconto ? 0m : Valor;

    /// <summary>
    /// Se este lancamento entra na base indicada.
    ///
    /// O teste explicito contra Nenhuma existe porque HasFlag(Nenhuma) devolve
    /// true para qualquer valor - zero esta contido em tudo. Sem ele, "quais
    /// lancamentos compoem a base Nenhuma" devolveria o holerite inteiro.
    /// </summary>
    public bool Compoe(BaseCalculo baseCalculo) =>
        baseCalculo != BaseCalculo.Nenhuma && BasesIncidentes.HasFlag(baseCalculo);

    /// <summary>
    /// Reaplica a incidencia atual da rubrica. Chamado APENAS no recalculo de
    /// folha aberta.
    ///
    /// Nao contradiz o congelamento: o congelamento protege o passado, e folha
    /// fechada recusa recalculo. Numa folha aberta, "recalcular" significa
    /// "aplicar as regras de agora" - e a incidencia e regra do catalogo, nao
    /// dado que o analista digitou. Dele sao a rubrica e o valor, que este
    /// metodo nao toca.
    ///
    /// Sem isto, corrigir uma rubrica mal parametrizada nao consertaria
    /// nenhuma folha aberta: seria preciso apagar e relancar cada lancamento
    /// manual, um a um.
    /// </summary>
    internal void AtualizarIncidencias(BaseCalculo bases) => BasesIncidentes = bases;

    internal void DefinirOrdem(int ordem) => Ordem = ordem;

    internal void RegistrarMemoria(IEnumerable<PassoCalculo> passos)
    {
        ArgumentNullException.ThrowIfNull(passos);

        _memoria.Clear();

        var ordem = 1;

        foreach (var passo in passos)
        {
            _memoria.Add(new LinhaMemoriaCalculo(
                IdOrganizacao, Id, ordem++, passo.Descricao, passo.Expressao, passo.Valor));
        }
    }
}
