namespace PrismaRH.Dominio.Parametros;

/// <summary>
/// Uma faixa da tabela progressiva do IRRF.
///
/// Diferenca ESSENCIAL em relacao a FaixaInss, e o motivo de os dois tipos nao
/// serem um so: aqui existe PARCELA A DEDUZIR.
///
/// O INSS soma trecho a trecho - cada pedaco da base paga a aliquota da sua
/// faixa. O IRRF nao: aplica-se UMA aliquota, a da faixa em que a base caiu,
/// sobre a base INTEIRA, e desconta-se uma parcela fixa que devolve o que foi
/// cobrado a mais nos trechos de baixo. O resultado e equivalente ao calculo
/// trecho a trecho, mas a formula publicada pela Receita e essa - e
/// implementar o IRRF "como o INSS" daria o mesmo numero por acidente hoje e
/// numeros errados assim que a tabela mudasse de forma.
///
/// O limite inferior nao e guardado, pela mesma razao da FaixaInss: e o
/// superior da anterior.
/// </summary>
public sealed class FaixaIrrf
{
    private FaixaIrrf()
    {
    }

    internal FaixaIrrf(
        Guid idTabelaIrrf,
        int ordem,
        decimal? limiteSuperior,
        decimal aliquota,
        decimal parcelaADeduzir)
    {
        if (ordem < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ordem), ordem, "Ordem da faixa comeca em 1.");
        }

        // Nulo e a ULTIMA faixa: acima dela a mesma aliquota segue valendo,
        // porque o IRRF nao tem teto. Um sentinela numerico gigante caberia
        // aqui, mas nao caberia na coluna - e "o maior numero que existe" nao
        // e a mesma afirmacao que "nao ha limite".
        if (limiteSuperior is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limiteSuperior), limiteSuperior, "Limite da faixa precisa ser positivo.");
        }

        // Zero e valido e necessario: a primeira faixa do IRRF e a de
        // ISENCAO. Recusar zero aqui impediria de cadastrar a tabela real.
        if (aliquota < 0 || aliquota >= 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(aliquota), aliquota,
                "Aliquota e fracao entre 0 e 1 (7,5% = 0.075), nao percentual.");
        }

        if (parcelaADeduzir < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(parcelaADeduzir), parcelaADeduzir, "Parcela a deduzir nao pode ser negativa.");
        }

        if (aliquota == 0 && parcelaADeduzir != 0)
        {
            // Faixa isenta com parcela produziria imposto NEGATIVO.
            throw new ArgumentException(
                "Faixa isenta nao tem parcela a deduzir.", nameof(parcelaADeduzir));
        }

        Id = Guid.CreateVersion7();
        IdTabelaIrrf = idTabelaIrrf;
        Ordem = ordem;
        LimiteSuperior = limiteSuperior;
        Aliquota = aliquota;
        ParcelaADeduzir = parcelaADeduzir;
    }

    public Guid Id { get; private set; }
    public Guid IdTabelaIrrf { get; private set; }

    public int Ordem { get; private set; }

    /// <summary>
    /// Ate quanto esta faixa vai. <c>null</c> na ultima: o IRRF nao tem teto,
    /// ao contrario do INSS.
    /// </summary>
    public decimal? LimiteSuperior { get; private set; }

    /// <summary>Fracao, nao percentual. Zero na faixa de isencao.</summary>
    public decimal Aliquota { get; private set; }

    /// <summary>
    /// O que se subtrai depois de aplicar a aliquota sobre a base inteira.
    /// Existe para reproduzir a progressividade sem somar trecho a trecho.
    /// </summary>
    public decimal ParcelaADeduzir { get; private set; }

    public decimal AliquotaPercentual => Aliquota * 100m;

    /// <summary>A ultima faixa, a que nao tem teto.</summary>
    public bool SemTeto => LimiteSuperior is null;

    /// <summary>A base cai nesta faixa?</summary>
    public bool Alcanca(decimal baseCalculo) =>
        LimiteSuperior is not { } limite || baseCalculo <= limite;
}
