using PrismaRH.Dominio.Contratos;
using PrismaRH.Dominio.DecimoTerceiro;
using PrismaRH.Dominio.Ferias;
using PrismaRH.Dominio.Folha;

namespace PrismaRH.Dominio.Rescisao;

/// <summary>
/// O valor base do FGTS para fins rescisorios, INFORMADO pelo analista.
///
/// FONTE: FGTS Digital - "valor base para fins rescisorios" e o total dos
/// depositos devidos durante o contrato, acrescido da remuneracao das contas
/// vinculadas. O proprio sistema oficial permite informa-lo manualmente quando
/// nao consegue reconstitui-lo.
///
/// POR QUE E ENTRADA, E NAO CALCULO: o saldo real inclui correcao monetaria e
/// juros da conta vinculada na Caixa, que o Prisma RH nao conhece. Ele conhece
/// apenas os depositos que ELE MESMO apurou desde a Fase 4C. Calcular a multa
/// sobre isso daria um numero menor que o devido, e com cara de exato.
///
/// Os depositos conhecidos servem para SUGERIR e para AVISAR quando o valor
/// informado ficar abaixo deles - nunca para substituir.
/// </summary>
public sealed record ValorBaseFgts(decimal Informado, decimal ConhecidoPeloSistema)
{
    /// <summary>
    /// O informado esta abaixo do que o proprio sistema ja depositou?
    ///
    /// Se estiver, ou o valor foi digitado errado, ou falta competencia no
    /// historico. Nos dois casos a multa sairia menor que a devida - por isso
    /// e aviso, e nao recusa: o sistema nao sabe o saldo real e nao pode
    /// afirmar que o analista errou.
    /// </summary>
    public bool AbaixoDoConhecido => Informado < ConhecidoPeloSistema;
}

/// <summary>Uma verba da rescisao: quanto, e a conta que chegou nela.</summary>
public sealed record VerbaRescisoria(
    string Codigo,
    string Nome,
    decimal Valor,
    string Referencia,
    IReadOnlyList<PassoCalculo> Passos);

/// <summary>O que uma rescisao gera - ou por que ela nao pode ser apurada.</summary>
public sealed record Rescisao(
    MotivoDesligamento Motivo,
    DateOnly DataDesligamento,
    decimal SalarioReferencia,
    bool Suportado,
    string? MotivoDoBloqueio,
    string Fonte,
    ApuracaoAvisoPrevio? Aviso,
    ApuracaoFeriasProporcionais? FeriasProporcionais,
    int DiasFeriasVencidas,
    ApuracaoAvos? Avos13,
    ValorBaseFgts? ValorBaseFgts,
    IReadOnlyList<VerbaRescisoria> Verbas)
{
    public decimal Total => Verbas.Sum(v => v.Valor);
}

/// <summary>
/// Apura o que uma rescisao gera.
///
/// SIMULACAO, nao folha: esta etapa responde "quanto esta rescisao vale e por
/// que", sem gerar holerite. A folha de rescisao e a etapa seguinte.
///
/// O 13o PROPORCIONAL aparece em avos, mas NAO vira verba em dinheiro: a Fase
/// 4F esta bloqueada por contradicao entre fontes oficiais sobre quando INSS e
/// IRRF incidem no 13o, e a rescisao herda a mesma duvida. Mostrar os avos e
/// util; converte-los em reais sem resolver aquela pendencia seria contorna-la
/// por outro caminho.
///
/// Funcao pura: sem banco, sem relogio, sem HTTP.
/// </summary>
public static class CalculadoraRescisao
{
    /// <summary>Divisor do mes comercial, o mesmo do resto do produto.</summary>
    public const int Divisor = 30;

    public static Rescisao Apurar(
        ContratoTrabalho contrato,
        decimal salarioReferencia,
        int diasFeriasVencidas,
        ValorBaseFgts? valorBaseFgts)
    {
        ArgumentNullException.ThrowIfNull(contrato);

        if (contrato.DataDesligamento is not { } desligamento
            || contrato.MotivoDesligamento is not { } motivo)
        {
            throw new InvalidOperationException(
                "Contrato ainda nao foi desligado: nao ha rescisao a apurar.");
        }

        if (salarioReferencia < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(salarioReferencia), salarioReferencia, "Salario nao pode ser negativo.");
        }

