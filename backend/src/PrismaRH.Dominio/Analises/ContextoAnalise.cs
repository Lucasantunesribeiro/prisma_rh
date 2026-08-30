using PrismaRH.Dominio.Contratos;
using PrismaRH.Dominio.Folha;

namespace PrismaRH.Dominio.Analises;

/// <summary>Um lancamento, do ponto de vista de quem confere.</summary>
public sealed record LancamentoAnalisado(
    string CodigoRubrica,
    string NomeRubrica,
    TipoRubrica Tipo,
    OrigemLancamento Origem,
    decimal Valor);

/// <summary>Um holerite, do ponto de vista de quem confere.</summary>
public sealed record HoleriteAnalisado(
    Guid IdFolhaFuncionario,
    Guid IdFuncionario,
    Guid IdContrato,
    string Matricula,
    string NomeFuncionario,
    decimal SalarioReferencia,
    decimal TotalProventos,
    decimal TotalDescontos,
    decimal Liquido,
    IReadOnlyList<LancamentoAnalisado> Lancamentos);

/// <summary>Um contrato da empresa, para saber quem DEVERIA estar na folha.</summary>
public sealed record ContratoAnalisado(
    Guid IdContrato,
    Guid IdFuncionario,
    string Matricula,
    string NomeFuncionario,
    DateOnly DataAdmissao,
    DateOnly? DataDesligamento,
    SituacaoContrato Situacao)
{
    /// <summary>
    /// O contrato esteve vivo em algum dia da competencia?
    ///
    /// Admitido ate o ultimo dia e nao desligado antes do primeiro. Um unico
    /// dia basta: quem foi admitido no dia 31 tem direito a um dia de salario.
    /// </summary>
    public bool VigenteEm(Competencia competencia) =>
        DataAdmissao <= competencia.UltimoDia
        && (DataDesligamento is null || DataDesligamento >= competencia.PrimeiroDia);
}

/// <summary>
/// Tudo o que as regras precisam para conferir uma folha - e nada alem disso.
///
/// ## Por que um retrato, e nao o DbContext
///
/// As regras sao **funcoes puras**: recebem este retrato e devolvem achados.
/// Nao consultam banco, nao sabem o que e uma organizacao, nao conhecem HTTP.
///
/// Tres consequencias que valem a construcao a mais:
///
/// 1. **Testar e trivial** - monta-se o retrato em memoria, sem banco;
/// 2. **A execucao e reproduzivel**, que e criterio de aceite da fase: o mesmo
///    retrato produz o mesmo resultado, sempre;
/// 3. **O isolamento nao depende da regra se comportar.** Quem monta o retrato
///    e a camada de aplicacao, consultando **sob o filtro global**. Uma regra
///    nao tem como enxergar fora da organizacao nem se sua configuracao pedisse
///    - ela nao tem por onde perguntar.
///
/// O ponto 3 e a resposta ao item 2 do Security Gate da Fase 6.
/// </summary>
public sealed record ContextoAnalise(
    Guid IdFolha,
    Competencia Competencia,
    TipoFolha Tipo,
    SituacaoFolha Situacao,
    IReadOnlyList<HoleriteAnalisado> Holerites,
    IReadOnlyList<ContratoAnalisado> ContratosDaEmpresa,
    IReadOnlyDictionary<Guid, decimal> SalarioNaFolhaAnterior);
