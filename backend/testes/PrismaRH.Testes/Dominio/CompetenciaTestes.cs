using PrismaRH.Dominio.Folha;

namespace PrismaRH.Testes.Dominio;

/// <summary>
/// Competencia e o eixo do sistema inteiro: toda folha, todo historico e todo
/// parametro futuro se penduram nela. Erro aqui nao aparece como erro - aparece
/// como uma folha processada no mes errado.
/// </summary>
public class CompetenciaTestes
{
    [Fact]
    public void PrimeiroEUltimoDia_SaoDerivadosDoMes()
    {
        var agosto = new Competencia(2026, 8);

        Assert.Equal(new DateOnly(2026, 8, 1), agosto.PrimeiroDia);
        Assert.Equal(new DateOnly(2026, 8, 31), agosto.UltimoDia);
        Assert.Equal(31, agosto.DiasDoMes);
    }

    [Fact]
    public void Fevereiro_ConheceOAnoBissexto()
    {
        Assert.Equal(new DateOnly(2026, 2, 28), new Competencia(2026, 2).UltimoDia);

        // 2028 e bissexto. Fixar 28 dias para fevereiro faria a folha ignorar
        // o dia 29 de quatro em quatro anos.
        Assert.Equal(new DateOnly(2028, 2, 29), new Competencia(2028, 2).UltimoDia);
    }

    [Fact]
    public void Codigo_OrdenaVirandoOAno()
    {
        var dezembro = new Competencia(2025, 12);
        var janeiro = new Competencia(2026, 1);

        Assert.Equal(202512, dezembro.Codigo);
        Assert.Equal(202601, janeiro.Codigo);
        Assert.True(dezembro < janeiro);
        Assert.Equal(janeiro, dezembro.Proxima());
        Assert.Equal(dezembro, janeiro.Anterior());
    }

    [Fact]
    public void DoCodigo_DesfazOCodigo()
    {
        var original = new Competencia(2026, 8);

        Assert.Equal(original, Competencia.DoCodigo(original.Codigo));
    }

    [Theory]
    [InlineData("08/2026")]
    [InlineData("2026-08")]
    [InlineData(" 08/2026 ")]
    public void TryParse_AceitaOsDoisFormatosUsados(string texto)
    {
        Assert.True(Competencia.TryParse(texto, out var competencia));
        Assert.Equal(new Competencia(2026, 8), competencia);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("agosto")]
    [InlineData("13/2026")]
    [InlineData("2026")]
    [InlineData("08/1999")]
    [InlineData("08/2026/01")]
    public void TryParse_RecusaOResto(string? texto)
    {
        Assert.False(Competencia.TryParse(texto, out _));
    }

    [Fact]
    public void Igualdade_IgnoraODia()
    {
        // O motivo de Competencia existir: 01/08 e 31/08 sao a MESMA
        // competencia. Com DateOnly seriam duas datas diferentes, e a folha
        // de agosto poderia ser aberta duas vezes.
        Assert.Equal(
            Competencia.De(new DateOnly(2026, 8, 1)),
            Competencia.De(new DateOnly(2026, 8, 31)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void Mes_ForaDaFaixa_Recusado(int mes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Competencia(2026, mes));
    }

    [Fact]
    public void Ano_ForaDaFaixa_Recusado()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Competencia(1999, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Competencia(2101, 8));
    }

    [Fact]
    public void ToString_UsaOFormatoQueOBrasilLe()
    {
        Assert.Equal("08/2026", new Competencia(2026, 8).ToString());
        Assert.Equal("01/2026", new Competencia(2026, 1).ToString());
    }
}
