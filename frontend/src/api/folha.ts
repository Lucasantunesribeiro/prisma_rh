import { enviar, obter, remover } from './cliente'
import type { Perfil } from './autenticacao'

// ---------------------------------------------------------------- rubricas

export type TipoRubrica = 'Provento' | 'Desconto' | 'Informativo'
export type EstrategiaRubrica =
  | 'SalarioBaseProporcional'
  | 'ValorInformado'
  | 'InssProgressivo'
  | 'FgtsMensal'
  | 'IrrfMensal'

/**
 * De onde sai o valor da rubrica, para a coluna da listagem.
 *
 * O Record é exaustivo de propósito: acrescentar uma estratégia no backend sem
 * acrescentá-la aqui passa a quebrar a compilação, em vez de renderizar
 * `undefined` na tela — que foi o que aconteceu quando o FGTS entrou.
 */
export const ORIGEM_DO_VALOR: Record<EstrategiaRubrica, string> = {
  SalarioBaseProporcional: 'calculado pelo sistema',
  InssProgressivo: 'calculado pelo sistema',
  FgtsMensal: 'calculado pelo sistema',
  IrrfMensal: 'calculado pelo sistema',
  ValorInformado: 'digitado no lançamento',
}
export type BaseCalculo = 'Inss' | 'Fgts' | 'Irrf'

/** As tres bases, na ordem em que o backend as devolve. */
export const BASES: readonly BaseCalculo[] = ['Inss', 'Fgts', 'Irrf']

export const ROTULO_BASE: Record<BaseCalculo, string> = {
  Inss: 'INSS',
  Fgts: 'FGTS',
  Irrf: 'IRRF',
}

export interface Rubrica {
  id: string
  codigo: string
  nome: string
  tipo: TipoRubrica
  estrategia: EstrategiaRubrica
  /** Enum de bits do backend, serializado como texto: "Inss, Fgts" ou "Nenhuma". */
  basesIncidentes: string
  ativa: boolean
}

export const ROTULO_TIPO_RUBRICA: Record<TipoRubrica, string> = {
  Provento: 'Provento',
  Desconto: 'Desconto',
  Informativo: 'Informativo',
}

export const listarRubricas = (somenteAtivas = false): Promise<Rubrica[]> =>
  obter(`/api/rubricas${somenteAtivas ? '?ativas=true' : ''}`)

export const criarRubrica = (dados: {
  codigo: string
  nome: string
  tipo: TipoRubrica
  estrategia: EstrategiaRubrica
  basesIncidentes: string
}): Promise<Rubrica> => enviar('/api/rubricas', dados)

export const alterarIncidencias = (id: string, basesIncidentes: string): Promise<Rubrica> =>
  enviar(`/api/rubricas/${id}/incidencias`, { basesIncidentes }, 'PUT')

/**
 * O backend manda "Inss, Fgts" ou "Nenhuma". Vira lista para a tela marcar as
 * caixas; o caminho de volta e juntarBases.
 */
export function separarBases(texto: string | null | undefined): BaseCalculo[] {
  if (!texto || texto === 'Nenhuma') return []

  return texto
    .split(',')
    .map((parte) => parte.trim())
    .filter((parte): parte is BaseCalculo => (BASES as readonly string[]).includes(parte))
}

export const juntarBases = (bases: BaseCalculo[]): string =>
  bases.length === 0 ? 'Nenhuma' : BASES.filter((b) => bases.includes(b)).join(', ')

export const inativarRubrica = (id: string): Promise<void> => remover(`/api/rubricas/${id}`)

// ---------------------------------------------------------------- folhas

export type SituacaoFolha = 'Rascunho' | 'Calculada' | 'Fechada'
export type OrigemLancamento = 'Calculado' | 'Manual'

export const ROTULO_SITUACAO_FOLHA: Record<SituacaoFolha, string> = {
  Rascunho: 'Rascunho',
  Calculada: 'Calculada',
  Fechada: 'Fechada',
}

export interface FolhaResumo {
  id: string
  idEmpresa: string
  empresa: string
  competencia: string
  situacao: SituacaoFolha
  versaoCalculo: number
  quantidadeFuncionarios: number
  totalProventos: number
  totalDescontos: number
  totalLiquido: number
  calculadaEm: string | null
  fechadaEm: string | null
}

export interface HoleriteResumo {
  id: string
  idFuncionario: string
  funcionario: string
  matricula: string
  avos: number
  divisor: number
  salarioReferencia: number
  totalProventos: number
  totalDescontos: number
  liquido: number
}

export interface FolhaDetalhe {
  folha: FolhaResumo
  funcionarios: HoleriteResumo[]
}

export interface LinhaMemoria {
  ordem: number
  descricao: string
  expressao: string
  valor: number
}

export interface Lancamento {
  id: string
  codigoRubrica: string
  nomeRubrica: string
  tipo: TipoRubrica
  origem: OrigemLancamento
  referencia: string | null
  valor: number
  ordem: number
  basesIncidentes: string
  memoria: LinhaMemoria[]
}

export interface BaseApurada {
  base: BaseCalculo
  valor: number
  /** Codigos das rubricas que formaram esta base. */
  composta: string[]
}

export interface Holerite {
  resumo: HoleriteResumo
  competencia: string
  situacaoFolha: SituacaoFolha
  lancamentos: Lancamento[]
  bases: BaseApurada[]
}

