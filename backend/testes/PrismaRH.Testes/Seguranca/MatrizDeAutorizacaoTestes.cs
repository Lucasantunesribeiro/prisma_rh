using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PrismaRH.Api.Identidade;
using PrismaRH.Dominio.Identidade;
using PrismaRH.Infraestrutura.Identidade;
using PrismaRH.Testes.Isolamento;

namespace PrismaRH.Testes.Seguranca;

/// <summary>
/// A matriz de autorização (Fase 12).
///
/// ## O que o gate pede, e por quê
///
/// Security Gate da Fase 12, item 6: *"Auditoria da matriz Recurso × Operação ×
/// Perfil **contra o código real, não contra o documento**."*
///
/// E o `CLAUDE.md §24.4` explica a razão:
///
/// > *"A matriz é derivada do código, não o contrário: se documento e código
/// > divergirem, o código é o fato e o documento é o defeito."*
///
/// ## Como este teste faz isso
///
/// Ele não lê o `CLAUDE.md`. Ele pega o `AuthorizationService` **da aplicação
/// rodando**, monta um usuário de cada perfil e **avalia cada política de
/// verdade**. O resultado é a matriz real.
///
/// Depois compara com a tabela declarada abaixo. Mudar quem pode fazer o quê
/// passa a exigir mudar esta tabela — num diff que alguém lê.
///
/// ## O que isto pega que um teste de rota não pega
///
/// Um teste de rota prova uma rota. Este prova as **cinco políticas contra os
/// cinco perfis**, de uma vez, e cobre as 85 rotas indiretamente ao exigir que
/// todas usem uma política conhecida.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class MatrizDeAutorizacaoTestes(BancoPostgresFixture banco)
{
    /// <summary>
    /// ⚠️ **A matriz declarada.** Quem passa em cada política.
    ///
    /// Sai da leitura do `PoliticasAutorizacao`, e é conferida contra a
    /// avaliação real logo abaixo.
    /// </summary>
    private static readonly Dictionary<string, Perfil[]> Esperado = new()
    {
        [PoliticasAutorizacao.AdministradorPlataforma] =
            [Perfil.AdministradorPlataforma],

        // Quem cria, altera e remove empresa, estabelecimento e o catalogo de
        // rubricas. O Analista NAO entra: ele mantem cadastro e calcula folha,
        // e nao administra a estrutura da organizacao (`CLAUDE.md §24.4`).
        [PoliticasAutorizacao.AdministrarEmpresas] =
            [Perfil.AdministradorPlataforma, Perfil.AdministradorEmpresa],

        [PoliticasAutorizacao.AdministrarPessoas] =
            [Perfil.AdministradorPlataforma, Perfil.AdministradorEmpresa, Perfil.AnalistaRh],

        [PoliticasAutorizacao.ProcessarFolha] =
            [Perfil.AdministradorPlataforma, Perfil.AdministradorEmpresa, Perfil.AnalistaRh],

        // Leitura: os cinco. Auditor e Visualizador existem para isto.
        [PoliticasAutorizacao.LerDadosEmpresariais] =
            [
                Perfil.AdministradorPlataforma, Perfil.AdministradorEmpresa,
                Perfil.AnalistaRh, Perfil.Auditor, Perfil.Visualizador,
            ],
    };

    private static ClaimsPrincipal Usuario(Perfil perfil) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString()),
                new Claim(GeradorJwt.ClaimOrganizacao, Guid.CreateVersion7().ToString()),
                new Claim(GeradorJwt.ClaimPerfil, perfil.ToString()),
            ],
            authenticationType: "Teste"));

    /// <summary>
    /// ⚠️ **A auditoria da matriz.**
    ///
    /// Avalia cada política contra cada perfil, pelo `IAuthorizationService`
    /// real da aplicação — o mesmo objeto que decide numa requisição.
    /// </summary>
    [Fact]
    public async Task AMatrizRealBateComAMatrizDeclarada()
    {
        using var fabrica = new FabricaApiIsolada(banco.StringConexao);
        using var escopo = fabrica.Services.CreateScope();

        var autorizacao = escopo.ServiceProvider.GetRequiredService<IAuthorizationService>();

        var divergencias = new List<string>();

        foreach (var (politica, permitidos) in Esperado)
        {
            foreach (var perfil in Enum.GetValues<Perfil>())
            {
                var resultado = await autorizacao.AuthorizeAsync(Usuario(perfil), politica);

                var deveriaPassar = permitidos.Contains(perfil);

                if (resultado.Succeeded != deveriaPassar)
                {
                    divergencias.Add(
                        $"{politica} x {perfil}: real={resultado.Succeeded} "
                        + $"declarado={deveriaPassar}");
                }
            }
        }

        Assert.True(divergencias.Count == 0, string.Join(" | ", divergencias));
    }

    /// <summary>
    /// ⚠️ Usuário **sem perfil** não passa em política nenhuma.
    ///
    /// `CLAUDE.md §24.2`: *fail closed*. Um token sem o claim `perfil` — por
    /// erro de emissão ou por adulteração — não pode cair no caminho mais
    /// permissivo.
    /// </summary>
    [Fact]
    public async Task UsuarioSemPerfilNaoPassaEmPoliticaNenhuma()
    {
        using var fabrica = new FabricaApiIsolada(banco.StringConexao);
        using var escopo = fabrica.Services.CreateScope();

        var autorizacao = escopo.ServiceProvider.GetRequiredService<IAuthorizationService>();

        var semPerfil = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString())],
            authenticationType: "Teste"));

        foreach (var politica in Esperado.Keys)
        {
            var resultado = await autorizacao.AuthorizeAsync(semPerfil, politica);

            Assert.False(resultado.Succeeded, $"{politica} aceitou usuario sem perfil.");
        }
    }

    /// <summary>
    /// Perfil **inventado** no token não passa. O claim é texto, e texto vindo
    /// de fora é dado não confiável mesmo depois de a assinatura conferir — o
    /// emissor pode estar certo e o valor, errado.
    /// </summary>
    [Theory]
    [InlineData("Superusuario")]
    [InlineData("admin")]
    [InlineData("1")]
    [InlineData("")]
    public async Task PerfilInventadoNaoPassaEmPoliticaNenhuma(string perfilTorto)
    {
        using var fabrica = new FabricaApiIsolada(banco.StringConexao);
        using var escopo = fabrica.Services.CreateScope();

        var autorizacao = escopo.ServiceProvider.GetRequiredService<IAuthorizationService>();

        var forjado = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString()),
                new Claim(GeradorJwt.ClaimPerfil, perfilTorto),
            ],
            authenticationType: "Teste"));

        foreach (var politica in Esperado.Keys)
        {
            var resultado = await autorizacao.AuthorizeAsync(forjado, politica);

            Assert.False(resultado.Succeeded, $"{politica} aceitou o perfil '{perfilTorto}'.");
        }
    }

    /// <summary>
    /// ⚠️ Toda rota de negócio usa uma política **conhecida**.
    ///
    /// É o que liga a matriz acima às 85 rotas: se alguma usasse um nome fora
    /// da lista, a matriz auditada não descreveria o sistema inteiro — e a
    /// auditoria estaria mentindo por omissão.
    /// </summary>
    [Fact]
    public void NenhumaRotaUsaPoliticaForaDaMatriz()
    {
        using var fabrica = new FabricaApiIsolada(banco.StringConexao);

        var desconhecidas = fabrica.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(r => (r.RoutePattern.RawText ?? string.Empty)
                .StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            .SelectMany(r => r.Metadata.GetOrderedMetadata<IAuthorizeData>())
            .Select(a => a.Policy)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct()
            .Where(p => !Esperado.ContainsKey(p!))
            .ToList();

        Assert.True(
            desconhecidas.Count == 0,
            "Rota usando politica fora da matriz auditada: " + string.Join(" | ", desconhecidas));
    }

    /// <summary>
    /// O caminho inverso: uma política declarada na matriz que nenhuma rota usa
    /// é política morta. Ela dá a impressão de que algo está protegido por ela.
    /// </summary>
    [Fact]
    public void TodaPoliticaDaMatrizEUsadaPorAlgumaRota()
    {
        using var fabrica = new FabricaApiIsolada(banco.StringConexao);

        var emUso = fabrica.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(r => r.Metadata.GetOrderedMetadata<IAuthorizeData>())
            .Select(a => a.Policy)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct()
            .ToHashSet();

        foreach (var politica in Esperado.Keys)
        {
            Assert.Contains(politica, emUso);
        }
    }
}
