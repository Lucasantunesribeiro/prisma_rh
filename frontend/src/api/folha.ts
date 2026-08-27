import { enviar, obter, remover } from './cliente'
import type { Perfil } from './autenticacao'

// ---------------------------------------------------------------- rubricas

export type TipoRubrica = 'Provento' | 'Desconto' | 'Informativo'
export type EstrategiaRubrica = 'SalarioBaseProporcional' | 'ValorInformado'
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
  Provento: 'provento',
  Desconto: 'desconto',
  Informativo: 'informativo',
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
  Rascunho: 'rascunho',
  Calculada: 'calculada',
  Fechada: 'fechada',
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
