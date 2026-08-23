using Microsoft.EntityFrameworkCore;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Dominio.Empresas;
using PrismaRH.Dominio.Identidade;

namespace PrismaRH.Infraestrutura.Persistencia;

/// <summary>
/// Contexto de persistencia do Prisma RH.
///
/// AQUI ESTA A FRONTEIRA ENTRE OS CLIENTES. Todo tipo que pertence a uma
/// organizacao ganha um filtro global: a consulta ja nasce restrita ao tenant
/// do token, e nao existe filtro escrito a mao para alguem esquecer.
///
/// Duas consequencias que precisam ficar explicitas:
///
/// 1. Sem usuario autenticado, IdOrganizacao e Guid.Empty, que nao casa com
///    organizacao nenhuma. O sistema falha FECHADO: devolve vazio em vez de
///    devolver tudo.
/// 2. Quem precisa atravessar a fronteira - Administrador da Plataforma,
///    login, semeadura - tem que escrever IgnoreQueryFilters() de forma
///    explicita. Atravessar sem querer deixa de ser possivel.
/// </summary>
public sealed class PrismaRhDbContext(
    DbContextOptions<PrismaRhDbContext> opcoes,
    IContextoUsuario contextoUsuario) : DbContext(opcoes)
{
    public DbSet<Organizacao> Organizacoes => Set<Organizacao>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Empresa> Empresas => Set<Empresa>();
    public DbSet<Estabelecimento> Estabelecimentos => Set<Estabelecimento>();

    /// <summary>Organizacao do token atual. Exposta para os filtros e para diagnostico.</summary>
    public Guid IdOrganizacaoAtual => contextoUsuario.IdOrganizacao;

    protected override void OnModelCreating(ModelBuilder construtor)
    {
        construtor.ApplyConfigurationsFromAssembly(typeof(PrismaRhDbContext).Assembly);

        // O filtro referencia a PROPRIEDADE do contexto, nao o valor. O EF
        // reavalia a cada consulta e transforma em parametro do SQL; capturar
        // o valor aqui congelaria a organizacao da primeira requisicao para
        // todas as seguintes.
        construtor.Entity<Organizacao>().HasQueryFilter(o => o.Id == IdOrganizacaoAtual);
        construtor.Entity<Usuario>().HasQueryFilter(u => u.IdOrganizacao == IdOrganizacaoAtual);
        construtor.Entity<Empresa>().HasQueryFilter(e => e.IdOrganizacao == IdOrganizacaoAtual);
        construtor.Entity<Estabelecimento>().HasQueryFilter(e => e.IdOrganizacao == IdOrganizacaoAtual);

        // RefreshToken NAO entra: e lido antes de existir usuario autenticado.
        // Ver o comentario em RefreshToken.cs.

        base.OnModelCreating(construtor);
    }
}
