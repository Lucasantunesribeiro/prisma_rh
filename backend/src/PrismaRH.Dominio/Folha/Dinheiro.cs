namespace PrismaRH.Dominio.Folha;

/// <summary>
/// A politica de arredondamento da folha, num lugar so.
///
/// O CLAUDE.md secao 28 proibe inventar arredondamento espalhado pelo codigo e
/// exige que a regra diga: em qual etapa, quantas casas, qual modo, e se
/// arredonda base, parcela ou resultado. As quatro respostas estao aqui.
///
/// ETAPA: no valor final de cada rubrica, nunca antes. As bases intermediarias
/// (salario vigente, avos) entram na conta com a precisao cheia do decimal.
///
/// CASAS: 2, que e o centavo.
///
/// MODO: AwayFromZero - meio centavo sobe. O padrao do .NET e ToEven
/// ("arredondamento do banqueiro"), que transformaria 0,125 em 0,12 e 0,135 em
/// 0,14. Ninguem confere folha assim, e o funcionario que perde um centavo por
/// causa da paridade do digito anterior nao tem como entender o motivo.
///
/// O QUE: a parcela. Cada rubrica e arredondada; os totais somam parcelas ja
/// arredondadas. Assim o holerite fecha na conta de cabeca - somar a coluna da
/// direita da exatamente o total impresso, sem centavo aparecendo do nada.
/// </summary>
public static class Dinheiro
{
    public const int Casas = 2;

    public static decimal Arredondar(decimal valor) =>
        Math.Round(valor, Casas, MidpointRounding.AwayFromZero);
}
