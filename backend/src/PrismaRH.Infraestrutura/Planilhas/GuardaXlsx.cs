using System.IO.Compression;
using PrismaRH.Dominio.Importacao;

namespace PrismaRH.Infraestrutura.Planilhas;

/// <summary>
/// A conferencia que roda ANTES de a ClosedXML ver o arquivo.
///
/// ## Por que existe uma etapa antes da biblioteca
///
/// Um `.xlsx` e um ZIP de XML. Isso significa que o tamanho do arquivo **nao
/// diz nada** sobre quanta memoria ele vai consumir: 80 KB de zeros comprimidos
/// viram 80 MB descomprimidos, e XML comprime ainda melhor que zeros.
///
/// O teto de 5 MB de <see cref="LimitesImportacao"/> protege a REDE e o disco.
/// Nao protege a memoria - e e a memoria que um *zip bomb* ataca.
///
/// Entregar o arquivo direto para a biblioteca seria confiar que ela se
/// defende. Ela ate se defende de varias coisas, mas o `CLAUDE.md secao 24.25`
/// e explicito: dependencia e superficie de ataque, nao substituta de controle
/// proprio. Aqui o controle e nosso, e ha teste com bomba de verdade.
///
/// ## O que esta classe NAO faz
///
/// Nao extrai nada para disco. Nao interpreta XML. Nao avalia formula. Ela
/// descomprime para um contador e joga os bytes fora - o unico produto e um
/// veredito.
/// </summary>
public static class GuardaXlsx
{
    /// <summary>
    /// A assinatura de todo arquivo ZIP: `PK\x03\x04`.
    ///
    /// As duas primeiras letras sao as iniciais de Phil Katz, autor do formato.
    /// E o unico jeito barato de saber que um arquivo E um zip antes de tentar
    /// abri-lo - e, portanto, de recusar um CSV renomeado para `.xlsx` sem
    /// gastar nada.
    /// </summary>
    public static readonly byte[] AssinaturaZip = [0x50, 0x4B, 0x03, 0x04];

    /// <summary>
    /// Oitenta megabytes descomprimidos, somando o arquivo inteiro.
    ///
    /// O numero vem do caso legitimo, nao do medo: uma planilha de 5 MB com
    /// 10 mil linhas expande para algo entre 30 e 60 MB de XML, porque o XLSX
    /// guarda cada celula como uma tag. Oitenta cobre o caso real com folga e
    /// recusa qualquer coisa que so faz sentido como ataque.
    /// </summary>
    public const long TamanhoMaximoDescomprimido = 80L * 1024 * 1024;

    /// <summary>
    /// Quinhentas entradas.
    ///
    /// Uma planilha normal tem menos de cinquenta partes. Milhares de entradas
    /// minusculas sao a outra forma de bomba: cada uma cabe no teto de tamanho,
    /// e o custo esta na QUANTIDADE.
    /// </summary>
    public const int MaximoEntradas = 500;

    /// <summary>
    /// As partes que todo XLSX de verdade tem.
    ///
    /// E aqui que "validar conteudo real, e nao a extensao" acontece de fato:
    /// um ZIP qualquer renomeado para `.xlsx` passa na assinatura e morre aqui.
    /// </summary>
    private const string ParteTipos = "[Content_Types].xml";
    private const string ParteWorkbook = "xl/workbook.xml";

    /// <summary>
    /// O que denuncia macro.
    ///
    /// `vbaProject.bin` e o codigo VBA compilado. O Prisma RH nunca executa
    /// macro - a ClosedXML sequer sabe executar -, mas recusar o arquivo e
    /// diferente de ignorar a macro: um arquivo com macro chegou aqui por
    /// algum motivo, e aceita-lo em silencio o deixaria seguir viagem para a
    /// maquina da proxima pessoa que o abrisse.
    /// </summary>
    private const string ParteMacro = "vbaproject.bin";

