import { enviar, obter, remover } from './cliente'

// ---------------------------------------------------------------- cargos

export interface Cargo {
  id: string
  codigo: string
  nome: string
  ativo: boolean
}

export const listarCargos = (): Promise<Cargo[]> => obter('/api/cargos')

export const criarCargo = (dados: { codigo: string; nome: string }): Promise<Cargo> =>
  enviar('/api/cargos', dados)

// ---------------------------------------------------------------- funcionários

export interface Funcionario {
  id: string
  nome: string
  /** Na listagem vem mascarado; no detalhe, completo. */
  cpf: string
  cpfFormatado: string
  dataNascimento: string
  ativo: boolean
}

export interface PaginaFuncionarios {
  total: number
  pagina: number
  tamanho: number
  itens: Funcionario[]
}

export interface FiltroFuncionarios {
  nome?: string
  cpf?: string
  ativo?: boolean
}

export function listarFuncionarios(filtro: FiltroFuncionarios = {}): Promise<PaginaFuncionarios> {
  const parametros = new URLSearchParams()

  if (filtro.nome?.trim()) parametros.set('nome', filtro.nome.trim())
  if (filtro.cpf?.trim()) parametros.set('cpf', filtro.cpf.trim())
  if (filtro.ativo !== undefined) parametros.set('ativo', String(filtro.ativo))

  const consulta = parametros.toString()
  return obter(`/api/funcionarios${consulta ? `?${consulta}` : ''}`)
}

export const obterFuncionario = (id: string): Promise<Funcionario> =>
  obter(`/api/funcionarios/${id}`)

export const criarFuncionario = (dados: {
  nome: string
  cpf: string
  dataNascimento: string
}): Promise<Funcionario> => enviar('/api/funcionarios', dados)

// ---------------------------------------------------------------- contratos

export type SituacaoContrato = 'Ativo' | 'Desligado'

/**
 * Por que o contrato terminou — o campo que decide as verbas rescisórias.
 *
 * Não é a Tabela 19 do eSocial: são os motivos que o **cálculo** distingue.
 * Cada um cita o artigo da CLT que o define.
 */
export type MotivoDesligamento =
  | 'DispensaSemJustaCausa'
  | 'DispensaPorJustaCausa'
  | 'PedidoDeDemissao'
  | 'RescisaoIndireta'
  | 'AcordoEntreAsPartes'
  | 'TerminoDeContratoPorPrazoDeterminado'
  | 'FalecimentoDoEmpregado'
  | 'Aposentadoria'

export const ROTULO_MOTIVO_DESLIGAMENTO: Record<MotivoDesligamento, string> = {
  DispensaSemJustaCausa: 'Dispensa sem justa causa',
  DispensaPorJustaCausa: 'Dispensa por justa causa',
  PedidoDeDemissao: 'Pedido de demissão',
  RescisaoIndireta: 'Rescisão indireta',
  AcordoEntreAsPartes: 'Acordo entre as partes',
  TerminoDeContratoPorPrazoDeterminado: 'Término de contrato por prazo determinado',
  FalecimentoDoEmpregado: 'Falecimento do empregado',
  Aposentadoria: 'Aposentadoria',
}

/** O artigo que define cada motivo, para a tela citar em vez de só rotular. */
export const NORMA_MOTIVO_DESLIGAMENTO: Partial<Record<MotivoDesligamento, string>> = {
  DispensaPorJustaCausa: 'CLT art. 482',
  RescisaoIndireta: 'CLT art. 483',
  AcordoEntreAsPartes: 'CLT art. 484-A',
  TerminoDeContratoPorPrazoDeterminado: 'CLT art. 443',
}

export type MotivoVigencia =
  | 'Admissao'
  | 'AlteracaoSalarial'
  | 'MudancaCargo'
  | 'Transferencia'
  | 'AlteracaoJornada'
  | 'Desligamento'

export const ROTULO_MOTIVO: Record<MotivoVigencia, string> = {
  Admissao: 'Admissão',
  AlteracaoSalarial: 'Alteração salarial',
  MudancaCargo: 'Mudança de cargo',
  Transferencia: 'Transferência',
  AlteracaoJornada: 'Alteração de jornada',
  Desligamento: 'Desligamento',
}

