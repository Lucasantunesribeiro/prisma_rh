namespace PrismaRH.Dominio.Pessoas;

/// <summary>
/// O vinculo entre o dependente e o funcionario.
///
/// Vocabulario FECHADO, nunca texto livre (CLAUDE.md secao 24.7): decisao de
/// negocio nao se toma sobre string digitada.
///
/// Atencao ao que este enum NAO faz: ele NAO decide se o dependente abate
/// IRRF. A dedutibilidade e declarada explicitamente em Dependente, com
/// periodo proprio. Derivar dedutibilidade da relacao exigiria codificar os
/// limites legais de idade e as condicoes de cada categoria - regra que
/// precisa de fonte oficial registrada (CLAUDE.md secao 29), e que esta fase
/// nao tem.
/// </summary>
public enum RelacaoDependente
{
    Conjuge = 1,
    Companheiro = 2,
    Filho = 3,
    Enteado = 4,
    Irmao = 5,
    Neto = 6,
    Pai = 7,
    Mae = 8,
    Avo = 9,
    Tutelado = 10,
    Outro = 99
}
