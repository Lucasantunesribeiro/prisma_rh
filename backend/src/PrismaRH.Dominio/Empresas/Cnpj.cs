namespace PrismaRH.Dominio.Empresas;

/// <summary>
/// CNPJ validado, guardado sem mascara (somente os 14 digitos).
///
/// E um value object: dois CNPJs com os mesmos digitos sao o mesmo CNPJ, e nao
/// existe CNPJ invalido em memoria - ou a instancia foi criada, ou o valor foi
/// recusado na porta de entrada.
///
/// LIMITE CONHECIDO: valida apenas o CNPJ NUMERICO. A Receita Federal iniciou a
/// transicao para CNPJ alfanumerico (12 primeiras posicoes podendo conter
/// letras, com os 2 digitos verificadores ainda numericos). Essa regra NAO foi
/// implementada porque exige fonte oficial confirmada, conforme CLAUDE.md
/// secao 29. Quando for implementar, o unico ponto a mudar e CalcularDigito e o
/// filtro de caracteres em SomenteDigitos.
/// </summary>
public readonly record struct Cnpj
{
    public const int Tamanho = 14;

    private static readonly int[] PesosPrimeiroDigito = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
    private static readonly int[] PesosSegundoDigito = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

    private Cnpj(string valor) => Valor = valor;

    /// <summary>Os 14 digitos, sem pontuacao.</summary>
    public string Valor { get; }

    /// <summary>Formato de exibicao: 00.000.000/0000-00</summary>
    public string Formatado =>
        string.IsNullOrEmpty(Valor)
            ? string.Empty
            : $"{Valor[..2]}.{Valor.Substring(2, 3)}.{Valor.Substring(5, 3)}/{Valor.Substring(8, 4)}-{Valor.Substring(12, 2)}";

    public override string ToString() => Valor;

    public static Cnpj Criar(string? entrada)
    {
        if (!TentarCriar(entrada, out var cnpj))
        {
            throw new ArgumentException($"CNPJ invalido: '{entrada}'.", nameof(entrada));
        }

        return cnpj;
    }

    public static bool TentarCriar(string? entrada, out Cnpj cnpj)
    {
        cnpj = default;

        if (string.IsNullOrWhiteSpace(entrada))
        {
            return false;
        }

        var digitos = SomenteDigitos(entrada);

        if (digitos.Length != Tamanho)
        {
            return false;
        }

        // 00000000000000, 11111111111111 e afins passam na conta dos digitos
        // verificadores, mas nao existem. Precisam ser recusados na mao.
        if (TodosIguais(digitos))
        {
            return false;
        }

        var primeiro = CalcularDigito(digitos, PesosPrimeiroDigito);
        var segundo = CalcularDigito(digitos, PesosSegundoDigito);

        if (digitos[12] != primeiro || digitos[13] != segundo)
        {
            return false;
        }

        cnpj = new Cnpj(digitos);
        return true;
    }

    private static string SomenteDigitos(string entrada)
    {
        Span<char> destino = stackalloc char[entrada.Length];
        var n = 0;

        foreach (var c in entrada)
        {
            if (char.IsAsciiDigit(c))
            {
                destino[n++] = c;
            }
            else if (c is not ('.' or '/' or '-' or ' '))
            {
                // Qualquer outro caractere invalida: melhor recusar do que
                // silenciosamente aceitar "12abc345..." como se fosse numero.
                return string.Empty;
            }
        }

        return new string(destino[..n]);
    }

    private static bool TodosIguais(string digitos)
    {
        for (var i = 1; i < digitos.Length; i++)
        {
            if (digitos[i] != digitos[0])
            {
                return false;
            }
        }

        return true;
    }

    private static char CalcularDigito(string digitos, int[] pesos)
    {
        var soma = 0;

        for (var i = 0; i < pesos.Length; i++)
        {
            soma += (digitos[i] - '0') * pesos[i];
        }

        var resto = soma % 11;
        var digito = resto < 2 ? 0 : 11 - resto;

        return (char)('0' + digito);
    }
}
