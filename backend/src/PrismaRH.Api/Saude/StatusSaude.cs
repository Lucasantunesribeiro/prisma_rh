using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PrismaRH.Api.Saude;

/// <summary>
/// Traduz o status tecnico do ASP.NET Core para o vocabulario em portugues
/// exposto pela API do Prisma RH.
/// </summary>
public static class StatusSaude
{
    public const string Saudavel = "saudavel";
    public const string Degradado = "degradado";
    public const string Indisponivel = "indisponivel";

    public static string Traduzir(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => Saudavel,
        HealthStatus.Degraded => Degradado,
        HealthStatus.Unhealthy => Indisponivel,
        _ => Indisponivel
    };
}
