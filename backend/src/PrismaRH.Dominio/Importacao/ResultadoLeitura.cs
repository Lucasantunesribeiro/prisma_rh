namespace PrismaRH.Dominio.Importacao;

/// <summary>
/// Um problema encontrado na importacao, sempre localizado.
///
/// <paramref name="Linha"/> e o numero da linha NO ARQUIVO, contando o
/// cabecalho como linha 1 - e nao o indice do registro. Quem abre a planilha
/// para corrigir procura pelo numero que o editor mostra na lateral; devolver
/// "registro 7" obrigaria a pessoa a fazer a conta de cabeca.
///
/// Zero significa que o problema e do arquivo inteiro, nao de uma linha.
/// </summary>
public sealed record ErroImportacao(int Linha, string? Coluna, string Mensagem);

/// <summary>Uma linha de dados lida, com o numero que ela tem no arquivo.</summary>
public sealed record LinhaCsv(int Numero, IReadOnlyList<string> Campos);

/// <summary>
/// O que a leitura de um arquivo produziu.
///
/// Erros e linhas convivem de proposito: uma linha malformada no meio do
/// arquivo nao impede ler as demais, e o `ROADMAP.md` pede **relatorio linha a
/// linha**. Abortar no primeiro erro obrigaria a pessoa a corrigir e reenviar
/// uma vez por problema.
///
/// A decisao de GRAVAR ou nao e de quem chama, e a regra do `ROADMAP.md` e
/// clara: importacao invalida nao deixa dado pela metade. Por isso
/// <see cref="Valido"/> existe - e por isso ele exige zero erros, nao "poucos".
/// </summary>
public sealed record ResultadoLeitura(
    IReadOnlyList<string> Cabecalho,
    IReadOnlyList<LinhaCsv> Linhas,
    IReadOnlyList<ErroImportacao> Erros)
{
    public bool Valido => Erros.Count == 0;

    public static ResultadoLeitura Falha(params ErroImportacao[] erros) =>
        new([], [], erros);

    /// <summary>
    /// A posicao de uma coluna no cabecalho, ou nulo se ela nao existe.
    ///
    /// Busca sem diferenciar maiuscula, acento ou espaco em volta: quem monta a
    /// planilha escreve "CPF", "Cpf" ou " cpf ", e recusar por causa disso seria
    /// rigor sem proposito. O CONTEUDO continua validado com todo rigor - o que
    /// se afrouxa aqui e so o nome da coluna.
    /// </summary>
    public int? Coluna(string nome)
    {
        for (var i = 0; i < Cabecalho.Count; i++)
        {
            if (NomesDeColunaIguais(Cabecalho[i], nome))
            {
                return i;
            }
        }

        return null;
    }

    internal static bool NomesDeColunaIguais(string a, string b) =>
        string.Equals(Normalizar(a), Normalizar(b), StringComparison.OrdinalIgnoreCase);

    private static string Normalizar(string valor)
    {
        var semAcento = valor.Trim().Normalize(System.Text.NormalizationForm.FormD);
        var construtor = new System.Text.StringBuilder(semAcento.Length);

        foreach (var caractere in semAcento)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(caractere)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                construtor.Append(caractere);
            }
        }

        return construtor.ToString();
    }
}
