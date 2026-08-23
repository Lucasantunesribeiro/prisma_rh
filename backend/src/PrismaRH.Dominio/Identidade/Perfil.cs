namespace PrismaRH.Dominio.Identidade;

/// <summary>
/// Perfis de acesso do Prisma RH (CLAUDE.md secao 6).
///
/// Os valores sao explicitos de proposito: o numero e gravado no banco, entao
/// reordenar a enum nao pode remapear o perfil de ninguem.
/// </summary>
public enum Perfil
{
    AdministradorPlataforma = 1,
    AdministradorEmpresa = 2,
    AnalistaRh = 3,
    Auditor = 4,
    Visualizador = 5
}
