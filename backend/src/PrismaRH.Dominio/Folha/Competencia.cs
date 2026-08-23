using System.Globalization;

namespace PrismaRH.Dominio.Folha;

/// <summary>
/// O mes de referencia de um processamento de folha. Escrito 08/2026.
///
/// E um tipo proprio, e nao uma string nem um DateOnly, porque o CLAUDE.md
/// secao 23 proibe competencia solta espalhada pelo sistema. Uma string
/// aceitaria "8/2026", "2026-08" e "agosto"; um DateOnly obrigaria todo lugar
/// a lembrar que o dia nao importa - e alguem acabaria comparando 01/08 com
/// 31/08 e concluindo que sao competencias diferentes.
///
/// Guarda apenas ano e mes. O primeiro e o ultimo dia sao derivados, nunca
/// armazenados: derivar impede que os tres campos discordem entre si.
/// </summary>
public readonly record struct Competencia : IComparable<Competencia>
{
    public const int AnoMinimo = 2000;
    public const int AnoMaximo = 2100;

    public Competencia(int ano, int mes)
    {
        if (ano is < AnoMinimo or > AnoMaximo)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ano), ano, $"Ano precisa ficar entre {AnoMinimo} e {AnoMaximo}.");
        }

        if (mes is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(mes), mes, "Mes precisa ficar entre 1 e 12.");
        }

        Ano = ano;
        Mes = mes;
    }

    public int Ano { get; }
    public int Mes { get; }

    /// <summary>
    /// Representacao inteira 202608, usada para persistir e ordenar.
    ///
    /// Uma coluna so, ordenavel e indexavel: 202512 &lt; 202601 sem nenhuma
    /// conversao. Duas colunas (ano, mes) exigiriam ORDER BY ano, mes em toda
    /// consulta, e bastaria alguem esquecer o segundo campo para a folha de
    /// janeiro aparecer antes da de dezembro.
    /// </summary>
    public int Codigo => (Ano * 100) + Mes;

    public DateOnly PrimeiroDia => new(Ano, Mes, 1);

    public DateOnly UltimoDia => new(Ano, Mes, DateTime.DaysInMonth(Ano, Mes));

    public int DiasDoMes => DateTime.DaysInMonth(Ano, Mes);

    public static Competencia DoCodigo(int codigo)
    {
        if (codigo is < (AnoMinimo * 100) or > ((AnoMaximo * 100) + 12))
        {
            throw new ArgumentOutOfRangeException(nameof(codigo), codigo, "Codigo de competencia fora da faixa.");
        }

        return new Competencia(codigo / 100, codigo % 100);
    }

    public static Competencia De(DateOnly data) => new(data.Year, data.Month);

    /// <summary>Aceita "08/2026" e "2026-08". Qualquer outra coisa e recusada.</summary>
    public static bool TryParse(string? texto, out Competencia competencia)
    {
        competencia = default;

        if (string.IsNullOrWhiteSpace(texto))
        {
            return false;
        }

        var partes = texto.Trim().Split('/', '-');

        if (partes.Length != 2)
        {
            return false;
        }

        if (!int.TryParse(partes[0], NumberStyles.None, CultureInfo.InvariantCulture, out var primeiro)
            || !int.TryParse(partes[1], NumberStyles.None, CultureInfo.InvariantCulture, out var segundo))
        {
            return false;
        }

        // "08/2026" tem o mes na frente; "2026-08" tem o ano. O numero de
        // quatro digitos desempata sem ambiguidade.
        var (ano, mes) = primeiro > 12 ? (primeiro, segundo) : (segundo, primeiro);

        if (ano is < AnoMinimo or > AnoMaximo || mes is < 1 or > 12)
        {
            return false;
        }

        competencia = new Competencia(ano, mes);
        return true;
    }

    public Competencia Proxima() => Mes == 12 ? new Competencia(Ano + 1, 1) : new Competencia(Ano, Mes + 1);

    public Competencia Anterior() => Mes == 1 ? new Competencia(Ano - 1, 12) : new Competencia(Ano, Mes - 1);

    /// <summary>Esta data cai dentro desta competencia?</summary>
    public bool Contem(DateOnly data) => data.Year == Ano && data.Month == Mes;

    public int CompareTo(Competencia outra) => Codigo.CompareTo(outra.Codigo);

    public static bool operator <(Competencia a, Competencia b) => a.Codigo < b.Codigo;

    public static bool operator >(Competencia a, Competencia b) => a.Codigo > b.Codigo;

    public static bool operator <=(Competencia a, Competencia b) => a.Codigo <= b.Codigo;

    public static bool operator >=(Competencia a, Competencia b) => a.Codigo >= b.Codigo;

    public override string ToString() => $"{Mes:00}/{Ano}";
}
