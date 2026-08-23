using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PrismaRH.Infraestrutura;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Testes;

public class ConfiguracaoInfraestruturaTestes
{
    [Fact]
    public void AdicionarInfraestrutura_FalhaComMensagemClaraQuandoNaoHaStringDeConexao()
    {
        var servicos = new ServiceCollection();
        var configuracaoVazia = new ConfigurationBuilder().Build();

        var excecao = Assert.Throws<InvalidOperationException>(
            () => servicos.AdicionarInfraestrutura(configuracaoVazia));

        Assert.Contains(ConfiguracaoInfraestrutura.NomeConexao, excecao.Message);
    }

    [Fact]
    public void AdicionarInfraestrutura_RegistraODbContextQuandoAConfiguracaoEstaCompleta()
    {
        var servicos = new ServiceCollection();
        servicos.AddLogging();

        var configuracao = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{ConfiguracaoInfraestrutura.NomeConexao}"] =
                    FabricaApiTestes.StringConexaoInacessivel
            })
            .Build();

        servicos.AdicionarInfraestrutura(configuracao);

        using var provedor = servicos.BuildServiceProvider();
        Assert.NotNull(provedor.GetRequiredService<PrismaRhDbContext>());
    }
}
