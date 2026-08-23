using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PrismaRH.Aplicacao.Comum;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Dominio.Empresas;
using PrismaRH.Dominio.Identidade;

namespace PrismaRH.Infraestrutura.Persistencia;

/// <summary>
/// Dados ficticios para desenvolvimento e demonstracao. NUNCA roda fora de
/// Development.
///
/// Cria DUAS organizacoes de proposito: com uma so, um furo no isolamento
/// multiempresa passaria despercebido, porque nao haveria vizinho para invadir.
///
/// A senha vem de PRISMARH_SEED_SENHA. Nao ha senha no codigo.
/// </summary>
public static class SemeadorDesenvolvimento
{
    public const string VariavelSenha = "PRISMARH_SEED_SENHA";

    public static async Task SemearAsync(IServiceProvider servicos, CancellationToken ct = default)
    {
        using var escopo = servicos.CreateScope();

        var contexto = escopo.ServiceProvider.GetRequiredService<PrismaRhDbContext>();
        var hasheador = escopo.ServiceProvider.GetRequiredService<IHasheadorSenha>();
        var relogio = escopo.ServiceProvider.GetRequiredService<IRelogio>();
        var log = escopo.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(SemeadorDesenvolvimento));

        // O banco pode estar fora. A aplicacao PRECISA subir mesmo assim: e o
        // /health que reporta o estado do banco, e ele so consegue reportar se
        // a API estiver de pe. Derrubar o startup aqui trocaria "banco
        // indisponivel" por "aplicacao nao inicia".
        if (!await contexto.Database.CanConnectAsync(ct))
        {
            log.LogWarning("Semeadura ignorada: banco indisponivel. Verifique em /health.");
            return;
        }

        // Idempotente: se ja existe organizacao, nao faz nada. Rodar duas vezes
        // nao pode duplicar nem sobrescrever o que voce alterou testando.
        if (await contexto.Organizacoes.IgnoreQueryFilters().AnyAsync(ct))
        {
            log.LogInformation("Semeadura ignorada: ja existem organizacoes.");
            return;
        }

        var senha = Environment.GetEnvironmentVariable(VariavelSenha);

        if (string.IsNullOrWhiteSpace(senha))
        {
            log.LogWarning(
                "Semeadura ignorada: defina {Variavel} para criar os usuarios de demonstracao.",
                VariavelSenha);
            return;
        }

        var agora = relogio.Agora;
        var hash = hasheador.Gerar(senha);

        var prisma = new Organizacao("Prisma Servicos de RH Ltda.", agora);
        var horizonte = new Organizacao("Contabilidade Horizonte Ltda.", agora);
        contexto.Organizacoes.AddRange(prisma, horizonte);

        // Um usuario por perfil na organizacao principal, para dar para testar
        // autorizacao entrando com cada um.
        contexto.Usuarios.AddRange(
            new Usuario(prisma.Id, "Ana Plataforma", "plataforma@prisma.exemplo", hash, Perfil.AdministradorPlataforma, agora),
            new Usuario(prisma.Id, "Bruno Admin", "admin@prisma.exemplo", hash, Perfil.AdministradorEmpresa, agora),
            new Usuario(prisma.Id, "Carla Analista", "analista@prisma.exemplo", hash, Perfil.AnalistaRh, agora),
            new Usuario(prisma.Id, "Diego Auditor", "auditor@prisma.exemplo", hash, Perfil.Auditor, agora),
            new Usuario(prisma.Id, "Elisa Visualizadora", "visualizador@prisma.exemplo", hash, Perfil.Visualizador, agora),

            // O vizinho: existe para provar que ele NAO enxerga a Prisma.
            new Usuario(horizonte.Id, "Fabio Horizonte", "admin@horizonte.exemplo", hash, Perfil.AdministradorEmpresa, agora));

        var empresaPrisma = new Empresa(prisma.Id, "Industria Modelo S.A.", Cnpj.Criar("11222333000181"), agora, "Modelo");
        var empresaHorizonte = new Empresa(horizonte.Id, "Comercio Vizinho Ltda.", Cnpj.Criar("11444777000161"), agora, "Vizinho");
        contexto.Empresas.AddRange(empresaPrisma, empresaHorizonte);

        contexto.Estabelecimentos.AddRange(
            new Estabelecimento(prisma.Id, empresaPrisma.Id, "001", "Matriz", agora),
            new Estabelecimento(prisma.Id, empresaPrisma.Id, "002", "Filial Sul", agora),
            new Estabelecimento(horizonte.Id, empresaHorizonte.Id, "001", "Matriz Vizinha", agora));

        await contexto.SaveChangesAsync(ct);

        log.LogInformation(
            "Semeadura concluida: 2 organizacoes, 6 usuarios, 2 empresas, 3 estabelecimentos.");
    }
}
