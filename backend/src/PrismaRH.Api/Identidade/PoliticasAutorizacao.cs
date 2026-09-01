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
    public const string ProcessarFolha = "processar-folha";
    public const string LerDadosEmpresariais = "ler-dados-empresariais";

    public static AuthorizationBuilder Adicionar(this AuthorizationBuilder builder) =>
        builder
            // ⚠️ FALHA FECHADA (Fase 12).
            //
            // `CLAUDE.md secao 24.4`: "Negar por padrao. Rota sem politica
            // declarada e erro de implementacao, nao rota liberada."
            //
            // Sem esta linha, uma rota nova onde alguem esquecesse
            // `RequireAuthorization` nasceria ANONIMA - e ninguem percebe uma
            // rota que funciona. Com ela, a rota esquecida devolve 401 e o
            // defeito aparece na primeira chamada.
            //
            // Nao substitui a politica por rota: o fallback so exige usuario
            // autenticado, e nao o PERFIL certo. Ele e a rede, nao o piso. O
            // `InventarioDeRotasTestes` continua exigindo politica explicita em
            // toda rota.
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build())

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

            // Quem abre, calcula e fecha folha. Hoje os perfis coincidem com
            // AdministrarPessoas, mas o significado e outro: o CLAUDE.md
            // secao 6 da "processar folha" ao Analista de RH como atribuicao
            // propria. Uma politica com nome errado e a que ninguem revisa
            // quando um dos dois conjuntos precisar mudar.
            .AddPolicy(ProcessarFolha, p => ExigirPerfis(
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
