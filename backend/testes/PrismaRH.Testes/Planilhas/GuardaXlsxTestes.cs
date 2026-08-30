using System.Text;
using PrismaRH.Infraestrutura.Planilhas;

namespace PrismaRH.Testes.Planilhas;

/// <summary>
/// A conferencia que roda antes da ClosedXML.
///
/// Cada teste daqui corresponde a um item do Security Gate da Fase 5: *zip
/// bomb*, macro, arquivo malformado, extensao mentindo sobre o conteudo.
/// </summary>
public class GuardaXlsxTestes
{
    [Fact]
    public void PlanilhaDeVerdade_Passa()
    {
        var bytes = FabricaXlsx.Simples(["nome"], ["Ana"]);

        Assert.Null(GuardaXlsx.Conferir(bytes));
    }

    [Fact]
    public void CsvRenomeadoParaXlsx_NaoPassaDaAssinatura()
    {
        var bytes = Encoding.UTF8.GetBytes("nome;cpf\nAna;111.444.777-35\n");

        var erro = GuardaXlsx.Conferir(bytes);

        Assert.NotNull(erro);
        Assert.Contains("nao e uma planilha XLSX", erro.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public void ZipQualquer_NaoEhPlanilha()
    {
        // E um ZIP de verdade - passa na assinatura -, mas nao tem as partes
        // que todo XLSX tem. E aqui que "validar conteudo real" acontece: a
        // assinatura sozinha nao distingue XLSX de .docx, .jar ou .zip.
        var bytes = FabricaXlsx.Pacote(("leiame.txt", Encoding.UTF8.GetBytes("oi")));

        var erro = GuardaXlsx.Conferir(bytes);

        Assert.NotNull(erro);
        Assert.Contains("nao e uma planilha XLSX", erro.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public void PacoteComMacro_EhRecusado()
    {
        var bytes = FabricaXlsx.Pacote([
            .. FabricaXlsx.PartesObrigatorias(),
            ("xl/vbaProject.bin", [1, 2, 3, 4]),
        ]);

        var erro = GuardaXlsx.Conferir(bytes);

        Assert.NotNull(erro);
        Assert.Contains("macro", erro.Mensagem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NomeDeEntradaComCaminhoParaCima_EhRecusado()
    {
        // Nada e extraido para disco, entao `../` nao alcanca lugar nenhum.
        // Mas um nome desses nao aparece por acaso: e o unico aviso que o
        // arquivo deu de que foi montado a mao.
        var bytes = FabricaXlsx.Pacote([
            .. FabricaXlsx.PartesObrigatorias(),
            ("../../etc/passwd", Encoding.UTF8.GetBytes("x")),
        ]);

        var erro = GuardaXlsx.Conferir(bytes);

        Assert.NotNull(erro);
        Assert.Contains("estrutura interna invalida", erro.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public void PacoteComEntradasDemais_EhRecusado()
    {
        var entradas = new List<(string, byte[])>(FabricaXlsx.PartesObrigatorias());

        for (var i = 0; i < GuardaXlsx.MaximoEntradas + 1; i++)
        {
            entradas.Add(($"xl/media/imagem{i}.bin", [0]));
        }

        var erro = GuardaXlsx.Conferir(FabricaXlsx.Pacote([.. entradas]));

        Assert.NotNull(erro);
        Assert.Contains("partes internas", erro.Mensagem, StringComparison.Ordinal);
    }

    /// <summary>
    /// O teste que justifica a classe inteira.
    ///
    /// O arquivo cabe folgado no teto de 5 MB do upload - ele tem alguns
    /// kilobytes. O que nao cabe e o que ele vira quando descompactado.
    /// </summary>
    [Fact]
    public void BombaDeDescompressao_EhRecusadaMesmoCabendoNoTetoDeUpload()
    {
        var bytes = FabricaXlsx.Bomba(GuardaXlsx.TamanhoMaximoDescomprimido + (4 * 1024 * 1024));

        // A prova de que o teto de bytes do upload nao protegeria: o arquivo
        // passa nele com folga.
        Assert.True(bytes.Length < 5 * 1024 * 1024);

        var erro = GuardaXlsx.Conferir(bytes);

        Assert.NotNull(erro);
        Assert.Contains("descompactada", erro.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public void PacoteTruncado_ViraRelatorioENaoExcecao()
    {
        var inteiro = FabricaXlsx.Simples(["nome"], ["Ana"]);
        var cortado = inteiro[..(inteiro.Length / 2)];

        var erro = GuardaXlsx.Conferir(cortado);

        Assert.NotNull(erro);
        Assert.Contains("corrompida", erro.Mensagem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PareceZip_SoOlhaAAssinatura()
    {
        Assert.True(GuardaXlsx.PareceZip([0x50, 0x4B, 0x03, 0x04, 0xFF]));
        Assert.False(GuardaXlsx.PareceZip([0x50, 0x4B]));
        Assert.False(GuardaXlsx.PareceZip(Encoding.UTF8.GetBytes("nome;cpf")));
        Assert.False(GuardaXlsx.PareceZip([]));
    }
}