export interface Vigencia {
  id: string
  validoDe: string
  validoAte: string | null
  salario: number
  idCargo: string
  idEstabelecimento: string
  jornadaMensalHoras: number
  motivo: MotivoVigencia
}

export interface Contrato {
  id: string
  idFuncionario: string
  idEmpresa: string
  matricula: string
  dataAdmissao: string
  dataDesligamento: string | null
  situacao: SituacaoContrato
  motivoDesligamento: MotivoDesligamento | null
  vigenciaAtual: Vigencia | null
}

export const listarContratos = (idFuncionario: string): Promise<Contrato[]> =>
  obter(`/api/funcionarios/${idFuncionario}/contratos`)

export const criarContrato = (
  idFuncionario: string,
  dados: {
    idEmpresa: string
    matricula: string
    dataAdmissao: string
    salarioInicial: number
    idCargo: string
    idEstabelecimento: string
    jornadaMensalHoras: number
  },
): Promise<Contrato> => enviar(`/api/funcionarios/${idFuncionario}/contratos`, dados)

export const listarVigencias = (idContrato: string): Promise<Vigencia[]> =>
  obter(`/api/contratos/${idContrato}/vigencias`)

export const registrarAlteracao = (
  idContrato: string,
  dados: {
    validoDe: string
    salario: number
    idCargo: string
    idEstabelecimento: string
    jornadaMensalHoras: number
    motivo: MotivoVigencia
  },
): Promise<Vigencia> => enviar(`/api/contratos/${idContrato}/vigencias`, dados)

export const desligar = (
  idContrato: string,
  dataDesligamento: string,
  motivo: MotivoDesligamento,
): Promise<Contrato> =>
  enviar(`/api/contratos/${idContrato}/desligamento`, { dataDesligamento, motivo })

// -------------------------------------------------------------- dependentes

export type RelacaoDependente =
  | 'Conjuge'
  | 'Companheiro'
  | 'Filho'
  | 'Enteado'
  | 'Irmao'
  | 'Neto'
  | 'Pai'
  | 'Mae'
  | 'Avo'
  | 'Tutelado'
  | 'Outro'

export const ROTULO_RELACAO: Record<RelacaoDependente, string> = {
  Conjuge: 'Cônjuge',
  Companheiro: 'Companheiro(a)',
  Filho: 'Filho(a)',
  Enteado: 'Enteado(a)',
  Irmao: 'Irmão(ã)',
  Neto: 'Neto(a)',
  Pai: 'Pai',
  Mae: 'Mãe',
  Avo: 'Avô/Avó',
  Tutelado: 'Tutelado(a)',
  Outro: 'Outro',
}

export interface Dependente {
  id: string
  idFuncionario: string
  nome: string
  dataNascimento: string
  relacao: RelacaoDependente
  /** Derivado do período: sem início, não abate IRRF. */
  dedutivelIrrf: boolean
  inicioDeducaoIrrf: string | null
  fimDeducaoIrrf: string | null
}

export interface DadosDependente {
  nome: string
  dataNascimento: string
  relacao: RelacaoDependente
  inicioDeducaoIrrf: string | null
  fimDeducaoIrrf: string | null
}

export const listarDependentes = (idFuncionario: string): Promise<Dependente[]> =>
  obter(`/api/funcionarios/${idFuncionario}/dependentes`)

export const criarDependente = (
  idFuncionario: string,
  dados: DadosDependente,
): Promise<Dependente> => enviar(`/api/funcionarios/${idFuncionario}/dependentes`, dados)

export const atualizarDependente = (
  idFuncionario: string,
  id: string,
  dados: DadosDependente,
): Promise<Dependente> =>
  enviar(`/api/funcionarios/${idFuncionario}/dependentes/${id}`, dados, 'PUT')

export const removerDependente = (idFuncionario: string, id: string): Promise<void> =>
  remover(`/api/funcionarios/${idFuncionario}/dependentes/${id}`)

// ------------------------------------------------------------------ férias

export type SituacaoPeriodoAquisitivo = 'EmAndamento' | 'Adquirido' | 'Vencido'

export const ROTULO_SITUACAO_PERIODO: Record<SituacaoPeriodoAquisitivo, string> = {
  EmAndamento: 'Em andamento',
  Adquirido: 'Adquirido',
  Vencido: 'Vencido',
}

