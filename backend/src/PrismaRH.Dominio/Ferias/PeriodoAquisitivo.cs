using PrismaRH.Dominio.Contratos;

namespace PrismaRH.Dominio.Ferias;

/// <summary>
/// Onde um periodo aquisitivo esta, em relacao a uma data.
///
/// Vocabulario fechado, e derivado de datas - nao e um campo que alguem muda.
/// </summary>
public enum SituacaoPeriodoAquisitivo
{
    /// <summary>Ainda esta correndo: o direito nao foi adquirido.</summary>
    EmAndamento = 1,

    /// <summary>Completou 12 meses e ainda esta dentro do prazo de concessao.</summary>
    Adquirido = 2,

    /// <summary>
    /// Passou do prazo do art. 134 sem ser concedido. A remuneracao passa a
    /// ser devida EM DOBRO (art. 137).
    /// </summary>
    Vencido = 3
}

/// <summary>
/// Um periodo aquisitivo de ferias.
///
/// FONTES (CLAUDE.md secao 29):
/// - CLT art. 130: apos cada periodo de 12 meses de vigencia do contrato, o
///   empregado tem direito a ferias;
/// - CLT art. 134: as ferias sao concedidas nos 12 meses SUBSEQUENTES a data
///   em que o direito foi adquirido - o periodo concessivo;
/// - CLT art. 137: concedidas apos esse prazo, a remuneracao e paga EM DOBRO.
///
/// E um record, e nao entidade: nao tem identidade propria nem estado que
/// alguem altere. Ver a nota em <see cref="PeriodosAquisitivos"/> sobre por
/// que ele NAO tem tabela no banco.
/// </summary>
public sealed record PeriodoAquisitivo(
    int Numero,
    DateOnly Inicio,
    DateOnly Fim,
    int DiasDireito)
{
    /// <summary>
    /// Quando o prazo para conceder comeca: o dia seguinte ao fim do
    /// aquisitivo.
    /// </summary>
    public DateOnly InicioConcessao => Fim.AddDays(1);

    /// <summary>
    /// Ate quando as ferias podem ser concedidas sem dobra: 12 meses depois do
    /// fim do aquisitivo (art. 134).
    /// </summary>
    public DateOnly LimiteConcessao => Fim.AddYears(1);

    /// <summary>Onde este periodo esta, na data informada.</summary>
    public SituacaoPeriodoAquisitivo SituacaoEm(DateOnly referencia)
    {
        if (referencia <= Fim)
        {
            return SituacaoPeriodoAquisitivo.EmAndamento;
        }

        return referencia > LimiteConcessao
            ? SituacaoPeriodoAquisitivo.Vencido
            : SituacaoPeriodoAquisitivo.Adquirido;
    }

    /// <summary>
    /// A remuneracao deste periodo sera devida em dobro se concedida na data
    /// informada? (art. 137)
    ///
    /// Quem aplica a dobra e o CALCULO de ferias, na etapa 2. Aqui o dominio
    /// apenas responde a pergunta, para a tela poder avisar antes.
    /// </summary>
    public bool EmDobraSeConcedidoEm(DateOnly referencia) =>
        SituacaoEm(referencia) == SituacaoPeriodoAquisitivo.Vencido;

    /// <summary>Quantos dias faltam para o periodo se completar.</summary>
    public int DiasParaCompletar(DateOnly referencia) =>
        referencia > Fim ? 0 : Fim.DayNumber - referencia.DayNumber + 1;
}

