using Microsoft.AspNetCore.Authorization;
using PrismaRH.Dominio.Identidade;
using PrismaRH.Infraestrutura.Identidade;

namespace PrismaRH.Api.Identidade;

/// <summary>
/// Politicas por perfil. A autorizacao vive AQUI, no backend: esconder botao no
/// frontend nao e mecanismo de autorizacao (CLAUDE.md secao 6).
/// </summary>
public static class PoliticasAutorizacao
{
    public const string AdministradorPlataforma = "administrador-plataforma";
    public const string AdministrarEmpresas = "administrar-empresas";
    public const string AdministrarPessoas = "administrar-pessoas";
    public const string LerDadosEmpresariais = "ler-dados-empresariais";

    public static AuthorizationBuilder Adicionar(this AuthorizationBuilder builder) =>
        builder
            .AddPolicy(AdministradorPlataforma, p => ExigirPerfis(p, Perfil.AdministradorPlataforma))

            // Quem cria, altera e remove empresa ou estabelecimento.
            .AddPolicy(AdministrarEmpresas, p => ExigirPerfis(
                p,
                Perfil.AdministradorPlataforma,
                Perfil.AdministradorEmpresa))

            // Quem mantem cadastro de gente: funcionarios, contratos, cargos.
            // NAO reusa AdministrarEmpresas de proposito - o Analista de RH
            // mantem cadastros (CLAUDE.md secao 6) mas nao administra empresas.
            .AddPolicy(AdministrarPessoas, p => ExigirPerfis(
                p,
                Perfil.AdministradorPlataforma,
                Perfil.AdministradorEmpresa,
                Perfil.AnalistaRh))

            // Leitura: todos os perfis, inclusive Auditor e Visualizador.
            .AddPolicy(LerDadosEmpresariais, p => ExigirPerfis(
                p,
                Perfil.AdministradorPlataforma,
                Perfil.AdministradorEmpresa,
                Perfil.AnalistaRh,
                Perfil.Auditor,
                Perfil.Visualizador));

    private static void ExigirPerfis(AuthorizationPolicyBuilder politica, params Perfil[] perfis)
    {
        politica.RequireAuthenticatedUser();
        politica.RequireClaim(GeradorJwt.ClaimPerfil, perfis.Select(p => p.ToString()));
    }
}
