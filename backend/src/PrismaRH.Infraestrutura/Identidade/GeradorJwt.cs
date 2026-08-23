using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PrismaRH.Aplicacao.Comum;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Dominio.Identidade;

namespace PrismaRH.Infraestrutura.Identidade;

public sealed class GeradorJwt(IOptions<OpcoesJwt> opcoes, IRelogio relogio) : IGeradorTokens
{
    /// <summary>Nome do claim que carrega a organizacao. Unica fonte de tenant aceita.</summary>
    public const string ClaimOrganizacao = "org";

    public const string ClaimPerfil = "perfil";

    private readonly OpcoesJwt _opcoes = opcoes.Value;

    public TimeSpan DuracaoRefreshToken => TimeSpan.FromDays(_opcoes.DiasRefreshToken);

    public (string Token, DateTimeOffset ExpiraEm) GerarAccessToken(Usuario usuario)
    {
        var agora = relogio.Agora;
        var expiraEm = agora.AddMinutes(_opcoes.MinutosAccessToken);

        var credenciais = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opcoes.ChaveAssinatura)),
            SecurityAlgorithms.HmacSha256);

        Claim[] claims =
        [
            new(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, usuario.Email),
            new(JwtRegisteredClaimNames.Name, usuario.Nome),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
            new(ClaimOrganizacao, usuario.IdOrganizacao.ToString()),
            new(ClaimPerfil, usuario.Perfil.ToString())
        ];

        var token = new JwtSecurityToken(
            issuer: _opcoes.Emissor,
            audience: _opcoes.Audiencia,
            claims: claims,
            notBefore: agora.UtcDateTime,
            expires: expiraEm.UtcDateTime,
            signingCredentials: credenciais);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiraEm);
    }

    /// <summary>
    /// Refresh token e valor opaco aleatorio, nao um JWT: nao precisa carregar
    /// informacao, precisa ser impossivel de adivinhar. 256 bits de
    /// RandomNumberGenerator resolvem isso.
    /// </summary>
    public (string Bruto, string Hash) GerarRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var bruto = Base64UrlEncoder.Encode(bytes);

        return (bruto, HashearRefreshToken(bruto));
    }

    /// <summary>
    /// SHA-256 simples, sem salt nem fator de trabalho - de proposito.
    /// Diferente de senha, este valor tem 256 bits de entropia real: nao ha
    /// dicionario para atacar, e a busca no banco precisa ser por igualdade
    /// exata para usar o indice.
    /// </summary>
    public string HashearRefreshToken(string bruto)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(bruto));
        return Convert.ToHexStringLower(bytes);
    }
}
