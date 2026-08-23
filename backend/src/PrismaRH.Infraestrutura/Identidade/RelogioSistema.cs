using PrismaRH.Aplicacao.Comum;

namespace PrismaRH.Infraestrutura.Identidade;

/// <summary>Relogio real. Sempre UTC: coluna timestamptz do Npgsql recusa outro offset.</summary>
public sealed class RelogioSistema : IRelogio
{
    public DateTimeOffset Agora => DateTimeOffset.UtcNow;
}