export function listarFolhas(filtro: { idEmpresa?: string; competencia?: string } = {}): Promise<
  FolhaResumo[]
> {
  const parametros = new URLSearchParams()

  if (filtro.idEmpresa) parametros.set('idEmpresa', filtro.idEmpresa)
  if (filtro.competencia?.trim()) parametros.set('competencia', filtro.competencia.trim())

  const consulta = parametros.toString()
  return obter(`/api/folhas${consulta ? `?${consulta}` : ''}`)
}

export const obterFolha = (id: string): Promise<FolhaDetalhe> => obter(`/api/folhas/${id}`)

export const abrirFolha = (idEmpresa: string, competencia: string): Promise<FolhaResumo> =>
  enviar('/api/folhas', { idEmpresa, competencia })

export const calcularFolha = (id: string): Promise<FolhaDetalhe> =>
  enviar(`/api/folhas/${id}/calcular`, {})

export const fecharFolha = (id: string): Promise<FolhaResumo> =>
  enviar(`/api/folhas/${id}/fechar`, {})

export const obterHolerite = (idFolha: string, idHolerite: string): Promise<Holerite> =>
  obter(`/api/folhas/${idFolha}/funcionarios/${idHolerite}`)

export const lancar = (
  idFolha: string,
  idHolerite: string,
  dados: { idRubrica: string; valor: number; referencia: string | null },
): Promise<Holerite> => enviar(`/api/folhas/${idFolha}/funcionarios/${idHolerite}/lancamentos`, dados)

export const removerLancamento = (
  idFolha: string,
  idHolerite: string,
  idLancamento: string,
): Promise<void> =>
  remover(`/api/folhas/${idFolha}/funcionarios/${idHolerite}/lancamentos/${idLancamento}`)

// ---------------------------------------------------------------- permissões

const PERFIS_PROCESSAM: Perfil[] = ['AdministradorPlataforma', 'AdministradorEmpresa', 'AnalistaRh']

/**
 * Só adapta a interface. A autoridade é o backend: a política ProcessarFolha
 * barra a chamada mesmo com o botão visível.
 */
export function podeProcessarFolha(perfil: Perfil | undefined): boolean {
  return perfil !== undefined && PERFIS_PROCESSAM.includes(perfil)
}

// ---------------------------------------------------------------- competência

/**
 * Aceita "8/2026", "08/2026" e "082026" e devolve "08/2026", que é o que a API
 * entende. Recusar por causa do zero à esquerda seria pedantismo com quem está
 * digitando depressa.
 */
export function normalizarCompetencia(texto: string): string | null {
  const digitos = texto.replace(/\D/g, '')

  if (digitos.length < 5 || digitos.length > 6) return null

  const mes = Number(digitos.slice(0, digitos.length - 4))
  const ano = Number(digitos.slice(-4))

  if (mes < 1 || mes > 12 || ano < 2000 || ano > 2100) return null

  return `${String(mes).padStart(2, '0')}/${ano}`
}

/** "08/2026" vira "agosto de 2026". */
export function competenciaPorExtenso(competencia: string): string {
  const [mes, ano] = competencia.split('/')
  const nomes = [
    'janeiro',
    'fevereiro',
    'março',
    'abril',
    'maio',
    'junho',
    'julho',
    'agosto',
    'setembro',
    'outubro',
    'novembro',
    'dezembro',
  ]

  const indice = Number(mes) - 1
  return indice >= 0 && indice < 12 ? `${nomes[indice]} de ${ano}` : competencia
}

// ------------------------------------------------- parametros legais (4B)

export interface FaixaInss {
  ordem: number
  limiteInferior: number
  limiteSuperior: number
  aliquota: number
  aliquotaPercentual: number
}

export interface TabelaInss {
  id: string
  vigenciaInicio: string
  fonte: string
  teto: number
  vigente: boolean
  faixas: FaixaInss[]
}

/** Parâmetro legal federal: a mesma tabela vale para todas as organizações. */
export const listarTabelasInss = (): Promise<TabelaInss[]> => obter('/api/tabelas-inss')

export interface TabelaFgts {
  id: string
  vigenciaInicio: string
  /** Fração: 8% chega como 0.08. */
  aliquota: number
  /** A mesma alíquota como percentual, para não refazer a conta na tela. */
  aliquotaPercentual: number
  fonte: string
  vigente: boolean
}

/** Também federal, e sem faixas: o FGTS é uma alíquota única sobre a base. */
export const listarTabelasFgts = (): Promise<TabelaFgts[]> => obter('/api/tabelas-fgts')

export interface FaixaIrrf {
  ordem: number
  limiteInferior: number
  /** Nulo na última: o IRRF não tem teto, ao contrário do INSS. */
  limiteSuperior: number | null
  aliquota: number
  aliquotaPercentual: number
  /** Subtraída depois da alíquota, para reproduzir a progressividade. */
  parcelaADeduzir: number
}

export interface TabelaIrrf {
  id: string
  vigenciaInicio: string
  fonte: string
  deducaoPorDependente: number
  descontoSimplificado: number
  redutorBase: number
  redutorCoeficiente: number
  /** A partir de qual rendimento o redutor zera. Derivado da fórmula. */
  limiteDoRedutor: number
  limiteIsencao: number
  temRedutor: boolean
  vigente: boolean
  faixas: FaixaIrrf[]
}

export const listarTabelasIrrf = (): Promise<TabelaIrrf[]> => obter('/api/tabelas-irrf')
