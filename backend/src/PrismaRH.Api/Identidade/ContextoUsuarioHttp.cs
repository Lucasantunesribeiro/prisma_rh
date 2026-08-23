using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Dominio.Identidade;
using PrismaRH.Infraestrutura.Identidade;

namespace PrismaRH.Api.Identidade;

/// <summary>
/// Le quem esta autenticado a partir dos claims do token JA VALIDADO pelo
/// middleware de autenticacao. Nada aqui vem do corpo, da query string ou de
/// header escolhido pelo cliente.
/// </summary>
public sealed class ContextoUsuarioHttp(IHttpContextAccessor acessor) : IContextoUsuario
{
    private ClaimsPrincipal? Principal => acessor.HttpContext?.User;

    public bool EstaAutenticado => Principal?.Identity?.IsAuthenticated == true;

    public Guid IdUsuario =>
        Guid.TryParse(Ler(JwtRegisteredClaimNames.Sub) ?? Ler(ClaimTypes.NameIdentifier), out var id)
            ? id
            : Guid.Empty;

    /// <summary>
    /// Organizacao do usuario autenticado. Guid.Empty quando nao ha token -
    /// e Guid.Empty nunca casa com organizacao real, entao o filtro global
    /// devolve vazio em vez de vazar dados quando algo der errado.
    /// </summary>
    public Guid IdOrganizacao =>
        Guid.TryParse(Ler(GeradorJwt.ClaimOrganizacao), out var id) ? id : Guid.Empty;

    public Perfil Perfil =>
        Enum.TryParse<Perfil>(Ler(GeradorJwt.ClaimPerfil), ignoreCase: true, out var perfil)
            ? perfil
            : Perfil.Visualizador;

    private string? Ler(string tipo) => Principal?.FindFirst(tipo)?.Value;
}
