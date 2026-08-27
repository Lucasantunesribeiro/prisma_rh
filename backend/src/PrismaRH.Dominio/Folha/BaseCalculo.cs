namespace PrismaRH.Dominio.Folha;

/// <summary>
/// Em quais bases de calculo uma rubrica entra.
///
/// INSS, FGTS e IRRF nao incidem sobre o total do holerite: cada um tem sua
/// propria base, formada por um subconjunto das rubricas. Comissao entra na
/// base de INSS; vale-transporte nao. Sem essa distincao, qualquer aliquota
/// aplicada depois estaria sobre o numero errado.
///
/// Enum de bits: cada base vale uma potencia de dois, e uma rubrica que entra
/// em INSS e FGTS guarda 3 numa coluna so. Tres colunas booleanas dariam o
/// mesmo resultado, mas apurar todas as bases exigiria uma linha de codigo por
/// base, e acrescentar a quarta seria migration em duas tabelas.
///
/// Os valores sao explicitos e sao potencias de dois porque o numero vai para
/// o banco e para o JSON. Numerar em sequencia (1, 2, 3) faria o terceiro
/// valor colidir com a combinacao dos dois primeiros, e a consulta passaria a
/// mentir em silencio. O teste EnumDeBases_TodoValorEPotenciaDeDois prova que
/// isso nao aconteceu.
/// </summary>
[Flags]
public enum BaseCalculo
{
    /// <summary>
    /// Nao compoe base alguma. E o padrao de propria: rubrica so entra numa
    /// base quando alguem disser explicitamente que ela entra.
    /// </summary>
    Nenhuma = 0,

    /// <summary>Salario-de-contribuicao do INSS.</summary>
    Inss = 1,

    /// <summary>Base de recolhimento do FGTS.</summary>
    Fgts = 2,

    /// <summary>Base de calculo do imposto de renda retido na fonte.</summary>
    Irrf = 4,
}

/// <summary>
/// As bases individuais, no mesmo arquivo do enum de proposito: quem
/// acrescentar um valor la precisa ver esta lista aqui embaixo. Manter as duas
/// coisas separadas seria convidar a desincronizacao, e o sintoma seria uma
/// base que simplesmente nunca e apurada - sem erro, sem aviso.
/// </summary>
public static class BasesDeCalculo
{
    /// <summary>Na ordem em que aparecem no holerite.</summary>
    public static readonly IReadOnlyList<BaseCalculo> Individuais =
        [BaseCalculo.Inss, BaseCalculo.Fgts, BaseCalculo.Irrf];

    /// <summary>
    /// Recusa bit que nao corresponde a nenhuma base conhecida. Sem isto, um
    /// valor invento vindo do JSON seria gravado no banco e ignorado em
    /// silencio na apuracao.
    /// </summary>
    public static bool Conhecidas(BaseCalculo bases)
    {
        var resto = bases;

        foreach (var individual in Individuais)
        {
            resto &= ~individual;
        }

        return resto == BaseCalculo.Nenhuma;
    }
}
