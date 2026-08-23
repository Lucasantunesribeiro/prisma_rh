namespace PrismaRH.Dominio.Contratos;

/// <summary>
/// Por que uma vigencia comecou. Responde "o que mudou aqui?" sem precisar
/// comparar duas linhas do historico campo a campo.
/// </summary>
public enum MotivoVigencia
{
    Admissao = 1,
    AlteracaoSalarial = 2,
    MudancaCargo = 3,
    Transferencia = 4,
    AlteracaoJornada = 5,
    Desligamento = 6
}
