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

    /// <summary>
    /// O sistema calcula: contribuicao progressiva do segurado sobre a base de
    /// INSS, pela tabela vigente na competencia da folha (Fase 4B).
    ///
    /// Nenhum numero legal mora aqui nem no motor: faixas, aliquotas e teto
    /// vem de TabelaInss, que exige fonte oficial registrada.
    /// </summary>
    InssProgressivo = 3,

    /// <summary>
    /// O sistema calcula: deposito de FGTS do empregador sobre a base de FGTS,
    /// pela aliquota vigente na competencia (Fase 4C).
    ///
    /// E INFORMATIVA: nao sai do salario de ninguem. FGTS e obrigacao da
    /// empresa, nao desconto do funcionario.
    /// </summary>
    FgtsMensal = 4,

    /// <summary>
    /// O sistema calcula: imposto de renda retido na fonte sobre a base de
    /// IRRF, deduzidos o INSS e os dependentes, pela tabela vigente na
    /// competencia (Fase 4D).
    ///
    /// E DESCONTO: sai do salario do funcionario, ao contrario do FGTS.
    ///
    /// Depende do INSS ja apurado, e por isso e a ultima rubrica calculada do
    /// holerite. Nenhum numero legal mora aqui: faixas, parcela a deduzir,
    /// deducao por dependente, desconto simplificado e redutor vem de
    /// TabelaIrrf, que exige fonte oficial registrada.
    /// </summary>
    IrrfMensal = 5,

    /// <summary>
    /// O sistema calcula: remuneracao dos dias de ferias gozados, pelo salario
    /// vigente na data da concessao (CLT art. 142). So aparece em folha do
    /// tipo Ferias.
    /// </summary>
    FeriasGozadas = 6,

    /// <summary>
    /// O sistema calcula: um terco sobre a remuneracao das ferias gozadas
    /// (CF art. 7o, XVII).
    /// </summary>
    TercoFerias = 7,

    /// <summary>
    /// O sistema calcula: os dias que o empregado converteu em dinheiro
    /// (CLT art. 143). NAO sao gozados.
    /// </summary>
    AbonoPecuniario = 8,

    /// <summary>
    /// O sistema calcula: um terco sobre o abono pecuniario.
    ///
    /// E rubrica SEPARADA do terco sobre ferias gozadas, e nao um detalhe: as
    /// incidencias das duas sao diferentes. Uma so rubrica de terco obrigaria
    /// a escolher uma das duas tabelas de incidencia e errar a outra.
    /// </summary>
    TercoAbono = 9,

    /// <summary>
    /// O sistema calcula: uma verba de rescisao, identificada pelo CODIGO da
    /// rubrica (Fase 4G).
    ///
    /// Aqui basta UMA estrategia, ao contrario das quatro de ferias, e a razao
    /// e concreta: nas ferias a estrategia dizia QUAL CONTA FAZER, e as contas
    /// eram diferentes. Na rescisao todas as verbas ja vem calculadas por
    /// CalculadoraRescisao - a rubrica so recebe o valor e empresta a sua
    /// incidencia.
    ///
    /// O casamento e por codigo: SALDO, AVISO, FERVEN, FERVEN13, FERPROP,
    /// FERPROP13, DEC13PROP, DEC13AV e MULTAFGTS. O calculo EXIGE as nove
    /// cadastradas e recusa se faltar alguma - uma verba sem rubrica sairia
    /// silenciosamente do acerto.
    /// </summary>
    VerbaRescisoria = 10,

    /// <summary>
    /// O sistema calcula: o adiantamento do 13o (Fase 4F).
    ///
    /// Natureza eSocial **5504 - 13o salario adiantamento**, com incidencia
    /// APENAS de FGTS.
    /// </summary>
    DecimoTerceiroAdiantamento = 11,

    /// <summary>
    /// O sistema calcula: o 13o INTEGRAL, na folha anual (Fase 4F).
    ///
    /// Natureza eSocial **5001**. Declara INSS e IRRF - e **nao** FGTS, porque
    /// o FGTS da folha anual incide so sobre a diferenca, que viaja na
    /// informativa <see cref="DecimoTerceiroBaseFgts"/>.
    /// </summary>
    DecimoTerceiroTotal = 12,

    /// <summary>
    /// O sistema calcula: o adiantamento ja pago, descontado na folha anual.
    ///
    /// Desconto, e portanto **sem incidencia alguma** - a invariante da Fase 4A
    /// recusa desconto que componha base, e esta certa: desconto nao reduz a
    /// base de INSS nem a de IRRF.
    /// </summary>
    DecimoTerceiroAdiantamentoDescontado = 13,

    /// <summary>
    /// O sistema calcula: a base de FGTS que ainda nao foi tributada.
    ///
    /// **Informativa**, e e a unica forma correta de resolver o problema das
    /// tres bases num holerite so. MOS S-1.3, item 10.3.4: "o FGTS incidente
    /// sobre a folha do 13o salario e calculado apenas sobre a diferenca entre
    /// o valor da gratificacao natalina e a primeira parcela".
    ///
    /// Informativa nao entra no liquido mas COMPOE base (Fase 4A) - entao ela
    /// carrega a diferenca para o FGTS sem pagar nada duas vezes.
    /// </summary>
    DecimoTerceiroBaseFgts = 14,
}
