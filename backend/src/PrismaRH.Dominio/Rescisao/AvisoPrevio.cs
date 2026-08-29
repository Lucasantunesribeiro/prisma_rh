using PrismaRH.Dominio.Contratos;

namespace PrismaRH.Dominio.Rescisao;

/// <summary>Quem deve o aviso, e por isso quantos dias sao devidos.</summary>
public enum DevedorDoAviso
{
    /// <summary>Ninguem: o motivo nao gera aviso previo.</summary>
    Ninguem = 0,

    /// <summary>O empregador. Aqui vale a PROPORCIONALIDADE da Lei 12.506/2011.</summary>
    Empregador = 1,

    /// <summary>
    /// O empregado. Aqui NAO vale a proporcionalidade: sao 30 dias fixos.
    /// </summary>
    Empregado = 2
}

/// <summary>O aviso previo apurado, e a conta que chegou nele.</summary>
public sealed record ApuracaoAvisoPrevio(
    DevedorDoAviso Devedor,
    int AnosCompletos,
    int DiasBase,
    int DiasAcrescidos,
    int Dias,
    bool Reduzido);

/// <summary>
/// O aviso previo.
///
/// FONTES (CLAUDE.md secao 29):
///
/// - **Lei n. 12.506, de 11/10/2011**, art. 1o: o aviso e concedido na
///   proporcao de 30 dias a quem tem ate 1 ano de servico na mesma empresa;
/// - **paragrafo unico**: acrescem-se 3 dias por ano de servico na mesma
///   empresa, ate o maximo de 60 dias, perfazendo um total de ate 90;
/// - **TST, SDI-1, E-RR-1964-73.2013.5.09.0009**: a PROPORCIONALIDADE so pode
///   ser exigida da EMPRESA. O empregado que pede demissao deve os 30 dias da
///   CLT, e nao o proporcional - exigir dele seria alteracao prejudicial;
/// - **CLT art. 484-A**: no acordo entre as partes, o aviso previo indenizado
///   e devido PELA METADE.
///
/// Funcao pura: sem banco, sem relogio (a data entra por parametro), sem HTTP.
/// </summary>
public static class AvisoPrevio
{
    /// <summary>Dias devidos a quem tem ate um ano de casa (Lei 12.506, art. 1o).</summary>
    public const int DiasBase = 30;

    /// <summary>Dias acrescidos por ano de servico (paragrafo unico).</summary>
    public const int DiasPorAno = 3;

    /// <summary>Teto do ACRESCIMO, nao do total (paragrafo unico).</summary>
    public const int MaximoAcrescido = 60;

    /// <summary>Teto do total: 30 + 60. E consequencia dos dois acima, nao regra a parte.</summary>
    public const int MaximoTotal = DiasBase + MaximoAcrescido;

    /// <summary>
    /// Apura o aviso previo.
    ///
    /// A proporcionalidade so entra quando quem deve e o EMPREGADOR. Quando
    /// quem deve e o empregado, sao 30 dias fixos - e essa distincao nao e
    /// detalhe: aplicar o proporcional aos dois lados cobraria do trabalhador
    /// um aviso que o TST decidiu que ele nao deve.
    ///
    /// <paramref name="reduzido"/> vale para o acordo do art. 484-A: o aviso
    /// indenizado e devido pela metade. A reducao incide sobre o total ja
    /// apurado, e o resultado e arredondado PARA BAIXO - dia de aviso e
    /// unidade inteira, e meio dia nao existe.
    /// </summary>
    public static ApuracaoAvisoPrevio Apurar(
        ContratoTrabalho contrato,
        DateOnly desligamento,
        DevedorDoAviso devedor,
        bool reduzido = false)
    {
        ArgumentNullException.ThrowIfNull(contrato);

        if (devedor == DevedorDoAviso.Ninguem)
        {
            return new ApuracaoAvisoPrevio(devedor, 0, 0, 0, 0, false);
        }

        var anos = AnosCompletos(contrato.DataAdmissao, desligamento);

        var acrescidos = devedor == DevedorDoAviso.Empregador
            ? Math.Min(anos * DiasPorAno, MaximoAcrescido)
            : 0;

        var total = DiasBase + acrescidos;

        if (reduzido)
        {
            total /= 2;
        }

        return new ApuracaoAvisoPrevio(devedor, anos, DiasBase, acrescidos, total, reduzido);
    }

    /// <summary>
    /// Anos completos de servico entre admissao e desligamento.
    ///
    /// Conta ANIVERSARIOS: quem entrou em 15/03/2020 e saiu em 14/03/2023 tem
    /// DOIS anos completos, nao tres. O terceiro aniversario nao chegou.
    /// </summary>
    public static int AnosCompletos(DateOnly admissao, DateOnly desligamento)
    {
        if (desligamento < admissao)
        {
            return 0;
        }

        var anos = desligamento.Year - admissao.Year;

        if (admissao.AddYears(anos) > desligamento)
        {
            anos--;
        }

        return Math.Max(0, anos);
    }
}
