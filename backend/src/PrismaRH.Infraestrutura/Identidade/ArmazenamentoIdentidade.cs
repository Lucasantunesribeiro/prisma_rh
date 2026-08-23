using Microsoft.EntityFrameworkCore;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Dominio.Identidade;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Infraestrutura.Identidade;

/// <summary>
/// Implementacao da porta de identidade com EF Core.
///
/// Todas as consultas usam IgnoreQueryFilters: no momento do login e da
/// renovacao ainda nao existe organizacao no contexto, entao o filtro global
/// por tenant tornaria estas buscas sempre vazias. O isolamento aqui vem do
/// proprio dado - o e-mail e globalmente unico e o hash do token tem 256 bits.
/// </summary>
public sealed class ArmazenamentoIdentidade(PrismaRhDbContext contexto) : IArmazenamentoIdentidade
{
    public Task<Usuario?> ObterUsuarioPorEmailAsync(string emailNormalizado, CancellationToken ct) =>
        contexto.Usuarios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == emailNormalizado, ct);

    public Task<Usuario?> ObterUsuarioPorIdAsync(Guid idUsuario, CancellationToken ct) =>
        contexto.Usuarios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == idUsuario, ct);

    public Task<RefreshToken?> ObterRefreshTokenPorHashAsync(string tokenHash, CancellationToken ct) =>
        contexto.RefreshTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task<IReadOnlyList<RefreshToken>> ObterTokensNaoRevogadosAsync(Guid idUsuario, CancellationToken ct) =>
        await contexto.RefreshTokens
            .IgnoreQueryFilters()
            .Where(t => t.IdUsuario == idUsuario && t.RevogadoEm == null)
            .ToListAsync(ct);

    public void AdicionarRefreshToken(RefreshToken token) => contexto.RefreshTokens.Add(token);

    public Task SalvarAsync(CancellationToken ct) => contexto.SaveChangesAsync(ct);
}