        if (diasFeriasVencidas < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(diasFeriasVencidas), diasFeriasVencidas, "Dias nao podem ser negativos.");
        }

        var regra = MatrizVerbasRescisorias.De(motivo);

        var proporcionais = AvosFeriasProporcionais.Apurar(contrato, desligamento);
        var avos13 = AvosDecimoTerceiro.Apurar(contrato, desligamento.Year);

        if (!regra.Suportado)
        {
            // Bloqueado: devolve o CONTEXTO - avos, dias, datas - mas nenhuma
            // verba em dinheiro. Quem le precisa entender o que falta, nao so
            // receber um erro.
            return new Rescisao(
                motivo, desligamento, salarioReferencia,
                Suportado: false, regra.MotivoDoBloqueio, regra.Fonte,
                Aviso: null, proporcionais, diasFeriasVencidas, avos13, valorBaseFgts,
                Verbas: []);
        }

        var aviso = AvisoPrevio.Apurar(contrato, desligamento, regra.DevedorDoAviso, regra.AvisoPelaMetade);
        var diario = salarioReferencia / Divisor;
        var verbas = new List<VerbaRescisoria>();

        // ------------------------------------------------------ saldo de salario
        var periodo = MotorCalculoFolha.PeriodoNaCompetencia(contrato, Competencia.De(desligamento));

        if (periodo is { } trecho)
        {
            var dias = trecho.Fim.DayNumber - trecho.Inicio.DayNumber + 1;
            var saldo = Dinheiro.Arredondar(diario * dias);

            verbas.Add(new VerbaRescisoria(
                "SALDO", "Saldo de salario", saldo, $"{dias}/{Divisor}",
                [
                    new("Salario na data do desligamento", Moeda(salarioReferencia), salarioReferencia),
                    new($"{dias} dias trabalhados no mes",
                        $"{Moeda(salarioReferencia)} / {Divisor} x {dias}", saldo),
                ]));
        }

        // ---------------------------------------------------------- aviso previo
        if (aviso.Dias > 0 && regra.DevedorDoAviso == DevedorDoAviso.Empregador)
        {
            var valor = Dinheiro.Arredondar(diario * aviso.Dias);

            var passos = new List<PassoCalculo>
            {
                new($"{aviso.AnosCompletos} ano(s) completo(s) de servico",
                    $"{AvisoPrevio.DiasBase} + {aviso.AnosCompletos} x {AvisoPrevio.DiasPorAno}",
                    aviso.DiasBase + aviso.DiasAcrescidos),
            };

            if (aviso.Reduzido)
            {
                passos.Add(new("Acordo entre as partes: aviso pela metade (CLT art. 484-A)",
                    $"{aviso.DiasBase + aviso.DiasAcrescidos} / 2", aviso.Dias));
            }

            passos.Add(new($"Aviso previo indenizado de {aviso.Dias} dias",
                $"{Moeda(salarioReferencia)} / {Divisor} x {aviso.Dias}", valor));

            verbas.Add(new VerbaRescisoria(
                "AVISO", "Aviso previo indenizado", valor, $"{aviso.Dias} dias", passos));
        }

        // ------------------------------------------------------ ferias vencidas
        if (diasFeriasVencidas > 0)
        {
            var vencidas = Dinheiro.Arredondar(diario * diasFeriasVencidas);
            var terco = Dinheiro.Arredondar(vencidas / 3m);

            verbas.Add(new VerbaRescisoria(
                "FERVEN", "Ferias vencidas", vencidas, $"{diasFeriasVencidas}/{Divisor}",
                [
                    new("Dias de periodos ja adquiridos e nao gozados",
                        $"{diasFeriasVencidas} dias", diasFeriasVencidas),
                    new("Remuneracao", $"{Moeda(salarioReferencia)} / {Divisor} x {diasFeriasVencidas}", vencidas),
                ]));

            verbas.Add(new VerbaRescisoria(
                "FERVEN13", "1/3 sobre ferias vencidas", terco, "1/3",
                [new("Um terco constitucional", $"{Moeda(vencidas)} / 3", terco)]));
        }

        // -------------------------------------------------- ferias proporcionais
        if (regra.FeriasProporcionais && proporcionais is { Avos: > 0 } prop)
        {
            // Um avo vale 1/12 do salario - e nao 30 dias divididos por 12,
            // que daria o mesmo numero por outro caminho e esconderia a regra.
            var valor = Dinheiro.Arredondar(salarioReferencia * prop.Avos / AvosFeriasProporcionais.MesesDoPeriodo);
            var terco = Dinheiro.Arredondar(valor / 3m);

            verbas.Add(new VerbaRescisoria(
                "FERPROP", "Ferias proporcionais", valor, prop.Fracao,
                [
                    new($"Periodo aquisitivo incompleto desde {prop.InicioPeriodo:dd/MM/yyyy}",
                        prop.Fracao, prop.Avos),
                    new("Remuneracao proporcional",
                        $"{Moeda(salarioReferencia)} x {prop.Avos} / {AvosFeriasProporcionais.MesesDoPeriodo}",
                        valor),
                ]));

            verbas.Add(new VerbaRescisoria(
                "FERPROP13", "1/3 sobre ferias proporcionais", terco, "1/3",
                [new("Um terco constitucional", $"{Moeda(valor)} / 3", terco)]));
        }

        // ----------------------------------------------------------- multa FGTS
        if (regra.PercentualMultaFgts > 0 && valorBaseFgts is { } baseFgts)
        {
            var multa = Dinheiro.Arredondar(baseFgts.Informado * regra.PercentualMultaFgts);
            var percentual = regra.PercentualMultaFgts * 100m;

            verbas.Add(new VerbaRescisoria(
                "MULTAFGTS", "Indenizacao compensatoria do FGTS", multa, $"{percentual:0}%",
                [
                    new("Valor base para fins rescisorios, informado",
                        Moeda(baseFgts.Informado), baseFgts.Informado),
                    new($"Indenizacao de {percentual:0}%",
                        $"{Moeda(baseFgts.Informado)} x {percentual:0}%", multa),
                ]));
        }

        return new Rescisao(
            motivo, desligamento, salarioReferencia,
            Suportado: true, MotivoDoBloqueio: null, regra.Fonte,
            aviso, proporcionais, diasFeriasVencidas, avos13, valorBaseFgts,
            verbas);
    }

    private static string Moeda(decimal valor) =>
        valor.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
}
