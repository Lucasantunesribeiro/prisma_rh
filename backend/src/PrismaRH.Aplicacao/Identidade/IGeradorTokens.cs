using PrismaRH.Dominio.Identidade;

namespace PrismaRH.Aplicacao.Identidade;

public interface IGeradorTokens
{
    /// <summary>Access token JWT assinado, de curta duracao.</summary>
    (string Token, DateTimeOffset ExpiraEm) GerarAccessToken(Usuario usuario);

    /// <summary>
    /// Refresh token: devolve o valor bruto (vai para o cookie do navegador) e
    /// o hash (unico que e gravado no banco).
    /// </summary>
    (string Bruto, string Hash) GerarRefreshToken();

    /// <summary>Hash de um refresh token recebido, para procurar no banco.</summary>
    string HashearRefreshToken(string bruto);

    TimeSpan DuracaoRefreshToken { get; }
}
