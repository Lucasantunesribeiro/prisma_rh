namespace PrismaRH.Dominio.Folha;

/// <summary>
/// O ciclo de vida de uma folha.
///
/// O ROADMAP listava cinco estados previstos e permitiu ajustar os finais na
/// especificacao da fase. Ficaram tres:
///
/// - EmCalculo foi descartado porque o calculo da Fase 3 e sincrono: o estado
///   so existiria entre duas linhas de codigo, e nunca seria observavel.
///   Ele volta a fazer sentido na Fase 9, com processamento assincrono.
///
/// - ComInconsistencias foi descartado porque nao ha motor de analises antes
///   da Fase 6. Criar o estado agora seria montar estrutura de fase futura,
///   exatamente o que o ROADMAP secao 0 proibe.
/// </summary>
public enum SituacaoFolha
{
    /// <summary>Aberta, aceita lancamentos e recalculo livre.</summary>
    Rascunho = 1,

    /// <summary>Ja calculada ao menos uma vez. Continua aceitando recalculo.</summary>
    Calculada = 2,

    /// <summary>Fechada. Nao aceita mais lancamento nem recalculo.</summary>
    Fechada = 3,
}
