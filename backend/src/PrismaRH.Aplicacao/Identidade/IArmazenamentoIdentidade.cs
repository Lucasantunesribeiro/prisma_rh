using PrismaRH.Dominio.Identidade;

namespace PrismaRH.Aplicacao.Identidade;

/// <summary>
/// Porta de persistencia usada pela autenticacao.
///
/// Nao e um Repository generico (CLAUDE.md secao 20): sao exatamente as seis
/// operacoes que o AutenticacaoServico executa, nem uma a mais. A implementacao
/// com EF Core vive na Infraestrutura, mantendo a direcao de dependencia
/// Aplicacao -> Dominio e Infraestrutura -> Aplicacao.
/// </summary>
public interface IArmazenamentoIdentidade
{
    Task<Usuario?> ObterUsuarioPorEmailAsync(string emailNormalizado, CancellationToken ct);

    Task<Usuario?> ObterUsuarioPorIdAsync(Guid idUsuario, CancellationToken ct);

    Task<RefreshToken?> ObterRefreshTokenPorHashAsync(string tokenHash, CancellationToken ct);

    /// <summary>Todos os tokens ainda nao revogados do usuario, para revogar a familia de uma vez.</summary>
    Task<IReadOnlyList<RefreshToken>> ObterTokensNaoRevogadosAsync(Guid idUsuario, CancellationToken ct);

    void AdicionarRefreshToken(RefreshToken token);

    Task SalvarAsync(CancellationToken ct);
}
