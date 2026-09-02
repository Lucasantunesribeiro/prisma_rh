using System.Globalization;

namespace PrismaRH.Dominio.Folha;

/// <summary>
/// O formato numérico brasileiro, **construído à mão**.
///
/// ## O defeito que isto corrige
///
/// ⚠️ Descoberto em **02/09/2026**, usando a produção como usuário: `POST
/// /api/folhas/{id}/calcular` devolvia **500**, e o log dizia
///
/// ```text
/// System.TypeInitializationException: The type initializer for
///   'PrismaRH.Dominio.Folha.MotorCalculoFolha' threw an exception.
/// ---> System.Globalization.CultureNotFoundException: Only the invariant
///   culture is supported in globalization-invariant mode.
/// ```
///
/// A Lambda roda com `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`, porque o
/// runtime `provided.al2023` **não traz o ICU** e carregá-lo engordaria o
/// pacote. Nesse modo, `CultureInfo.GetCultureInfo("pt-BR")` **lança**.
///
/// Nove classes faziam exatamente isso num `static readonly`. Como é
/// inicializador estático, a exceção não acontecia na linha que formata texto —
/// acontecia no **primeiro toque na classe**, derrubando o cálculo inteiro
/// antes da primeira conta.
///
/// **O motor de cálculo nunca funcionou em produção.** A suíte não pegou porque
/// a máquina de desenvolvimento e o runner do CI têm ICU: o mesmo código passa
/// nos dois e falha só onde importa.
///
/// ## Por que a correção é esta, e não "ligar o ICU"
///
/// Duas saídas existiam:
///
/// 1. desligar o modo invariante e embarcar o ICU — resolve, e custa dezenas de
///    megabytes num pacote que precisa caber no limite da Lambda;
/// 2. **não depender de cultura instalada** para produzir vírgula decimal e
///    ponto de milhar.
///
/// A segunda é a certa porque o que o produto precisa aqui é **formatação de
/// texto**, não regra cultural: são duas convenções de pontuação, e elas cabem
/// em cinco linhas. Um `NumberFormatInfo` montado à mão funciona **em modo
/// invariante**, sem ICU, sem pacote maior e sem depender do que está instalado
/// na máquina que executa.
///
/// ⚠️ Isto **não** toca em cálculo. Todo valor monetário é `decimal` e toda
/// conta é feita em `decimal`; cultura aqui decide apenas como o número aparece
/// na frase da memória de cálculo (`CLAUDE.md §22` e `§4.2`).
/// </summary>
public static class FormatoBrasileiro
{
    /// <summary>
    /// Vírgula decimal, ponto de milhar, e `R$` como símbolo — as convenções
    /// brasileiras que o produto usa, sem pedir nada ao sistema operacional.
    /// </summary>
    public static readonly IFormatProvider Numero = Montar();

    private static NumberFormatInfo Montar()
    {
        // Parte da invariante, que existe SEMPRE, inclusive em modo invariante.
        var formato = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();

        formato.NumberDecimalSeparator = ",";
        formato.NumberGroupSeparator = ".";
        formato.NumberGroupSizes = [3];
        formato.NumberDecimalDigits = 2;

        formato.CurrencySymbol = "R$";
        formato.CurrencyDecimalSeparator = ",";
        formato.CurrencyGroupSeparator = ".";
        formato.CurrencyGroupSizes = [3];
        formato.CurrencyDecimalDigits = 2;

        // `R$ 1.234,56` - simbolo, espaco, numero.
        formato.CurrencyPositivePattern = 2;
        formato.CurrencyNegativePattern = 9;

        formato.PercentDecimalSeparator = ",";
        formato.PercentGroupSeparator = ".";
        formato.PercentGroupSizes = [3];

        return NumberFormatInfo.ReadOnly(formato);
    }
}
