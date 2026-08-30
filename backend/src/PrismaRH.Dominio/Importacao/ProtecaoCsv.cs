using System.Text;

namespace PrismaRH.Dominio.Importacao;

/// <summary>
/// Escrita segura de CSV.
///
/// ## O ataque que isto impede
///
/// **CSV injection** (ou *formula injection*). O Prisma RH nunca avalia
/// formula - <see cref="LeitorCsv"/> so le texto. Mas o Excel avalia, e um
/// arquivo EXPORTADO pelo sistema e aberto por uma pessoa, na maquina dela.
///
/// Se alguem cadastrar um funcionario chamado `=cmd|'/c calc'!A1` e esse nome
/// sair num CSV, o Excel de quem abrir tenta executar aquilo. O dado atravessou
/// o sistema inteiro como texto inofensivo e virou codigo no destino.
///
/// A defesa e do `CLAUDE.md secao 24.12` e do Security Gate da Fase 5:
/// prefixar celula que comece com `=`, `+`, `-` ou `@`.
///
/// ## Por que apostrofo, e por que ele nao aparece
///
/// O prefixo e o apostrofo: o Excel o entende como "o que vem depois e texto" e
/// **nao o mostra** na celula. Quem abre le o nome como ele e; so a formula
/// deixa de rodar.
///
/// ## Por que o sinal de menos entra na lista
///
/// `-` e o menos comum de lembrar e o mais facil de errar, porque parece numero
/// negativo. Mas `-1+1` e formula para o Excel, e `-2+3+cmd|...` tambem. Um
/// numero negativo de verdade continua saindo certo, porque o apostrofo so e
/// posto quando ha risco - ver <see cref="Escapar"/>.
/// </summary>
public static class ProtecaoCsv
{
    /// <summary>Os quatro caracteres com que uma formula pode comecar.</summary>
    public static readonly char[] IniciosDeFormula = ['=', '+', '-', '@'];

    /// <summary>
    /// Tabulacao e retorno de carro tambem iniciam formula no Excel quando
    /// aparecem antes do sinal de igual. Entram junto porque sao invisiveis -
    /// e o que ninguem enxerga e o que ninguem confere.
    /// </summary>
    private static readonly char[] InvisiveisPerigosos = ['\t', '\r'];

    /// <summary>
    /// Prepara um valor para sair num arquivo CSV.
    ///
    /// Faz as duas coisas que a escrita exige, nesta ordem:
    /// 1. neutraliza formula, com apostrofo;
    /// 2. envolve em aspas quando o valor contem delimitador, aspas ou quebra
    ///    de linha - senao o proprio arquivo sairia malformado.
    /// </summary>
    public static string Escapar(string? valor, char delimitador = LeitorCsv.DelimitadorPadrao)
    {
        if (string.IsNullOrEmpty(valor))
        {
            return string.Empty;
        }

        var seguro = EhFormula(valor) ? "'" + valor : valor;

        var precisaDeAspas =
            seguro.Contains(delimitador)
            || seguro.Contains('"')
            || seguro.Contains('\n')
            || seguro.Contains('\r');

        return precisaDeAspas
            ? "\"" + seguro.Replace("\"", "\"\"") + "\""
            : seguro;
    }

    /// <summary>
    /// Diz se o valor seria interpretado como formula pelo Excel.
    ///
    /// Um numero negativo bem formado - `-1234,56` - NAO e formula, e marca-lo
    /// poria um apostrofo em toda coluna de desconto do sistema. Por isso o
    /// teste olha o que vem depois do sinal: se o resto for numero, e numero.
    /// </summary>
    public static bool EhFormula(string? valor)
    {
        if (string.IsNullOrEmpty(valor))
        {
            return false;
        }

        var primeiro = valor[0];

        if (Array.IndexOf(InvisiveisPerigosos, primeiro) >= 0)
        {
            return true;
        }

        if (Array.IndexOf(IniciosDeFormula, primeiro) < 0)
        {
            return false;
        }

        if (primeiro is '-' or '+')
        {
            var resto = valor[1..];

            if (resto.Length > 0 && resto.All(c => char.IsDigit(c) || c is ',' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Monta uma linha inteira, ja escapada.</summary>
    public static string Linha(
        IEnumerable<string?> campos, char delimitador = LeitorCsv.DelimitadorPadrao)
    {
        ArgumentNullException.ThrowIfNull(campos);

        return string.Join(delimitador, campos.Select(c => Escapar(c, delimitador)));
    }

    /// <summary>
    /// Monta um arquivo CSV completo, com BOM de UTF-8.
    ///
    /// O BOM existe por uma razao pratica: sem ele, o Excel no Windows abre o
    /// arquivo como Latin-1 e todo acento aparece quebrado. Com ele, acerta.
    /// E o mesmo problema que <see cref="LeitorCsv"/> resolve na leitura, do
    /// outro lado.
    /// </summary>
    public static byte[] Arquivo(
        IEnumerable<string> cabecalho,
        IEnumerable<IEnumerable<string?>> linhas,
        char delimitador = LeitorCsv.DelimitadorPadrao)
    {
        ArgumentNullException.ThrowIfNull(cabecalho);
        ArgumentNullException.ThrowIfNull(linhas);

        var texto = new StringBuilder();

        texto.Append(Linha(cabecalho, delimitador)).Append("\r\n");

        foreach (var linha in linhas)
        {
            texto.Append(Linha(linha, delimitador)).Append("\r\n");
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(texto.ToString())];
    }
}
