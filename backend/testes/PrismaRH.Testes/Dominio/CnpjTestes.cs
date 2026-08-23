using PrismaRH.Dominio.Empresas;

namespace PrismaRH.Testes.Dominio;

public class CnpjTestes
{
    [Theory]
    [InlineData("11222333000181")]
    [InlineData("11444777000161")]
    [InlineData("11.222.333/0001-81")]
    [InlineData("  11444777000161  ")]
    public void Criar_AceitaCnpjValidoComOuSemMascara(string entrada)
    {
        var cnpj = Cnpj.Criar(entrada);

        Assert.Equal(Cnpj.Tamanho, cnpj.Valor.Length);
        Assert.All(cnpj.Valor, c => Assert.True(char.IsAsciiDigit(c)));
    }

    [Theory]
    [InlineData("11222333000180")]   // segundo digito verificador errado
    [InlineData("11222333000191")]   // primeiro digito verificador errado
    [InlineData("11444777000160")]   // ultimo digito trocado
    public void TentarCriar_RecusaDigitoVerificadorErrado(string entrada)
    {
        Assert.False(Cnpj.TentarCriar(entrada, out _));
    }

    [Theory]
    [InlineData("00000000000000")]
    [InlineData("11111111111111")]
    [InlineData("99999999999999")]
    public void TentarCriar_RecusaTodosOsDigitosIguais(string entrada)
    {
        // Estes passam na conta dos digitos verificadores mas nao existem.
        // Sem a regra explicita, "00000000000000" entraria como CNPJ valido.
        Assert.False(Cnpj.TentarCriar(entrada, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1122233300018")]      // 13 digitos
    [InlineData("112223330001811")]    // 15 digitos
    [InlineData("11222333ABCD81")]     // letras: alfanumerico ainda nao suportado
    [InlineData("11222333/0001*81")]   // pontuacao nao prevista
    public void TentarCriar_RecusaEntradaMalformada(string? entrada)
    {
        Assert.False(Cnpj.TentarCriar(entrada, out _));
    }

    [Fact]
    public void Criar_LancaComMensagemQueMostraAEntrada()
    {
        var erro = Assert.Throws<ArgumentException>(() => Cnpj.Criar("11222333000180"));

        Assert.Contains("11222333000180", erro.Message);
    }

    [Fact]
    public void Formatado_AplicaMascaraOficial()
    {
        var cnpj = Cnpj.Criar("11222333000181");

        Assert.Equal("11.222.333/0001-81", cnpj.Formatado);
        Assert.Equal("11222333000181", cnpj.Valor);
    }

    [Fact]
    public void Igualdade_ComparaPelosDigitos_NaoPelaFormatacao()
    {
        var comMascara = Cnpj.Criar("11.222.333/0001-81");
        var semMascara = Cnpj.Criar("11222333000181");

        Assert.Equal(comMascara, semMascara);
        Assert.Equal(comMascara.GetHashCode(), semMascara.GetHashCode());
    }
}
