using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Dominio.Empresas;
using PrismaRH.Dominio.Identidade;
using PrismaRH.Dominio.Parametros;
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

    public Guid IdOrganizacaoC { get; private set; }
    public Guid IdEmpresaC { get; private set; }
    public Guid IdEstabelecimentoC { get; private set; }

    public Guid IdOrganizacaoD { get; private set; }
    public Guid IdEmpresaD { get; private set; }
    public Guid IdEstabelecimentoD { get; private set; }

    public Guid IdOrganizacaoE { get; private set; }
    public Guid IdEmpresaE { get; private set; }
    public Guid IdEstabelecimentoE { get; private set; }

    public const string Senha = "SenhaDeTeste#2026";

    public const string EmailAdminA = "admin@a.teste";
    public const string EmailAnalistaA = "analista@a.teste";
    public const string EmailVisualizadorA = "visualizador@a.teste";
    public const string EmailAuditorA = "auditor@a.teste";
    public const string EmailPlataformaA = "plataforma@a.teste";
    public const string EmailAdminB = "admin@b.teste";

    /// <summary>
    /// Organizacao C existe so para os testes de INSS.
    ///
    /// Configurar INSS e um fato da ORGANIZACAO: a partir do momento em que
    /// existe rubrica de INSS ativa, toda folha dela passa a descontar. Se os
    /// testes de INSS criassem essa rubrica na organizacao A, os testes das
    /// Fases 3 e 4A - que somam liquido sem encargo - passariam ou falhariam
    /// conforme a ORDEM de execucao. Separar por dados e o que torna os dois
    /// conjuntos deterministicos.
    /// </summary>
    public const string EmailAdminC = "admin@c.teste";

    /// <summary>
    /// Organizacao D existe so para os testes de FGTS, pela mesma razao da C.
    ///
    /// A rubrica de FGTS ativa acrescenta UMA LINHA a todo holerite da
    /// organizacao. Ela nao mexe no liquido - FGTS e do empregador -, mas
    /// FolhaMensalTestes conta lancamentos (Assert.Single, Assert.Equal(2)).
    /// Ligar FGTS na organizacao A faria aqueles testes falharem conforme a
    /// ORDEM de execucao, e o defeito pareceria estar na Fase 3.
    /// </summary>
    public const string EmailAdminD = "admin@d.teste";

    /// <summary>
    /// Organizacao E existe so para os testes de IRRF, pela mesma razao da C e
    /// da D - e aqui o motivo e mais forte: o IRRF e DESCONTO. Liga-lo na
    /// organizacao A mudaria o LIQUIDO de todo holerite dela, e os testes das
    /// Fases 3, 4A e 4C passariam a falhar conforme a ordem de execucao.
    /// </summary>
    public const string EmailAdminE = "admin@e.teste";

    /// <summary>
    /// CPF valido e unico por semente. Os testes desta colecao compartilham o
    /// MESMO banco: reaproveitar um CPF entre dois testes esbarra no indice
    /// unico e faz o segundo falhar por colisao, nao por defeito real.
    /// </summary>
    public static string CpfDeTeste(int semente)
    {
        var noveDigitos = (100_000_000 + semente * 7_919 % 800_000_000).ToString("D9");
        var comPrimeiro = noveDigitos + Digito(noveDigitos, 9);
        return comPrimeiro + Digito(comPrimeiro, 10);

        static char Digito(string digitos, int quantidade)
        {
            var soma = 0;
            var peso = quantidade + 1;

            for (var i = 0; i < quantidade; i++)
            {
                soma += (digitos[i] - '0') * peso--;
            }

            var resto = soma * 10 % 11;
            return (char)('0' + (resto == 10 ? 0 : resto));
        }
    }

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
        var orgC = new Organizacao("Organizacao C", agora);
        var orgD = new Organizacao("Organizacao D", agora);
        var orgE = new Organizacao("Organizacao E", agora);
        db.Organizacoes.AddRange(orgA, orgB, orgC, orgD, orgE);

        db.Usuarios.AddRange(
            new Usuario(orgA.Id, "Admin A", EmailAdminA, hash, Perfil.AdministradorEmpresa, agora),
            new Usuario(orgA.Id, "Analista A", EmailAnalistaA, hash, Perfil.AnalistaRh, agora),
            new Usuario(orgA.Id, "Visualizador A", EmailVisualizadorA, hash, Perfil.Visualizador, agora),
            new Usuario(orgA.Id, "Auditor A", EmailAuditorA, hash, Perfil.Auditor, agora),
            new Usuario(orgA.Id, "Plataforma A", EmailPlataformaA, hash, Perfil.AdministradorPlataforma, agora),
            new Usuario(orgB.Id, "Admin B", EmailAdminB, hash, Perfil.AdministradorEmpresa, agora),
            new Usuario(orgC.Id, "Admin C", EmailAdminC, hash, Perfil.AdministradorEmpresa, agora),
            new Usuario(orgD.Id, "Admin D", EmailAdminD, hash, Perfil.AdministradorEmpresa, agora),
            new Usuario(orgE.Id, "Admin E", EmailAdminE, hash, Perfil.AdministradorEmpresa, agora));

        var empresaA = new Empresa(orgA.Id, "Empresa da A", Cnpj.Criar("11222333000181"), agora);
        var empresaB = new Empresa(orgB.Id, "Empresa da B", Cnpj.Criar("11444777000161"), agora);
        var empresaC = new Empresa(orgC.Id, "Empresa da C", Cnpj.Criar("34028316000103"), agora);
        var empresaD = new Empresa(orgD.Id, "Empresa da D", Cnpj.Criar("60746948000112"), agora);
        var empresaE = new Empresa(orgE.Id, "Empresa da E", Cnpj.Criar("33000167000101"), agora);
        db.Empresas.AddRange(empresaA, empresaB, empresaC, empresaD, empresaE);

        var estabA = new Estabelecimento(orgA.Id, empresaA.Id, "001", "Matriz A", agora);
        var estabC = new Estabelecimento(orgC.Id, empresaC.Id, "001", "Matriz C", agora);
        var estabD = new Estabelecimento(orgD.Id, empresaD.Id, "001", "Matriz D", agora);
        var estabE = new Estabelecimento(orgE.Id, empresaE.Id, "001", "Matriz E", agora);
        db.Estabelecimentos.AddRange(estabA, estabC, estabD, estabE);

        // Parametro legal FEDERAL: nao pertence a organizacao alguma, entao
        // entra uma vez so e vale para as duas. Mesma tabela da semeadura de
        // desenvolvimento - os testes de INSS conferem os valores reais.
        db.TabelasInss.Add(new TabelaInss(
            new DateOnly(2026, 1, 1),
            "Portaria Interministerial MPS/MF n. 13, de 09/01/2026, Anexo II",
            [
                (1621.00m, 0.075m),
                (2902.84m, 0.09m),
                (4354.27m, 0.12m),
                (8475.55m, 0.14m),
            ],
            agora));

        // Tambem federal: uma aliquota de FGTS desde 1990 (Lei 8.036/90,
        // art. 15). Os testes de FGTS conferem o valor real.
        db.TabelasFgts.Add(new TabelaFgts(
            new DateOnly(1990, 5, 11),
            0.08m,
            "Lei n. 8.036, de 11/05/1990, art. 15 - deposito mensal de 8% da remuneracao",
            agora));

        // Tambem federal: a tabela de IRRF de 2026, com o redutor da Lei
        // 15.270/2025. Os testes de IRRF conferem os exemplos oficiais.
        db.TabelasIrrf.Add(new TabelaIrrf(
            new DateOnly(2026, 1, 1),
            "Lei n. 15.191, de 11/08/2025 (tabela) e Lei n. 15.270, de 26/11/2025 (redutor)",
            deducaoPorDependente: 189.59m,
            descontoSimplificado: 607.20m,
            redutorBase: 978.62m,
            redutorCoeficiente: 0.133145m,
            [
                (2428.80m, 0m, 0m),
                (2826.65m, 0.075m, 182.16m),
                (3751.05m, 0.15m, 394.16m),
                (4664.68m, 0.225m, 675.49m),
                (0m, 0.275m, 908.73m),
            ],
            agora));

        await db.SaveChangesAsync();

        IdOrganizacaoA = orgA.Id;
        IdOrganizacaoB = orgB.Id;
        IdEmpresaA = empresaA.Id;
        IdEmpresaB = empresaB.Id;
        IdEstabelecimentoA = estabA.Id;
        IdOrganizacaoC = orgC.Id;
        IdEmpresaC = empresaC.Id;
        IdEstabelecimentoC = estabC.Id;
        IdOrganizacaoD = orgD.Id;
        IdEmpresaD = empresaD.Id;
        IdEstabelecimentoD = estabD.Id;
        IdOrganizacaoE = orgE.Id;
        IdEmpresaE = empresaE.Id;
        IdEstabelecimentoE = estabE.Id;
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
