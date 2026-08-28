namespace PrismaRH.Dominio.Contratos;

/// <summary>
/// Por que o contrato terminou.
///
/// E o campo que DECIDE as verbas rescisorias: quem pede demissao nao recebe
/// aviso previo indenizado nem multa de FGTS; quem e dispensado por justa
/// causa perde tambem as ferias proporcionais e o 13o proporcional; no acordo
/// do art. 484-A metade do aviso e metade da multa sao devidas. Sem ele, a
/// Fase 4G nao teria como calcular nada.
///
/// FONTES (CLAUDE.md secao 29) - cada motivo cita o artigo que o define:
/// - art. 482: justa causa do empregado;
/// - art. 483: rescisao indireta (justa causa do empregador);
/// - art. 484-A (Lei 13.467/2017): extincao por acordo entre as partes;
/// - art. 443 e 479/480: contrato por prazo determinado.
///
/// VOCABULARIO FECHADO (CLAUDE.md secao 24.7): decisao de negocio nao se toma
/// sobre texto digitado.
///
/// ATENCAO ao que esta lista NAO e: ela nao e a Tabela 19 do eSocial, que tem
/// cerca de trinta codigos e inclui situacoes que nao mudam verba nenhuma -
/// transferencia entre empresas do grupo, mudanca de CNPJ, reforma de
/// aposentadoria. Aqui estao os motivos que o CALCULO distingue, que e o que o
/// CLAUDE.md secao 7 pede: nada de campo sem uso claro.
///
/// O mapeamento para os codigos do eSocial e assunto de INTEGRACAO (Fase 8), e
/// fica pendente: a Tabela 19 nao pode ser lida das fontes oficiais com as
/// ferramentas disponiveis - o HTML trunca antes dela e os PDFs nao extraem.
/// </summary>
public enum MotivoDesligamento
{
    /// <summary>
    /// O empregador dispensa sem motivo disciplinar. E o caso mais completo:
    /// todas as verbas sao devidas.
    /// </summary>
    DispensaSemJustaCausa = 1,

    /// <summary>
    /// O empregador dispensa por falta grave do empregado (CLT art. 482).
    /// E o caso mais restrito.
    /// </summary>
    DispensaPorJustaCausa = 2,

    /// <summary>O empregado pede para sair.</summary>
    PedidoDeDemissao = 3,

    /// <summary>
    /// O empregado rompe por falta grave do EMPREGADOR (CLT art. 483). Tem os
    /// mesmos efeitos da dispensa sem justa causa - a diferenca esta em quem
    /// deu causa, nao no que e devido.
    /// </summary>
    RescisaoIndireta = 4,

    /// <summary>
    /// Extincao por acordo entre as partes (CLT art. 484-A, incluido pela Lei
    /// 13.467/2017). Aviso previo e multa do FGTS pela METADE, saque limitado
    /// a 80% e sem direito ao seguro-desemprego.
    /// </summary>
    AcordoEntreAsPartes = 5,

    /// <summary>
    /// Chegou ao fim o prazo combinado (CLT art. 443). Nao ha aviso previo:
    /// as duas partes ja sabiam a data.
    /// </summary>
    TerminoDeContratoPorPrazoDeterminado = 6,

    /// <summary>Morte do empregado. As verbas vao aos dependentes ou herdeiros.</summary>
    FalecimentoDoEmpregado = 7,

    /// <summary>
    /// Aposentadoria do empregado, quando ela encerra o vinculo.
    ///
    /// Mantido separado da dispensa e do pedido de demissao porque a
    /// jurisprudencia trata o caso de forma propria, e agrupa-lo com outro
    /// motivo esconderia essa distincao no cadastro.
    /// </summary>
    Aposentadoria = 8
}
