namespace PrismaRH.Dominio.Workflow;

/// <summary>
/// Em que ponto do tratamento a inconsistencia esta.
///
/// O fluxo e o do `CLAUDE.md secao 12` e do `ROADMAP.md` da Fase 7:
///
/// <code>
/// Detectada ──> EmAnalise ──> Justificada ──┐
///                    │                       ├──> Resolvida
///                    └──> Corrigida ─────────┘
///
/// Resolvida ──> EmAnalise   (reabertura)
/// </code>
///
/// ## Por que dois caminhos ate Resolvida
///
/// Porque sao duas conclusoes diferentes, e confundi-las apagaria a informacao
/// que mais importa depois:
///
/// - **Justificada** = o numero estava certo, e ha um motivo escrito. A folha
///   nao muda.
/// - **Corrigida** = o numero estava errado e alguem arrumou. A folha muda.
///
/// Um unico status "tratada" faria as duas virarem a mesma coisa, e "quantas
/// divergencias eram erro de verdade?" deixaria de ter resposta.
/// </summary>
public enum StatusInconsistencia
{
    /// <summary>Acabou de sair do motor de analises. Ninguem olhou ainda.</summary>
    Detectada = 1,

    /// <summary>Alguem assumiu e esta conferindo.</summary>
    EmAnalise = 2,

    /// <summary>Estava certo, e o motivo esta escrito. Exige justificativa.</summary>
    Justificada = 3,

    /// <summary>Estava errado e foi arrumado.</summary>
    Corrigida = 4,

    /// <summary>Encerrada. Pode ser reaberta.</summary>
    Resolvida = 5,
}

/// <summary>
/// As transicoes permitidas.
///
/// ## Por que uma maquina de estados, e nao um campo que aceita qualquer valor
///
/// O Security Gate da Fase 7 nomeia a ameaca: "transicao de status pulando
/// etapas para esconder pendencia". Sem a maquina, um `PUT status=Resolvida`
/// fecharia qualquer inconsistencia sem passar por analise nem justificativa -
/// e o relatorio de conformidade viraria ficcao.
///
/// A tabela mora no dominio, e nao no endpoint: quem chamar o dominio direto
/// esbarra nela do mesmo jeito.
/// </summary>
public static class TransicoesInconsistencia
{
    private static readonly IReadOnlyDictionary<StatusInconsistencia, StatusInconsistencia[]> Mapa =
        new Dictionary<StatusInconsistencia, StatusInconsistencia[]>
        {
            [StatusInconsistencia.Detectada] = [StatusInconsistencia.EmAnalise],

            [StatusInconsistencia.EmAnalise] =
            [
                StatusInconsistencia.Justificada,
                StatusInconsistencia.Corrigida,
            ],

            // De Justificada ou Corrigida so se avanca para Resolvida, ou se
            // volta para analise quando a conclusao nao se sustentou.
            [StatusInconsistencia.Justificada] =
            [
                StatusInconsistencia.Resolvida,
                StatusInconsistencia.EmAnalise,
            ],

            [StatusInconsistencia.Corrigida] =
            [
                StatusInconsistencia.Resolvida,
                StatusInconsistencia.EmAnalise,
            ],

            // Reabertura: o `ROADMAP.md` pede explicitamente. Uma inconsistencia
            // resolvida por engano precisa poder voltar, e voltar para ANALISE -
            // nao para Detectada, que apagaria o fato de alguem ja ter olhado.
            [StatusInconsistencia.Resolvida] = [StatusInconsistencia.EmAnalise],
        };

    public static IReadOnlyList<StatusInconsistencia> A_partir_de(StatusInconsistencia atual) =>
        Mapa.TryGetValue(atual, out var destinos) ? destinos : [];

    public static bool Permitida(StatusInconsistencia de, StatusInconsistencia para) =>
        A_partir_de(de).Contains(para);

    /// <summary>Status em que a inconsistencia ainda pede trabalho de alguem.</summary>
    public static bool Pendente(StatusInconsistencia status) =>
        status is StatusInconsistencia.Detectada
            or StatusInconsistencia.EmAnalise
            or StatusInconsistencia.Justificada
            or StatusInconsistencia.Corrigida;
}
