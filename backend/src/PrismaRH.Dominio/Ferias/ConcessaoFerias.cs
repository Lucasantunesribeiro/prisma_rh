namespace PrismaRH.Dominio.Ferias;

/// <summary>
/// Onde uma concessao esta, em relacao a uma data. Derivado, nunca guardado.
/// </summary>
public enum SituacaoConcessao
{
    /// <summary>O gozo ainda vai comecar.</summary>
    Programada = 1,

    /// <summary>A pessoa esta de ferias hoje.</summary>
    EmGozo = 2,

    /// <summary>O gozo terminou.</summary>
    Concluida = 3
}

/// <summary>
/// Uma concessao de ferias: os dias que a pessoa vai gozar (e os que vendeu)
/// de um periodo aquisitivo.
///
/// FONTES (CLAUDE.md secao 29):
/// - CLT art. 134, par. 1o (redacao da Lei 13.467/2017): as ferias podem ser
///   fracionadas em ATE TRES periodos, um deles nao inferior a 14 dias
///   corridos e os demais nao inferiores a 5 dias corridos cada um;
/// - CLT art. 143: o empregado pode converter UM TERCO do periodo a que tem
///   direito em abono pecuniario.
///
/// Esta e a entidade que TEM estado - ao contrario do periodo aquisitivo, que
/// e derivado do calendario (ver PeriodosAquisitivos). Ela existe porque
/// alguem decidiu conceder, e essa decisao nao se recalcula.
///
/// O periodo aquisitivo e referenciado pelas suas DATAS, e nao por um id: ele
/// nao tem tabela, e as datas sao a identidade natural dele. Corrigir a
/// admissao de um contrato desloca os periodos, e uma concessao apontando para
/// um intervalo que nao existe mais fica visivelmente orfa - o que e melhor do
/// que apontar em silencio para o periodo errado.
/// </summary>
public sealed class ConcessaoFerias
{
    /// <summary>
    /// Maximo de concessoes de GOZO por periodo aquisitivo (art. 134, par. 1o).
    /// Vender dias em abono nao conta como um dos tres.
    /// </summary>
    public const int MaximoFracoes = 3;

    /// <summary>Uma das fracoes precisa ter ao menos isto (art. 134, par. 1o).</summary>
    public const int DiasMinimosDaMaiorFracao = 14;

    /// <summary>As demais fracoes precisam ter ao menos isto (art. 134, par. 1o).</summary>
    public const int DiasMinimosDasDemaisFracoes = 5;

    private ConcessaoFerias()
    {
    }

    public ConcessaoFerias(
        Guid idOrganizacao,
        Guid idContrato,
        PeriodoAquisitivo periodo,
        DateOnly inicio,
        int dias,
        int diasAbonoPecuniario,
        DateTimeOffset criadaEm)
    {
        ArgumentNullException.ThrowIfNull(periodo);

        if (idOrganizacao == Guid.Empty)
        {
            throw new ArgumentException("Concessao precisa pertencer a uma organizacao.", nameof(idOrganizacao));
        }

        if (idContrato == Guid.Empty)
        {
            throw new ArgumentException("Concessao precisa pertencer a um contrato.", nameof(idContrato));
        }

        if (dias < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dias), dias, "Dias de gozo nao podem ser negativos.");
        }

        if (diasAbonoPecuniario < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(diasAbonoPecuniario), diasAbonoPecuniario, "Dias de abono nao podem ser negativos.");
        }

        if (dias == 0 && diasAbonoPecuniario == 0)
        {
            throw new ArgumentException("Uma concessao sem dias de gozo nem de abono nao concede nada.", nameof(dias));
        }

        // O gozo nao pode comecar antes de o direito existir. Ferias
        // antecipadas sao situacao excepcional e nao estao no escopo.
        if (inicio <= periodo.Fim)
        {
            throw new ArgumentException(
                $"O gozo nao pode comecar antes de o periodo aquisitivo terminar em {periodo.Fim:dd/MM/yyyy}.",
                nameof(inicio));
        }

        Id = Guid.CreateVersion7();
        IdOrganizacao = idOrganizacao;
        IdContrato = idContrato;
        InicioPeriodoAquisitivo = periodo.Inicio;
        FimPeriodoAquisitivo = periodo.Fim;
        Inicio = inicio;
        Dias = dias;
        DiasAbonoPecuniario = diasAbonoPecuniario;
        CriadaEm = criadaEm;
    }

    public Guid Id { get; private set; }
    public Guid IdOrganizacao { get; private set; }
    public Guid IdContrato { get; private set; }

    /// <summary>Identifica o periodo aquisitivo pelas datas dele.</summary>
    public DateOnly InicioPeriodoAquisitivo { get; private set; }

    public DateOnly FimPeriodoAquisitivo { get; private set; }

    /// <summary>Primeiro dia de gozo. Sem gozo (so abono), e a data de referencia.</summary>
    public DateOnly Inicio { get; private set; }

    /// <summary>Dias efetivamente gozados.</summary>
    public int Dias { get; private set; }

    /// <summary>Dias convertidos em dinheiro (art. 143). Nao sao gozados.</summary>
    public int DiasAbonoPecuniario { get; private set; }

    public DateTimeOffset CriadaEm { get; private set; }

    /// <summary>Ultimo dia de gozo. Dias corridos, feriados inclusos.</summary>
    public DateOnly Fim => Dias == 0 ? Inicio : Inicio.AddDays(Dias - 1);

    /// <summary>Quantos dias do periodo esta concessao consome ao todo.</summary>
    public int DiasBaixados => Dias + DiasAbonoPecuniario;

    /// <summary>Esta concessao pertence a este periodo?</summary>
    public bool EDoPeriodo(PeriodoAquisitivo periodo)
    {
        ArgumentNullException.ThrowIfNull(periodo);

        return InicioPeriodoAquisitivo == periodo.Inicio && FimPeriodoAquisitivo == periodo.Fim;
    }

    public SituacaoConcessao SituacaoEm(DateOnly referencia)
    {
        if (referencia < Inicio)
        {
            return SituacaoConcessao.Programada;
        }

        return referencia > Fim ? SituacaoConcessao.Concluida : SituacaoConcessao.EmGozo;
    }

    /// <summary>
    /// A concessao ainda pode ser cancelada?
    ///
    /// So antes de comecar. Cancelar ferias que a pessoa ja esta gozando nao e
    /// operacao de cadastro - envolve retorno ao trabalho e acerto do que foi
    /// pago, e nao esta no escopo desta etapa.
    /// </summary>
    public bool PodeSerCancelada(DateOnly referencia) =>
        SituacaoEm(referencia) == SituacaoConcessao.Programada;
}
