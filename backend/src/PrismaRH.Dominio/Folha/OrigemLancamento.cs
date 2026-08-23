namespace PrismaRH.Dominio.Folha;

/// <summary>
/// Quem colocou este lancamento na folha.
///
/// Essa distincao e o que torna o reprocessamento seguro: recalcular apaga e
/// refaz o que foi Calculado, e nao encosta no que foi Manual. Sem ela, o
/// analista perderia os lancamentos digitados a cada clique em "calcular".
/// </summary>
public enum OrigemLancamento
{
    Calculado = 1,
    Manual = 2,
}
