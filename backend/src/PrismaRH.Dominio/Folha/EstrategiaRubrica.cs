namespace PrismaRH.Dominio.Folha;

/// <summary>
/// Como o valor da rubrica aparece na folha.
///
/// E um enum fechado de proposito. O CLAUDE.md secao 9 proibe mecanismo
/// generico de execucao de codigo do usuario: parametrizar formula por texto
/// livre seria dar um interpretador para quem preenche cadastro, com o risco
/// de seguranca e a impossibilidade de testar que isso traz.
///
/// Cada estrategia nova e uma regra escrita, revisada e testada em C#.
/// </summary>
public enum EstrategiaRubrica
{
    /// <summary>
    /// O sistema calcula: salario da vigencia x avos do mes / 30. E a unica
    /// rubrica automatica da Fase 3.
    /// </summary>
    SalarioBaseProporcional = 1,

    /// <summary>
    /// O valor vem digitado por quem processa a folha. O sistema so guarda,
    /// classifica e soma.
    /// </summary>
    ValorInformado = 2,
}
