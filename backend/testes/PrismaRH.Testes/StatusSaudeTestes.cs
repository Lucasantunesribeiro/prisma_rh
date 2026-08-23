using Microsoft.Extensions.Diagnostics.HealthChecks;
using PrismaRH.Api.Saude;

namespace PrismaRH.Testes;

public class StatusSaudeTestes
{
    [Theory]
    [InlineData(HealthStatus.Healthy, StatusSaude.Saudavel)]
    [InlineData(HealthStatus.Degraded, StatusSaude.Degradado)]
    [InlineData(HealthStatus.Unhealthy, StatusSaude.Indisponivel)]
    public void Traduzir_ConverteStatusTecnicoParaVocabularioDoProduto(
        HealthStatus statusTecnico,
        string statusEsperado)
    {
        Assert.Equal(statusEsperado, StatusSaude.Traduzir(statusTecnico));
    }
}
