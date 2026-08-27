namespace PrismaRH.Dominio.Parametros;

/// <summary>
/// Uma faixa da tabela progressiva do INSS: ate quanto ela vai e com que
/// aliquota.
///
/// O limite inferior NAO e guardado: ele e o limite superior da faixa
/// anterior, e a primeira comeca em zero. Guardar os dois abriria espaco para
/// buraco e sobreposicao entre faixas - uma tabela onde a faixa 1 termina em
/// 1.500 e a faixa 2 comeca em 1.600 deixaria 100 reais sem aliquota, e o
/// desconto sairia menor sem nenhum erro aparecer.
/// </summary>
public sealed class FaixaInss
{
    private FaixaInss()
    {
    }

    internal FaixaInss(Guid idTabelaInss, int ordem, decimal limiteSuperior, decimal aliquota)
    {
        if (ordem < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ordem), ordem, "Ordem da faixa comeca em 1.");
        }

        if (limiteSuperior <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limiteSuperior), limiteSuperior, "Limite da faixa precisa ser positivo.");
        }

        if (aliquota <= 0 || aliquota >= 1)
        {
            // Fracao, nao percentual: 7,5% e 0.075. Recusar 7.5 aqui evita o
            // erro classico de esquecer a divisao por 100 e descontar o
            // salario inteiro do funcionario.
            throw new ArgumentOutOfRangeException(
                nameof(aliquota), aliquota,
                "Aliquota e fracao entre 0 e 1 (7,5% = 0.075), nao percentual.");
        }

        Id = Guid.CreateVersion7();
        IdTabelaInss = idTabelaInss;
        Ordem = ordem;
        LimiteSuperior = limiteSuperior;
        Aliquota = aliquota;
    }

    public Guid Id { get; private set; }
    public Guid IdTabelaInss { get; private set; }

    /// <summary>Posicao na tabela, a partir de 1. Define a ordem de aplicacao.</summary>
    public int Ordem { get; private set; }

    /// <summary>Ate quanto esta faixa vai. A ultima faixa define o teto da tabela.</summary>
    public decimal LimiteSuperior { get; private set; }

    /// <summary>Fracao, nao percentual: 7,5% e 0.075.</summary>
    public decimal Aliquota { get; private set; }

    /// <summary>A aliquota como percentual, para exibir na memoria de calculo.</summary>
    public decimal AliquotaPercentual => Aliquota * 100m;
}
