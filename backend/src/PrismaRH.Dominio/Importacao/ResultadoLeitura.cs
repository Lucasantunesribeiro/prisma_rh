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

    /// <summary>
    /// Compara nomes de coluna com a mesma tolerancia usada em <see cref="Coluna"/>.
    ///
    /// Publico desde a etapa 4: o leitor de XLSX mora em outro projeto e
    /// precisa da MESMA comparacao para recusar cabecalho duplicado. Duas
    /// comparacoes parecidas acabariam divergindo num acento.
    /// </summary>
    public static bool NomesDeColunaIguais(string a, string b) =>
        string.Equals(Normalizar(a), Normalizar(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Tira acento **sem depender de ICU**.
    ///
    /// ## O defeito que isto corrige
    ///
    /// ⚠️ Descoberto em **02/09/2026**, rodando a suíte com
    /// `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` — o modo em que a Lambda de
    /// produção roda, porque o runtime `provided.al2023` não traz ICU.
    ///
    /// A versão anterior usava `Normalize(NormalizationForm.FormD)` para separar
    /// a letra do acento e depois descartava as marcas. Isso funciona na máquina
    /// de desenvolvimento e no runner do CI, e **não funciona em produção**: sem
    /// ICU, `Normalize` devolve a string **intacta**, sem lançar. Nada estoura;
    /// a coluna simplesmente não é encontrada.
    ///
    /// O efeito era silencioso e caro: um CSV com cabeçalho `Salário` importava
    /// como se a coluna não existisse. **Falha silenciosa em importação de folha
    /// é pior que erro**, porque o arquivo é aceito e o dado some.
    ///
    /// ## Por que uma tabela, e não a normalização
    ///
    /// O conjunto de letras acentuadas do português é **fechado e pequeno**.
    /// Uma tabela explícita resolve exatamente esse conjunto, se comporta igual
    /// em toda máquina, e não fica na dependência do que está instalado.
    ///
    /// Caractere fora da tabela passa inalterado — o objetivo é casar cabeçalho
    /// de planilha brasileira, não transliterar o Unicode inteiro.
    /// </summary>
    private const string ComAcento = "áàâãäéèêëíìîïóòôõöúùûüçÁÀÂÃÄÉÈÊËÍÌÎÏÓÒÔÕÖÚÙÛÜÇñÑ";
    private const string SemAcento = "aaaaaeeeeiiiiooooouuuucAAAAAEEEEIIIIOOOOOUUUUCnN";

    private static string Normalizar(string valor)
    {
        var recortado = valor.Trim();
        var construtor = new System.Text.StringBuilder(recortado.Length);

        foreach (var caractere in recortado)
        {
            var posicao = ComAcento.IndexOf(caractere, StringComparison.Ordinal);

            construtor.Append(posicao >= 0 ? SemAcento[posicao] : caractere);
        }

        return construtor.ToString();
    }
}
