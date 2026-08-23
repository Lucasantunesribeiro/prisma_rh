using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Dominio.Identidade;

namespace PrismaRH.Infraestrutura.Identidade;

/// <summary>
/// Contexto padrao para quando nao ha requisicao HTTP: migrations, semeadura,
/// tarefas de fundo, testes de unidade.
///
/// Devolve Guid.Empty, que nao casa com organizacao nenhuma. O sistema falha
/// FECHADO: codigo sem usuario autenticado enxerga nada, em vez de enxergar
/// tudo. A camada Api substitui esta implementacao pela que le os claims.
/// </summary>
public sealed class ContextoSemUsuario : IContextoUsuario
{
    public bool EstaAutenticado => false;
    public Guid IdUsuario => Guid.Empty;
    public Guid IdOrganizacao => Guid.Empty;
    public Perfil Perfil => Perfil.Visualizador;
}