/// <summary>
/// Deriva os periodos aquisitivos de um contrato.
///
/// NAO HA TABELA no banco, e isso e decisao registrada, nao esquecimento.
///
/// Um periodo aquisitivo e funcao pura de duas coisas que o sistema JA guarda:
/// a data de admissao e a data de referencia. Nao ha nada nele que alguem
/// altere - ele nasce do calendario. Persistir seria criar linhas cujo unico
/// conteudo e o que o proprio calculo produziria, com o risco extra de
/// divergirem da admissao se ela for corrigida.
///
/// O que TEM estado e a CONCESSAO de ferias - quantos dias foram gozados, em
/// que folha, quando. Isso nao existe ainda: chega na etapa 2, e ai sim vira
/// tabela, apontando para o periodo pelo seu intervalo.
///
/// Funcao pura: sem banco, sem relogio (a referencia entra como parametro),
/// sem HTTP - CLAUDE.md secao 10 e secao 23.
/// </summary>
public static class PeriodosAquisitivos
{
    /// <summary>
    /// Dias de ferias do periodo completo, sem faltas (CLT art. 130, I).
    ///
    /// A REDUCAO POR FALTAS INJUSTIFICADAS NAO E APLICADA, e nao por
    /// esquecimento: o dominio nao tem faltas. Nao existe registro de ausencia
    /// em lugar nenhum do Prisma RH, entao nao ha o que contar. Implementar a
    /// tabela do art. 130 sem a entrada dela seria escrever regra que nunca
    /// dispara - e dar a impressao de que o sistema confere isso.
    ///
    /// O mesmo vale para o regime de tempo parcial (art. 130-A), que tem
    /// tabela propria: o contrato guarda jornada MENSAL, e deduzir a semanal
    /// dela seria suposicao.
    /// </summary>
    public const int DiasPorPeriodoCompleto = 30;

    /// <summary>
    /// Todos os periodos aquisitivos do contrato ate a data de referencia,
    /// do mais antigo para o mais novo.
    ///
    /// O periodo em andamento ENTRA na lista: o analista precisa ver quanto
    /// falta para o proximo direito, e nao so os ja adquiridos.
    ///
    /// Contrato desligado para de gerar periodos na data do desligamento. O
    /// que sobra de periodo incompleto vira ferias PROPORCIONAIS, que sao
    /// verba rescisoria e pertencem a Fase 4G - por isso ele nao aparece aqui
    /// como se fosse um direito de 30 dias.
    /// </summary>
    public static IReadOnlyList<PeriodoAquisitivo> De(ContratoTrabalho contrato, DateOnly referencia)
    {
        ArgumentNullException.ThrowIfNull(contrato);

        // O contrato encerrado nao gera periodo depois do desligamento.
        var ate = contrato.DataDesligamento is { } desligamento && desligamento < referencia
            ? desligamento
            : referencia;

        if (ate < contrato.DataAdmissao)
        {
            return [];
        }

        var periodos = new List<PeriodoAquisitivo>();
        var numero = 1;

        while (true)
        {
            var inicio = contrato.DataAdmissao.AddYears(numero - 1);

            if (inicio > ate)
            {
                break;
            }

            // O fim e a VESPERA do aniversario seguinte. Assim o proximo
            // periodo comeca no aniversario, que e como "12 meses de vigencia"
            // e contado na pratica.
            var fim = contrato.DataAdmissao.AddYears(numero).AddDays(-1);

            periodos.Add(new PeriodoAquisitivo(numero, inicio, fim, DiasPorPeriodoCompleto));

            numero++;
        }

        return periodos;
    }

    /// <summary>
    /// Os periodos que ja deram direito e ainda nao foram gozados, do mais
    /// antigo para o mais novo.
    ///
    /// A ordem importa: quando a etapa 2 conceder ferias, o periodo mais
    /// ANTIGO e o que deve ser baixado primeiro - e o que esta mais perto de
    /// vencer e virar dobra.
    /// </summary>
    public static IReadOnlyList<PeriodoAquisitivo> Adquiridos(
        ContratoTrabalho contrato, DateOnly referencia) =>
        [.. De(contrato, referencia)
            .Where(p => p.SituacaoEm(referencia) != SituacaoPeriodoAquisitivo.EmAndamento)];

    /// <summary>
    /// O periodo em andamento, se houver. Nulo quando o contrato esta
    /// desligado ou quando a referencia e anterior a admissao.
    /// </summary>
    public static PeriodoAquisitivo? EmAndamento(ContratoTrabalho contrato, DateOnly referencia) =>
        De(contrato, referencia)
            .LastOrDefault(p => p.SituacaoEm(referencia) == SituacaoPeriodoAquisitivo.EmAndamento);
}
