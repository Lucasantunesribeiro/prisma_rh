namespace PrismaRH.Dominio.Contratos;

/// <summary>
/// Situacao do vinculo. Valores explicitos: o numero vai para o banco.
///
/// Afastamento e ferias NAO estao aqui de proposito - pertencem a Fase 4, e
/// criar o estado antes da regra que o usa seria antecipar fase futura.
/// </summary>
public enum SituacaoContrato
{
    Ativo = 1,
    Desligado = 2
}
