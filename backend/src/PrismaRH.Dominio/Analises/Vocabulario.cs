namespace PrismaRH.Dominio.Analises;

/// <summary>
/// As regras que o Prisma RH conhece.
///
/// ## Por que enum, e nao texto
///
/// O `ROADMAP.md` da Fase 6 e o `CLAUDE.md secao 11` dizem a mesma coisa: **o
/// usuario nao escreve codigo nem SQL**. Ele configura regra OFICIAL do
/// sistema.
///
/// Um enum fechado e a forma mais forte de garantir isso. Nao ha string vinda
/// do cliente que vire regra: o que nao esta aqui nao existe, e a recusa
/// acontece na desserializacao, antes de qualquer codigo de negocio rodar.
///
/// Mesmo mecanismo da <see cref="Folha.EstrategiaRubrica"/>, e pela mesma
/// razao: parametrizacao **nunca** executa codigo (`CLAUDE.md secao 24.8`).
/// </summary>
public enum CodigoRegra
{
    /// <summary>Desligado antes da competencia, mas presente na folha mensal.</summary>
    DesligadoNaFolha = 1,

    /// <summary>Contrato ativo na competencia, sem holerite na folha mensal.</summary>
    AusenteDaFolha = 2,

    /// <summary>Holerite com liquido negativo: a pessoa deve para a empresa.</summary>
    LiquidoNegativo = 3,

    /// <summary>A mesma rubrica lancada mais de uma vez no mesmo holerite.</summary>
    RubricaDuplicada = 4,

    /// <summary>Descontos passando do percentual configurado sobre os proventos.</summary>
    DescontoAcimaDoLimite = 5,

    /// <summary>Salario de referencia variando alem da tolerancia entre competencias.</summary>
    VariacaoSalarial = 6,
}

/// <summary>
/// O assunto da regra.
///
/// Serve para agrupar na tela e para filtrar o relatorio. Sai da lista de
/// categorias do `ROADMAP.md` da Fase 6 - so as que tem regra de verdade,
/// porque categoria vazia e promessa de funcionalidade que nao existe.
/// </summary>
public enum CategoriaRegra
{
    Contrato = 1,
    Ausencia = 2,
    Valores = 3,
    Duplicidade = 4,
    Salario = 5,
}

/// <summary>
/// Quanto o achado importa.
///
/// Tres niveis, e nao cinco: a escala existe para ORDENAR o trabalho de quem
/// vai conferir, e escala fina demais faz todo mundo escolher o meio.
/// </summary>
public enum Severidade
{
    /// <summary>Confira quando puder. Pode ser legitimo.</summary>
    Baixa = 1,

    /// <summary>Confira antes de fechar a folha.</summary>
    Media = 2,

    /// <summary>Quase certamente errado. Dinheiro ou obrigacao legal em jogo.</summary>
    Alta = 3,
}
