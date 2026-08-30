using System.IO.Compression;
using System.Text;
using ClosedXML.Excel;

namespace PrismaRH.Testes.Planilhas;

/// <summary>
/// Monta os arquivos que os testes de planilha precisam - inclusive os que
/// nenhum programa honesto produziria.
/// </summary>
internal static class FabricaXlsx
{
    /// <summary>Uma planilha de verdade, montada com a propria ClosedXML.</summary>
    internal static byte[] Planilha(Action<IXLWorksheet> montar, string nome = "Funcionarios")
    {
        using var pasta = new XLWorkbook();
        var planilha = pasta.AddWorksheet(nome);

        montar(planilha);

        using var memoria = new MemoryStream();
        pasta.SaveAs(memoria);

        return memoria.ToArray();
    }

    /// <summary>Uma planilha com cabecalho e linhas, tudo como texto.</summary>
    internal static byte[] Simples(IEnumerable<string> cabecalho, params string[][] linhas)
    {
        var colunas = cabecalho.ToArray();

        return Planilha(planilha =>
        {
            for (var c = 0; c < colunas.Length; c++)
            {
                planilha.Cell(1, c + 1).SetValue(colunas[c]);
            }

            for (var l = 0; l < linhas.Length; l++)
            {
                for (var c = 0; c < linhas[l].Length; c++)
                {
                    planilha.Cell(l + 2, c + 1).SetValue(linhas[l][c]);
                }
            }
        });
    }

    /// <summary>
    /// Um ZIP montado a mao, entrada por entrada.
    ///
    /// Serve para os arquivos que a ClosedXML nunca geraria: pacote com macro,
    /// nome com `../`, parte obrigatoria faltando, bomba de descompressao.
    /// </summary>
    internal static byte[] Pacote(params (string Nome, byte[] Conteudo)[] entradas)
    {
        using var memoria = new MemoryStream();

        using (var pacote = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (nome, conteudo) in entradas)
            {
                using var fluxo = pacote.CreateEntry(nome, CompressionLevel.SmallestSize).Open();

                fluxo.Write(conteudo);
            }
        }

        return memoria.ToArray();
    }

    /// <summary>As duas partes que fazem um ZIP parecer um XLSX.</summary>
    internal static (string Nome, byte[] Conteudo)[] PartesObrigatorias() =>
    [
        ("[Content_Types].xml", Encoding.UTF8.GetBytes("<Types/>")),
        ("xl/workbook.xml", Encoding.UTF8.GetBytes("<workbook/>")),
    ];

    /// <summary>
    /// Uma bomba de descompressao de verdade.
    ///
    /// Zeros comprimem em torno de 1000:1, entao alguns megabytes declarados
    /// cabem em poucos kilobytes de arquivo - que e exatamente o que torna o
    /// teto de bytes do upload inutil contra este ataque.
    ///
    /// Escrito em blocos: o teste nao aloca os megabytes de uma vez, porque
    /// derrubar a suite de testes por causa do teste da bomba seria ironico
    /// demais.
    /// </summary>
    internal static byte[] Bomba(long bytesDescomprimidos)
    {
        using var memoria = new MemoryStream();

        using (var pacote = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (nome, conteudo) in PartesObrigatorias())
            {
                using var parte = pacote.CreateEntry(nome, CompressionLevel.SmallestSize).Open();

                parte.Write(conteudo);
            }

            using var fluxo = pacote
                .CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.SmallestSize)
                .Open();

            var bloco = new byte[1024 * 1024];
            var restante = bytesDescomprimidos;

            while (restante > 0)
            {
                var pedaco = (int)Math.Min(bloco.Length, restante);

                fluxo.Write(bloco, 0, pedaco);
                restante -= pedaco;
            }
        }

        return memoria.ToArray();
    }
}
