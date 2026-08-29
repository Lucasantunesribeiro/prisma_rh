using PrismaRH.Dominio.Contratos;

namespace PrismaRH.Dominio.Rescisao;

/// <summary>
/// O que um motivo de desligamento gera, com a fonte de cada celula.
///
/// <paramref name="Suportado"/> falso significa que o Prisma RH NAO calcula
/// aquele motivo - e o campo <paramref name="MotivoDoBloqueio"/> diz por que.
/// </summary>
public sealed record VerbasDoMotivo(
    MotivoDesligamento Motivo,
    bool Suportado,
    DevedorDoAviso DevedorDoAviso,
    bool AvisoPelaMetade,
    bool FeriasProporcionais,
    decimal PercentualMultaFgts,
    string Fonte,
    string? MotivoDoBloqueio = null);

/// <summary>
/// Quais verbas cada motivo de desligamento gera.
///
/// Esta e a tabela mais perigosa do produto: uma celula errada muda quanto uma
/// pessoa recebe ao perder o emprego. Por isso CADA LINHA cita a fonte, e os
/// motivos sem fonte clara ficam **explicitamente bloqueados** em vez de
/// receberem um palpite.
///
/// FONTES USADAS:
///
/// - **Lei n. 8.036/1990, art. 18, par. 1o**: 40% dos depositos na dispensa
///   sem justa causa;
/// - **Lei n. 8.036/1990, art. 18, par. 2o**: 20% na culpa reciproca e na
///   forca maior reconhecidas pela Justica do Trabalho;
/// - **CLT art. 484-A e par. 1o**: no acordo, aviso previo indenizado e
///   indenizacao do FGTS **pela metade** - ou seja, 20%;
/// - **Lei n. 12.506/2011**: proporcionalidade do aviso previo;
/// - **TST, SDI-1, E-RR-1964-73.2013.5.09.0009**: a proporcionalidade so se
///   exige da EMPRESA;
/// - **Sumula 171 do TST** e **CLT art. 146, par. unico**: ferias
///   proporcionais devidas SALVO na dispensa por justa causa;
/// - **Manual do FGTS Digital**: a multa de 40% alcanca tambem a **dispensa
///   indireta**.
///
/// O QUE NAO E FONTE: analogia. Onde a norma nao alcanca o motivo, ele fica
/// bloqueado - ver os tres casos no fim desta classe.
/// </summary>
public static class MatrizVerbasRescisorias
{
    private static readonly Dictionary<MotivoDesligamento, VerbasDoMotivo> Matriz = new()
    {
        [MotivoDesligamento.DispensaSemJustaCausa] = new(
            MotivoDesligamento.DispensaSemJustaCausa,
            Suportado: true,
            DevedorDoAviso.Empregador,
            AvisoPelaMetade: false,
            FeriasProporcionais: true,
            PercentualMultaFgts: 0.40m,
            Fonte: "Lei 8.036/1990 art. 18 par. 1o; Lei 12.506/2011; Sumula 171 do TST"),

        [MotivoDesligamento.RescisaoIndireta] = new(
            MotivoDesligamento.RescisaoIndireta,
            Suportado: true,
            // Quem deu causa foi o empregador (CLT art. 483), entao e ele quem
            // deve o aviso - com a proporcionalidade da Lei 12.506/2011.
            DevedorDoAviso.Empregador,
            AvisoPelaMetade: false,
            FeriasProporcionais: true,
            PercentualMultaFgts: 0.40m,
            Fonte: "CLT art. 483; Manual do FGTS Digital (a multa de 40% alcanca a dispensa indireta); "
                 + "Lei 12.506/2011; Sumula 171 do TST"),

        [MotivoDesligamento.PedidoDeDemissao] = new(
            MotivoDesligamento.PedidoDeDemissao,
            Suportado: true,
            // Quem avisa e o empregado - e sem proporcionalidade, por decisao
            // da SDI-1: exigir dele o proporcional seria alteracao
            // prejudicial. Trinta dias, e ponto.
            DevedorDoAviso.Empregado,
            AvisoPelaMetade: false,
            // Sumula 171: proporcionais sao devidas SALVO justa causa. Pedido
            // de demissao nao e a excecao.
            FeriasProporcionais: true,
            // O art. 18 lista as hipoteses em que a multa e devida. Pedido de
            // demissao nao esta entre elas: nao ha o que pagar.
            PercentualMultaFgts: 0m,
            Fonte: "TST SDI-1 E-RR-1964-73.2013.5.09.0009 (aviso sem proporcionalidade); "
                 + "Sumula 171 do TST; Lei 8.036/1990 art. 18 (nao alcanca este motivo)"),

        [MotivoDesligamento.DispensaPorJustaCausa] = new(
            MotivoDesligamento.DispensaPorJustaCausa,
            Suportado: true,
            // A falta grave dispensa o aviso: nao ha o que avisar.
            DevedorDoAviso.Ninguem,
            AvisoPelaMetade: false,
            // A UNICA excecao da Sumula 171.
            FeriasProporcionais: false,
            PercentualMultaFgts: 0m,
            Fonte: "CLT art. 482; Sumula 171 do TST (a excecao expressa); "
                 + "Lei 8.036/1990 art. 18 (nao alcanca este motivo)"),

        [MotivoDesligamento.AcordoEntreAsPartes] = new(
            MotivoDesligamento.AcordoEntreAsPartes,
            Suportado: true,
            DevedorDoAviso.Empregador,
            // Art. 484-A, par. 1o, I: aviso previo indenizado pela METADE.
            AvisoPelaMetade: true,
            FeriasProporcionais: true,
            // Art. 484-A, par. 1o, II: indenizacao do FGTS pela metade dos 40%.
            PercentualMultaFgts: 0.20m,
            Fonte: "CLT art. 484-A e par. 1o, I e II; Sumula 171 do TST"),

        // ------------------------------------------------------ bloqueados

        [MotivoDesligamento.TerminoDeContratoPorPrazoDeterminado] = Bloqueado(
            MotivoDesligamento.TerminoDeContratoPorPrazoDeterminado,
            "O dominio nao distingue o TERMINO NORMAL do prazo da RESCISAO ANTECIPADA. "
            + "O termino normal nao gera aviso previo nem multa; a rescisao antecipada gera "
            + "indenizacao propria (CLT art. 479 e 480), que e outra verba. "
            + "Calcular sem essa distincao erraria um dos dois casos sempre."),

        [MotivoDesligamento.FalecimentoDoEmpregado] = Bloqueado(
            MotivoDesligamento.FalecimentoDoEmpregado,
            "Nao ha norma alcancada que diga se a multa do FGTS e devida, e o aviso previo "
            + "perde sentido - nao ha a quem avisar nem quem cumpra. Alem disso as verbas vao "
            + "a dependentes ou herdeiros (Lei 6.858/1980), o que muda a quem se paga, e o "
            + "produto nao tem esse cadastro."),

        [MotivoDesligamento.Aposentadoria] = Bloqueado(
            MotivoDesligamento.Aposentadoria,
            "A aposentadoria espontanea NAO extingue por si o contrato de trabalho, e o "
            + "tratamento das verbas depende do que aconteceu depois dela. Registrar como "
            + "motivo de desligamento nao diz qual dos cenarios ocorreu."),
    };

    private static VerbasDoMotivo Bloqueado(MotivoDesligamento motivo, string razao) =>
        new(motivo, Suportado: false, DevedorDoAviso.Ninguem, false, false, 0m,
            Fonte: "sem fonte oficial alcancada", MotivoDoBloqueio: razao);

    /// <summary>O que este motivo gera.</summary>
    public static VerbasDoMotivo De(MotivoDesligamento motivo) =>
        Matriz.TryGetValue(motivo, out var verbas)
            ? verbas
            : Bloqueado(motivo, "Motivo desconhecido pela matriz.");

    /// <summary>A matriz inteira, para a tela mostrar o que o produto cobre.</summary>
    public static IReadOnlyCollection<VerbasDoMotivo> Todas => Matriz.Values;
}