export type SituacaoConcessao = 'Programada' | 'EmGozo' | 'Concluida'

export const ROTULO_SITUACAO_CONCESSAO: Record<SituacaoConcessao, string> = {
  Programada: 'Programada',
  EmGozo: 'Em gozo',
  Concluida: 'Concluída',
}

export interface ConcessaoFerias {
  id: string
  inicioPeriodoAquisitivo: string
  fimPeriodoAquisitivo: string
  inicio: string
  fim: string
  dias: number
  /** Dias vendidos (CLT art. 143). Não são gozados. */
  diasAbonoPecuniario: number
  diasBaixados: number
  situacao: SituacaoConcessao
  podeCancelar: boolean
}

export interface PeriodoAquisitivo {
  numero: number
  inicio: string
  fim: string
  inicioConcessao: string
  /** Depois desta data a remuneração é devida em dobro (CLT art. 137). */
  limiteConcessao: string
  diasDireito: number
  situacao: SituacaoPeriodoAquisitivo
  diasParaCompletar: number
  emDobra: boolean
  diasConcedidos: number
  saldo: number
  /** Quanto ainda pode ser vendido: 1/3 do período menos o já vendido. */
  saldoAbono: number
  /** Frações de gozo já usadas. No máximo três (CLT art. 134, §1º). */
  fracoesUsadas: number
  concessoes: ConcessaoFerias[]
}

export interface FeriasDoContrato {
  idContrato: string
  matricula: string
  dataAdmissao: string
  dataDesligamento: string | null
  /** A data usada como referência. Sem parâmetro, é hoje. */
  referencia: string
  diasAdquiridos: number
  /** Dias adquiridos menos o que já foi programado. */
  saldoTotal: number
  periodosVencidos: number
  periodos: PeriodoAquisitivo[]
}

export const listarPeriodosFerias = (idContrato: string): Promise<FeriasDoContrato> =>
  obter(`/api/contratos/${idContrato}/ferias/periodos`)

export const concederFerias = (
  idContrato: string,
  dados: {
    inicioPeriodoAquisitivo: string
    inicio: string
    dias: number
    diasAbonoPecuniario: number
  },
): Promise<ConcessaoFerias> => enviar(`/api/contratos/${idContrato}/ferias/concessoes`, dados)

export const cancelarConcessao = (idContrato: string, id: string): Promise<void> =>
  remover(`/api/contratos/${idContrato}/ferias/concessoes/${id}`)

// --------------------------------------------------------- décimo terceiro

export interface MesDoAvo {
  mes: number
  diasTrabalhados: number
  /** Fração ≥ 15 dias conta como mês inteiro (Lei 4.090/1962). */
  conta: boolean
  motivo: string
}

export interface AvosDecimoTerceiro {
  idContrato: string
  matricula: string
  dataAdmissao: string
  dataDesligamento: string | null
  ano: number
  avos: number
  /** "7/12", pronto para exibir. */
  fracao: string
  anoCompleto: boolean
  meses: MesDoAvo[]
}

export const obterAvosDecimoTerceiro = (
  idContrato: string,
  ano?: number,
): Promise<AvosDecimoTerceiro> =>
  obter(
    `/api/contratos/${idContrato}/decimo-terceiro/avos${ano === undefined ? '' : `?ano=${ano}`}`,
  )

export const MESES_CURTOS = [
  'jan',
  'fev',
  'mar',
  'abr',
  'mai',
  'jun',
  'jul',
  'ago',
  'set',
  'out',
  'nov',
  'dez',
] as const

// ---------------------------------------------------------------- formatação

const MOEDA = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' })

export const formatarSalario = (valor: number): string => MOEDA.format(valor)

/**
 * Datas civis vêm da API como "2026-08-23", sem fuso.
 * `new Date('2026-08-23')` interpretaria como UTC e, no Brasil, exibiria o dia
 * anterior. Por isso a formatação é feita na mão, sem passar por Date.
 */
export function formatarData(iso: string | null): string {
  if (!iso) return '—'

  const [ano, mes, dia] = iso.slice(0, 10).split('-')
  return `${dia}/${mes}/${ano}`
}
