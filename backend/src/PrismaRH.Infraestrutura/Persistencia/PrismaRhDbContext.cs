using Microsoft.EntityFrameworkCore;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Dominio.Contratos;
using PrismaRH.Dominio.Empresas;
using PrismaRH.Dominio.Folha;
using PrismaRH.Dominio.Identidade;
using PrismaRH.Dominio.Parametros;
using PrismaRH.Dominio.Pessoas;

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
    public DbSet<Funcionario> Funcionarios => Set<Funcionario>();
    public DbSet<Cargo> Cargos => Set<Cargo>();
    public DbSet<ContratoTrabalho> ContratosTrabalho => Set<ContratoTrabalho>();
    public DbSet<VigenciaContrato> VigenciasContrato => Set<VigenciaContrato>();
    public DbSet<Rubrica> Rubricas => Set<Rubrica>();
    public DbSet<FolhaPagamento> Folhas => Set<FolhaPagamento>();
    public DbSet<FolhaFuncionario> FolhasFuncionario => Set<FolhaFuncionario>();
    public DbSet<LancamentoFolha> LancamentosFolha => Set<LancamentoFolha>();
    public DbSet<LinhaMemoriaCalculo> MemoriasCalculo => Set<LinhaMemoriaCalculo>();
    public DbSet<BaseApurada> BasesApuradas => Set<BaseApurada>();

    /// <summary>
    /// Parametros legais federais. NAO tem filtro global de organizacao, e e
    /// a unica excecao do sistema: INSS e lei, vale igual para todos os
    /// tenants, e nao ha dado de ninguem aqui. Ver TabelaInssConfiguracao.
    /// </summary>
    public DbSet<TabelaInss> TabelasInss => Set<TabelaInss>();
    public DbSet<FaixaInss> FaixasInss => Set<FaixaInss>();

    /// <summary>Organizacao do token atual. Exposta para os filtros e para diagnostico.</summary>
    public Guid IdOrganizacaoAtual => contextoUsuario.IdOrganizacao;

    protected override void OnModelCreating(ModelBuilder construtor)
    {
        // Toda entidade recebe ValueGeneratedNever() no Id: as chaves sao
        // atribuidas pelo DOMINIO com Guid.CreateVersion7(), nunca pelo banco.
        //
        // Sem isso o EF assume, por convencao, que chave Guid e gerada pelo
        // banco - e ao encontrar uma entidade nova dentro de um grafo ja
        // rastreado COM a chave preenchida, conclui que ela ja existe e emite
        // UPDATE em vez de INSERT. O sintoma e um
        // DbUpdateConcurrencyException dizendo "esperava 1 linha, afetou 0",
        // que nao aponta em nada para a causa.
        construtor.ApplyConfigurationsFromAssembly(typeof(PrismaRhDbContext).Assembly);

        // O filtro referencia a PROPRIEDADE do contexto, nao o valor. O EF
        // reavalia a cada consulta e transforma em parametro do SQL; capturar
        // o valor aqui congelaria a organizacao da primeira requisicao para
        // todas as seguintes.
        construtor.Entity<Organizacao>().HasQueryFilter(o => o.Id == IdOrganizacaoAtual);
        construtor.Entity<Usuario>().HasQueryFilter(u => u.IdOrganizacao == IdOrganizacaoAtual);
        construtor.Entity<Empresa>().HasQueryFilter(e => e.IdOrganizacao == IdOrganizacaoAtual);
        construtor.Entity<Estabelecimento>().HasQueryFilter(e => e.IdOrganizacao == IdOrganizacaoAtual);
        construtor.Entity<Funcionario>().HasQueryFilter(f => f.IdOrganizacao == IdOrganizacaoAtual);
        construtor.Entity<Cargo>().HasQueryFilter(c => c.IdOrganizacao == IdOrganizacaoAtual);
        construtor.Entity<ContratoTrabalho>().HasQueryFilter(c => c.IdOrganizacao == IdOrganizacaoAtual);
        construtor.Entity<VigenciaContrato>().HasQueryFilter(v => v.IdOrganizacao == IdOrganizacaoAtual);
        construtor.Entity<Rubrica>().HasQueryFilter(r => r.IdOrganizacao == IdOrganizacaoAtual);
        construtor.Entity<FolhaPagamento>().HasQueryFilter(f => f.IdOrganizacao == IdOrganizacaoAtual);
        construtor.Entity<FolhaFuncionario>().HasQueryFilter(f => f.IdOrganizacao == IdOrganizacaoAtual);
        construtor.Entity<LancamentoFolha>().HasQueryFilter(l => l.IdOrganizacao == IdOrganizacaoAtual);
        construtor.Entity<LinhaMemoriaCalculo>().HasQueryFilter(m => m.IdOrganizacao == IdOrganizacaoAtual);
        construtor.Entity<BaseApurada>().HasQueryFilter(b => b.IdOrganizacao == IdOrganizacaoAtual);

        // RefreshToken NAO entra: e lido antes de existir usuario autenticado.
        // Ver o comentario em RefreshToken.cs.

        base.OnModelCreating(construtor);
    }
}
