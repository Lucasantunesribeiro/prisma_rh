namespace PrismaRH.Dominio.Importacao;

/// <summary>
/// Leitura de um fluxo com teto de bytes.
///
/// Nasceu dentro do <see cref="LeitorCsv"/> na etapa 1 e saiu de la na etapa 4,
/// quando o leitor de XLSX passou a precisar do MESMO controle. Duplicar teria
/// sido duplicar um controle de seguranca - e controle duplicado e controle que
/// um dia diverge, com um dos dois lados afrouxando sem ninguem notar.
/// </summary>
public static class FluxoComTeto
{
    /// <summary>
    /// Le no maximo <paramref name="teto"/> bytes e diz se o arquivo coube.
    ///
    /// Le em blocos e para de verdade ao ultrapassar o teto - nao usa
    /// `Stream.Length`, que um cliente HTTP pode omitir ou mentir, e nao usa
    /// `CopyTo`, que copiaria o arquivo inteiro antes de alguem conferir o
    /// tamanho.
    ///
    /// Le UM byte alem do teto de proposito: e assim que se distingue "coube
    /// exatamente" de "estourou por pouco".
    /// </summary>
    public static bool Ler(Stream origem, int teto, out byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(origem);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(teto);

        using var memoria = new MemoryStream();

        var bloco = new byte[81_920];
        var total = 0;

        while (true)
        {
            var lidos = origem.Read(bloco, 0, bloco.Length);

            if (lidos == 0)
            {
                break;
            }

            total += lidos;

            if (total > teto)
            {
                bytes = [];
                return false;
            }

            memoria.Write(bloco, 0, lidos);
        }

        bytes = memoria.ToArray();
        return true;
    }
}
