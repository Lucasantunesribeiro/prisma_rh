using PrismaRH.Dominio.Identidade;

namespace PrismaRH.Aplicacao.Identidade;

/// <summary>
/// Quem esta fazendo a requisicao, segundo o token assinado pelo servidor.
///
/// Estes valores NUNCA podem vir do corpo, da query string ou de header: sao
/// lidos dos claims do JWT. E a unica fonte de IdOrganizacao aceita pelo
/// sistema (CLAUDE.md secao 5).
/// </summary>
public interface IContextoUsuario
{
    bool EstaAutenticado { get; }

    Guid IdUsuario { get; }

    Guid IdOrganizacao { get; }

    Perfil Perfil { get; }
}