    /// <summary>
    /// Confere o arquivo e devolve o erro, ou nulo quando ele passa.
    ///
    /// Nunca lanca por causa do conteudo: arquivo de usuario e a entrada menos
    /// confiavel que existe, e transformar ZIP corrompido em 500 e o defeito
    /// que o `CLAUDE.md secao 24.19 item 4` ja registra.
    /// </summary>
    public static ErroImportacao? Conferir(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (!PareceZip(bytes))
        {
            return new ErroImportacao(
                0, null,
                "O arquivo nao e uma planilha XLSX. Salve como 'Pasta de Trabalho do Excel'.");
        }

        try
        {
            using var memoria = new MemoryStream(bytes, writable: false);
            using var pacote = new ZipArchive(memoria, ZipArchiveMode.Read);

            return ConferirPacote(pacote);
        }
        catch (InvalidDataException)
        {
            // ZIP truncado, central directory corrompida, arquivo cortado no
            // meio do upload. Tudo isso e "arquivo malformado", que o Security
            // Gate manda tratar sem derrubar o processo.
            return new ErroImportacao(0, null, "Planilha corrompida ou incompleta.");
        }
    }

    /// <summary>So a assinatura, sem abrir nada.</summary>
    public static bool PareceZip(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= AssinaturaZip.Length
        && bytes[..AssinaturaZip.Length].SequenceEqual(AssinaturaZip);

    private static ErroImportacao? ConferirPacote(ZipArchive pacote)
    {
        if (pacote.Entries.Count > MaximoEntradas)
        {
            return new ErroImportacao(
                0, null, $"Planilha com mais de {MaximoEntradas} partes internas.");
        }

        var temTipos = false;
        var temWorkbook = false;

        foreach (var entrada in pacote.Entries)
        {
            var nome = entrada.FullName.Replace('\\', '/');

            // Nada e extraido para disco, entao `../` nao alcanca lugar
            // nenhum. Mas um nome assim nao aparece por acaso num arquivo
            // gerado pelo Excel: e sinal de que o pacote foi montado a mao, e
            // seguir lendo seria ignorar o unico aviso que ele deu.
            if (nome.Contains("..", StringComparison.Ordinal)
                || nome.StartsWith('/')
                || (nome.Length > 1 && nome[1] == ':'))
            {
                return new ErroImportacao(
                    0, null, "Planilha com estrutura interna invalida.");
            }

            if (nome.EndsWith(ParteMacro, StringComparison.OrdinalIgnoreCase))
            {
                return new ErroImportacao(
                    0, null,
                    "Planilha com macro nao e aceita. Salve como .xlsx sem macros.");
            }

            if (string.Equals(nome, ParteTipos, StringComparison.OrdinalIgnoreCase))
            {
                temTipos = true;
            }
            else if (string.Equals(nome, ParteWorkbook, StringComparison.OrdinalIgnoreCase))
            {
                temWorkbook = true;
            }
        }

        if (!temTipos || !temWorkbook)
        {
            return new ErroImportacao(
                0, null,
                "O arquivo e um ZIP, mas nao e uma planilha XLSX.");
        }

        return MedirDescomprimido(pacote);
    }

    /// <summary>
    /// Descomprime de verdade, contando, e para no teto.
    ///
    /// **Nao usa <c>entrada.Length</c>.** Aquele numero vem da central
    /// directory do ZIP, que e escrita por quem montou o arquivo - ou seja,
    /// pelo atacante. Uma bomba pode declarar 1 KB e entregar 1 GB, e conferir
    /// o valor declarado seria perguntar ao suspeito se ele e culpado.
    ///
    /// Os bytes vao para <see cref="Stream.Null"/>: o custo e so o do
    /// algoritmo de descompressao, e nada fica na memoria.
    /// </summary>
    private static ErroImportacao? MedirDescomprimido(ZipArchive pacote)
    {
        var total = 0L;
        var bloco = new byte[81_920];

        foreach (var entrada in pacote.Entries)
        {
            using var fluxo = entrada.Open();

            while (true)
            {
                var lidos = fluxo.Read(bloco, 0, bloco.Length);

                if (lidos == 0)
                {
                    break;
                }

                total += lidos;

                if (total > TamanhoMaximoDescomprimido)
                {
                    var megabytes = TamanhoMaximoDescomprimido / (1024d * 1024d);

                    return new ErroImportacao(
                        0, null,
                        $"A planilha passa de {megabytes:N0} MB ao ser descompactada.");
                }
            }
        }

        return null;
    }
}
