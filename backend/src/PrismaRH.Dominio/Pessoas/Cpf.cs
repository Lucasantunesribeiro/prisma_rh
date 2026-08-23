namespace PrismaRH.Dominio.Pessoas;

/// <summary>
/// CPF validado, guardado sem mascara (somente os 11 digitos).
///
/// Mesmo desenho do Cnpj: ou a instancia existe e o numero e valido, ou o valor
/// foi recusado na porta de entrada. Nao existe CPF invalido circulando.
///
/// Diferente do CNPJ, o CPF nao tem transicao para formato alfanumerico
/// prevista: o algoritmo abaixo vale para todo CPF emitido.
/// </summary>
public readonly record struct Cpf
{
    public const int Tamanho = 11;

    private Cpf(string valor) => Valor = valor;

    /// <summary>Os 11 digitos, sem pontuacao.</summary>
    public string Valor { get; }

    /// <summary>Formato de exibicao: 000.000.000-00</summary>
    public string Formatado =>
        string.IsNullOrEmpty(Valor)
            ? string.Empty
            : $"{Valor[..3]}.{Valor.Substring(3, 3)}.{Valor.Substring(6, 3)}-{Valor.Substring(9, 2)}";

    /// <summary>
    /// Mascara para exibicao em lista: 000.***.**0-00.
    /// CPF e dado pessoal (LGPD): a tela de listagem nao precisa do numero
    /// inteiro para o usuario reconhecer de quem se trata.
    /// </summary>
    public string Mascarado =>
        string.IsNullOrEmpty(Valor)
            ? string.Empty
            : $"{Valor[..3]}.***.**{Valor[8]}-{Valor.Substring(9, 2)}";

    public override string ToString() => Valor;

    public static Cpf Criar(string? entrada)
    {
        if (!TentarCriar(entrada, out var cpf))
        {
            throw new ArgumentException($"CPF invalido: '{entrada}'.", nameof(entrada));
        }

        return cpf;
    }

    public static bool TentarCriar(string? entrada, out Cpf cpf)
    {
        cpf = default;

        if (string.IsNullOrWhiteSpace(entrada))
        {
            return false;
        }

        var digitos = SomenteDigitos(entrada);

        if (digitos.Length != Tamanho || TodosIguais(digitos))
        {
            return false;
        }

        // Primeiro digito: pesos 10..2 sobre os 9 primeiros.
        // Segundo digito: pesos 11..2 sobre os 10 primeiros.
        if (digitos[9] != CalcularDigito(digitos, 9) || digitos[10] != CalcularDigito(digitos, 10))
        {
            return false;
        }

        cpf = new Cpf(digitos);
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
            else if (c is not ('.' or '-' or ' '))
            {
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

    private static char CalcularDigito(string digitos, int quantidade)
    {
        var soma = 0;
        var peso = quantidade + 1;

        for (var i = 0; i < quantidade; i++)
        {
            soma += (digitos[i] - '0') * peso--;
        }

        var resto = soma * 10 % 11;

        return (char)('0' + (resto == 10 ? 0 : resto));
    }
}
