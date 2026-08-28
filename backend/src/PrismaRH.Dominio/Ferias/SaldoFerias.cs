namespace PrismaRH.Dominio.Ferias;

/// <summary>Por que uma concessao foi recusada. Vocabulario fechado.</summary>
public enum RecusaConcessao
{
    /// <summary>Nao ha dias suficientes no periodo.</summary>
    SaldoInsuficiente = 1,

    /// <summary>O abono passa de 1/3 dos dias do periodo (art. 143).</summary>
    AbonoAcimaDoTerco = 2,

    /// <summary>Ja existem tres fracoes de gozo (art. 134, par. 1o).</summary>
    FracoesDemais = 3,

    /// <summary>Nenhuma fracao alcanca 14 dias (art. 134, par. 1o).</summary>
    SemFracaoDeQuatorzeDias = 4,

    /// <summary>Alguma fracao ficaria abaixo de 5 dias (art. 134, par. 1o).</summary>
    FracaoAbaixoDoMinimo = 5,

    /// <summary>O gozo se sobrepoe a outra concessao ja registrada.</summary>
    SobrepoeOutroGozo = 6
}

/// <summary>Um periodo aquisitivo com o que ja foi concedido dele.</summary>
public sealed record PeriodoComSaldo(
    PeriodoAquisitivo Periodo,
    IReadOnlyList<ConcessaoFerias> Concessoes)
{
    public int DiasConcedidos => Concessoes.Sum(c => c.DiasBaixados);

    public int Saldo => Periodo.DiasDireito - DiasConcedidos;

    /// <summary>Dias que ainda podem ser vendidos: 1/3 do periodo menos o que ja foi.</summary>
    public int SaldoAbono =>
        Math.Max(0, (Periodo.DiasDireito / 3) - Concessoes.Sum(c => c.DiasAbonoPecuniario));

    /// <summary>Quantas fracoes de GOZO ja existem. Abono puro nao conta.</summary>
    public int FracoesUsadas => Concessoes.Count(c => c.Dias > 0);

    public bool TotalmenteConcedido => Saldo == 0;
}

/// <summary>
/// As regras que decidem se uma concessao pode ser registrada.
///
/// FONTES (CLAUDE.md secao 29):
/// - CLT art. 134, par. 1o: ate tres periodos, um com no minimo 14 dias
///   corridos e os demais com no minimo 5;
/// - CLT art. 143: conversao de ate um terco em abono pecuniario.
///
/// Funcao pura, sem banco e sem relogio: quem chama traz o periodo, as
/// concessoes ja existentes e a pretendida.
///
/// Devolve a LISTA de recusas, e nao a primeira: quem preenche o formulario
/// merece ver tudo que esta errado de uma vez, e nao descobrir um problema por
/// tentativa.
/// </summary>
public static class RegrasDeConcessao
{
    public static IReadOnlyList<RecusaConcessao> Conferir(
        PeriodoComSaldo periodo,
        int dias,
        int diasAbono,
        DateOnly inicio)
    {
        ArgumentNullException.ThrowIfNull(periodo);

        var recusas = new List<RecusaConcessao>();

        if (dias + diasAbono > periodo.Saldo)
        {
            recusas.Add(RecusaConcessao.SaldoInsuficiente);
        }

        if (diasAbono > periodo.SaldoAbono)
        {
            recusas.Add(RecusaConcessao.AbonoAcimaDoTerco);
        }

        // As fracoes de gozo depois desta concessao entrar.
        var fracoes = periodo.Concessoes
            .Where(c => c.Dias > 0)
            .Select(c => c.Dias)
            .ToList();

        if (dias > 0)
        {
            fracoes.Add(dias);
        }

        if (fracoes.Count > ConcessaoFerias.MaximoFracoes)
        {
            recusas.Add(RecusaConcessao.FracoesDemais);
        }

        if (fracoes.Any(d => d < ConcessaoFerias.DiasMinimosDasDemaisFracoes))
        {
            recusas.Add(RecusaConcessao.FracaoAbaixoDoMinimo);
        }

        // A regra dos 14 dias so vale quando ha FRACIONAMENTO. Uma unica
        // fracao de 30 dias obviamente a cumpre; e enquanto o periodo nao
        // estiver todo concedido, ainda ha espaco para a fracao grande vir
        // depois - cobrar antes impediria de programar 5 dias em janeiro e os
        // outros 25 em julho.
        var saldoDepois = periodo.Saldo - dias - diasAbono;

        if (fracoes.Count > 1
            && saldoDepois == 0
            && !fracoes.Any(d => d >= ConcessaoFerias.DiasMinimosDaMaiorFracao))
        {
            recusas.Add(RecusaConcessao.SemFracaoDeQuatorzeDias);
        }

        if (dias > 0)
        {
            var fim = inicio.AddDays(dias - 1);

            var sobrepoe = periodo.Concessoes
                .Where(c => c.Dias > 0)
                .Any(c => inicio <= c.Fim && fim >= c.Inicio);

            if (sobrepoe)
            {
                recusas.Add(RecusaConcessao.SobrepoeOutroGozo);
            }
        }

        return recusas;
    }

    /// <summary>Mensagem em portugues para cada recusa, para a API devolver.</summary>
    public static string Explicar(RecusaConcessao recusa) => recusa switch
    {
        RecusaConcessao.SaldoInsuficiente =>
            "O periodo aquisitivo nao tem dias suficientes.",
        RecusaConcessao.AbonoAcimaDoTerco =>
            "O abono pecuniario nao pode passar de um terco dos dias do periodo (CLT art. 143).",
        RecusaConcessao.FracoesDemais =>
            "As ferias podem ser divididas em no maximo tres periodos (CLT art. 134, par. 1o).",
        RecusaConcessao.SemFracaoDeQuatorzeDias =>
            "Ao fracionar, um dos periodos precisa ter ao menos 14 dias corridos (CLT art. 134, par. 1o).",
        RecusaConcessao.FracaoAbaixoDoMinimo =>
            "Nenhum periodo de gozo pode ter menos de 5 dias corridos (CLT art. 134, par. 1o).",
        RecusaConcessao.SobrepoeOutroGozo =>
            "Ja existe uma concessao de ferias nesse intervalo.",
        _ => "Concessao invalida."
    };
}
