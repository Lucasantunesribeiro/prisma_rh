using System.Globalization;

namespace PrismaRH.Dominio.Analises;

/// <summary>O que um parametro de regra aceita.</summary>
public enum TipoParametro
{
    /// <summary>Dinheiro, em reais.</summary>
    Decimal = 1,

    /// <summary>Percentual, de 0 a 100.</summary>
    Percentual = 2,

    /// <summary>Quantidade inteira - dias, ocorrencias.</summary>
    Inteiro = 3,
}

/// <summary>
/// A declaracao de um parametro: nome, tipo, faixa e valor padrao.
///
/// ## Este e o mecanismo que substitui "o usuario escreve a regra"
///
/// O Security Gate da Fase 6 exige que o usuario configure **dentro de tipo e
/// faixa validados**. A definicao mora no CODIGO, junto da regra que a usa - e
/// nao no banco - porque quem sabe o que "percentual maximo de desconto"
/// significa e a regra, nao a tabela.
///
/// Consequencia pratica: nao existe parametro sem dono. Uma chave que a regra
/// nao declarou e recusada em <see cref="ValoresParametros.Interpretar"/>, e
/// nunca chega a virar comportamento.
///
/// E vocabulario fechado, o mesmo padrao do mapeamento de colunas da Fase 5: o
/// cliente escolhe DENTRO do que o servidor declarou.
/// </summary>
public sealed record DefinicaoParametro(
    string Chave,
    string Rotulo,
    string Explicacao,
    TipoParametro Tipo,
    decimal Padrao,
    decimal Minimo,
    decimal Maximo)
{
    /// <summary>
    /// Teto do texto de um valor guardado.
    ///
    /// O valor viaja e e gravado como texto - `numeric` variavel seria uma
    /// coluna por tipo. Trinta caracteres cobrem qualquer decimal do dominio e
    /// impedem que alguem grave um romance na coluna.
    /// </summary>
    public const int TamanhoMaximoValor = 30;

    /// <summary>
    /// Converte e valida um valor recebido de fora.
    ///
    /// Devolve `null` no <c>Erro</c> quando passou.
    ///
    /// **Cultura invariante, sempre.** Aceitar a cultura da maquina faria
    /// `1,5` virar um e meio num servidor e quinze noutro - e os dois numeros
    /// existem, entao ninguem perceberia.
    /// </summary>
    public (decimal Valor, string? Erro) Interpretar(string? texto)
    {
        var limpo = (texto ?? string.Empty).Trim();

        if (limpo.Length == 0)
        {
            return (Padrao, null);
        }

        if (limpo.Length > TamanhoMaximoValor)
        {
            // A mensagem nao ecoa o texto recebido: ele acabaria na tela e no
            // registro da configuracao (`CLAUDE.md secao 24.16`).
            return (Padrao, $"O valor de '{Chave}' e longo demais.");
        }

        if (!decimal.TryParse(limpo, NumberStyles.Number, CultureInfo.InvariantCulture, out var valor))
        {
            return (Padrao, $"O valor de '{Chave}' nao e um numero.");
        }

        if (Tipo == TipoParametro.Inteiro && decimal.Truncate(valor) != valor)
        {
            return (Padrao, $"'{Rotulo}' precisa ser um numero inteiro.");
        }

        if (valor < Minimo || valor > Maximo)
        {
            return (Padrao,
                $"'{Rotulo}' precisa estar entre {Formatar(Minimo)} e {Formatar(Maximo)}.");
        }

        return (valor, null);
    }

    /// <summary>O valor como texto, para gravar e para trafegar.</summary>
    public static string Formatar(decimal valor) =>
        valor.ToString("0.####", CultureInfo.InvariantCulture);
}
