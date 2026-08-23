using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Dominio.Empresas;
using PrismaRH.Dominio.Identidade;
using PrismaRH.Infraestrutura.Identidade;
using PrismaRH.Infraestrutura.Persistencia;
using Testcontainers.PostgreSql;

namespace PrismaRH.Testes.Isolamento;

/// <summary>
/// PostgreSQL de verdade, num container descartavel.
///
/// Filtro global testado contra banco falso e teatro: o EF InMemory nem sequer
/// gera SQL. Se o isolamento entre organizacoes vai ser a fronteira de
/// seguranca do produto, ele precisa ser provado onde vai rodar.
/// </summary>
public sealed class BancoPostgresFixture : IAsyncLifetime
{
    // Roda ANTES dos inicializadores de campo da primeira instancia, que e
    // exatamente onde o PostgreSqlBuilder toca a configuracao do Testcontainers.
    static BancoPostgresFixture() => NormalizarDockerHost();

    /// <summary>
    /// Esta maquina tem DOCKER_HOST=npipe:////./pipe/docker_engine, com QUATRO
    /// barras. O CLI do Docker tolera; o Docker.DotNet, que o Testcontainers
    /// usa, nao reconhece como URI de named pipe e falha com "The endpoint is
    /// not a npipe URI".
    ///
    /// A correcao vale so para o processo de teste. A variavel esta persistida
    /// no ambiente do usuario e pode ter sido colocada ali de proposito:
    /// reescreve-la afetaria a maquina inteira.
    /// </summary>
    private static void NormalizarDockerHost()
    {
        var atual = Environment.GetEnvironmentVariable("DOCKER_HOST");

        if (string.IsNullOrWhiteSpace(atual) ||
            !atual.StartsWith("npipe:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Forma canonica tem host preenchido: npipe://./pipe/nome
        if (Uri.TryCreate(atual, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
        {
            return;
        }

        var caminho = atual["npipe:".Length..].TrimStart('/');
        Environment.SetEnvironmentVariable("DOCKER_HOST", "npipe://" + caminho);
    }

    // Mesma imagem do docker-compose: evita baixar outra e evita testar contra
    // uma versao diferente da que roda em desenvolvimento.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("prisma_rh_testes")
        .WithUsername("testes")
        .WithPassword("testes")
        .Build();

    public string StringConexao { get; private set; } = string.Empty;

    // Organizacao A e seus dados.
    public Guid IdOrganizacaoA { get; private set; }
    public Guid IdEmpresaA { get; private set; }
    public Guid IdEstabelecimentoA { get; private set; }

    // Organizacao B: o vizinho que nao pode ser enxergado.
    public Guid IdOrganizacaoB { get; private set; }
    public Guid IdEmpresaB { get; private set; }

    public const string Senha = "SenhaDeTeste#2026";

    public const string EmailAdminA = "admin@a.teste";
    public const string EmailAnalistaA = "analista@a.teste";
    public const string EmailVisualizadorA = "visualizador@a.teste";
    public const string EmailAuditorA = "auditor@a.teste";
    public const string EmailPlataformaA = "plataforma@a.teste";
    public const string EmailAdminB = "admin@b.teste";

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        StringConexao = _container.GetConnectionString();

        await MigrarESemearAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync().AsTask();

    private async Task MigrarESemearAsync()
    {
        var servicos = new ServiceCollection();
        servicos.AddLogging();
        servicos.AddSingleton<IContextoUsuario, ContextoSemUsuario>();
        servicos.AddDbContext<PrismaRhDbContext>(o => o.UseNpgsql(StringConexao));

        using var provedor = servicos.BuildServiceProvider();
        using var escopo = provedor.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PrismaRhDbContext>();

        await db.Database.MigrateAsync();

        var hasheador = new HasheadorSenha();
        var hash = hasheador.Gerar(Senha);
        var agora = DateTimeOffset.UtcNow;

        var orgA = new Organizacao("Organizacao A", agora);
        var orgB = new Organizacao("Organizacao B", agora);
        db.Organizacoes.AddRange(orgA, orgB);

        db.Usuarios.AddRange(
            new Usuario(orgA.Id, "Admin A", EmailAdminA, hash, Perfil.AdministradorEmpresa, agora),
            new Usuario(orgA.Id, "Analista A", EmailAnalistaA, hash, Perfil.AnalistaRh, agora),
            new Usuario(orgA.Id, "Visualizador A", EmailVisualizadorA, hash, Perfil.Visualizador, agora),
            new Usuario(orgA.Id, "Auditor A", EmailAuditorA, hash, Perfil.Auditor, agora),
            new Usuario(orgA.Id, "Plataforma A", EmailPlataformaA, hash, Perfil.AdministradorPlataforma, agora),
            new Usuario(orgB.Id, "Admin B", EmailAdminB, hash, Perfil.AdministradorEmpresa, agora));

        var empresaA = new Empresa(orgA.Id, "Empresa da A", Cnpj.Criar("11222333000181"), agora);
        var empresaB = new Empresa(orgB.Id, "Empresa da B", Cnpj.Criar("11444777000161"), agora);
        db.Empresas.AddRange(empresaA, empresaB);

        var estabA = new Estabelecimento(orgA.Id, empresaA.Id, "001", "Matriz A", agora);
        db.Estabelecimentos.Add(estabA);

        await db.SaveChangesAsync();

        IdOrganizacaoA = orgA.Id;
        IdOrganizacaoB = orgB.Id;
        IdEmpresaA = empresaA.Id;
        IdEmpresaB = empresaB.Id;
        IdEstabelecimentoA = estabA.Id;
    }

    /// <summary>
    /// Contexto usado apenas para migrar e semear. Devolve Guid.Empty, entao
    /// as consultas filtradas veem NADA - o que e o comportamento correto para
    /// codigo sem usuario autenticado. A semeadura funciona porque INSERT nao
    /// passa por filtro de consulta.
    /// </summary>
    private sealed class ContextoSemUsuario : IContextoUsuario
    {
        public bool EstaAutenticado => false;
        public Guid IdUsuario => Guid.Empty;
        public Guid IdOrganizacao => Guid.Empty;
        public Perfil Perfil => Perfil.Visualizador;
    }
}

/// <summary>
/// Todos os testes que sobem a API compartilham esta colecao. Isso os torna
/// SEQUENCIAIS de proposito: as fabricas configuram a string de conexao por
/// variavel de ambiente, que e global ao processo - em paralelo elas
/// sobrescreveriam uma a outra.
/// </summary>
[CollectionDefinition(Nome)]
public sealed class ColecaoApi : ICollectionFixture<BancoPostgresFixture>
{
    public const string Nome = "api";
}
