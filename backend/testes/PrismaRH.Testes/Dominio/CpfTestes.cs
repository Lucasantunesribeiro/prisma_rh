using PrismaRH.Dominio.Pessoas;

namespace PrismaRH.Testes.Dominio;

public class CpfTestes
{
    [Theory]
    [InlineData("11144477735")]
    [InlineData("111.444.777-35")]
    [InlineData("  11144477735  ")]
    public void Criar_AceitaCpfValidoComOuSemMascara(string entrada)
    {
        var cpf = Cpf.Criar(entrada);

        Assert.Equal("11144477735", cpf.Valor);
    }

    [Theory]
    [InlineData("11144477734")]   // segundo digito errado
    [InlineData("11144477725")]   // primeiro digito errado
    public void TentarCriar_RecusaDigitoVerificadorErrado(string entrada)
    {
        Assert.False(Cpf.TentarCriar(entrada, out _));
    }

    [Theory]
    [InlineData("00000000000")]
    [InlineData("11111111111")]
    [InlineData("99999999999")]
    public void TentarCriar_RecusaTodosOsDigitosIguais(string entrada)
    {
        // Passam na conta dos digitos verificadores, mas nao existem.
        Assert.False(Cpf.TentarCriar(entrada, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1114447773")]      // 10 digitos
    [InlineData("111444777351")]    // 12 digitos
    [InlineData("111.444.ABC-35")]  // letras
    public void TentarCriar_RecusaEntradaMalformada(string? entrada)
    {
        Assert.False(Cpf.TentarCriar(entrada, out _));
    }

    [Fact]
    public void Formatado_E_Mascarado_TemPropositosDiferentes()
    {
        var cpf = Cpf.Criar("11144477735");

        Assert.Equal("111.444.777-35", cpf.Formatado);

        // Listagem nao precisa do numero inteiro para identificar a pessoa.
        Assert.Equal("111.***.**7-35", cpf.Mascarado);
        Assert.DoesNotContain("444", cpf.Mascarado);
    }

    [Fact]
    public void Igualdade_ComparaPelosDigitos()
    {
        Assert.Equal(Cpf.Criar("111.444.777-35"), Cpf.Criar("11144477735"));
    }
}
