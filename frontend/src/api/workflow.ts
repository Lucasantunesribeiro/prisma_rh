import type { CategoriaRegra, Severidade } from './analises'
import type { Perfil } from './autenticacao'
import { enviar, obter } from './cliente'

export type StatusInconsistencia =
  | 'Detectada'
  | 'EmAnalise'
  | 'Justificada'
  | 'Corrigida'
  | 'Resolvida'

export type TipoAndamento = 'Comentario' | 'Transicao' | 'Atribuicao' | 'Evidencia'

export interface Andamento {
  id: string
  tipo: TipoAndamento
  autor: string | null
  ocorridoEm: string
  /**
   * ⚠️ Texto escrito por outro usuário.
   *
   * Renderizado como TEXTO, sempre. O React escapa por padrão, e
   * `dangerouslySetInnerHTML` é proibido no projeto (`CLAUDE.md §24.9`) — não há
   * um único uso dele em nenhuma tela.
   */
  texto: string | null
  statusAnterior: StatusInconsistencia | null
  statusNovo: StatusInconsistencia | null
  responsavelAnterior: string | null
  responsavelNovo: string | null
}

export interface Inconsistencia {
  id: string
  idFolha: string
  competencia: string
  codigo: string
  regra: string
  versaoRegra: number
  categoria: CategoriaRegra
  severidade: Severidade
  status: StatusInconsistencia
  pendente: boolean
  /**
   * Para onde dá para ir a partir daqui — vem do **servidor**.
   *
   * A tela não repete a máquina de estados: duas cópias divergiriam, e a da
   * tela é a que ninguém testa.
   */
  proximosStatus: StatusInconsistencia[]
  idResponsavel: string | null
  responsavel: string | null
  justificativa: string | null
  concluidaEm: string | null
  idFolhaFuncionario: string | null
  matricula: string | null
  nomeFuncionario: string | null
  descricao: string
  valorEsperado: number | null
  valorEncontrado: number | null
  diferenca: number | null
  andamentos: Andamento[] | null
}

export interface PaginaInconsistencias {
  total: number
  pagina: number
  itens: Inconsistencia[]
}

export interface EventoAuditoria {
  id: string
  acao: string
  entidade: string
  idEntidade: string
  usuario: string | null
  descricao: string
  contexto: string | null
  ocorridoEm: string
}

export interface PaginaEventos {
  total: number
  pagina: number
  itens: EventoAuditoria[]
}

export interface ContagemPorRotulo {
  rotulo: string
  quantidade: number
}

export interface PendenciaPorResponsavel {
  idResponsavel: string | null
  responsavel: string
  quantidade: number
}

export interface EvolucaoCompetencia {
  competencia: string
  folhas: number
  inconsistencias: number
  resolvidas: number
}

export interface Painel {
  folhasCalculadas: number
  folhasFechadas: number
  inconsistenciasTotais: number
  inconsistenciasPendentes: number
  inconsistenciasResolvidas: number
  /** Nulo quando não há inconsistência nenhuma — não é 100%, é "não há o que medir". */
  percentualConformidade: number | null
  porSeveridade: ContagemPorRotulo[]
  porStatus: ContagemPorRotulo[]
  porRegra: ContagemPorRotulo[]
  porResponsavel: PendenciaPorResponsavel[]
  evolucao: EvolucaoCompetencia[]
}

export const ROTULO_STATUS: Record<StatusInconsistencia, string> = {
  Detectada: 'Detectada',
  EmAnalise: 'Em análise',
  Justificada: 'Justificada',
  Corrigida: 'Corrigida',
  Resolvida: 'Resolvida',
}

export const TOM_STATUS: Record<
  StatusInconsistencia,
  'critico' | 'atencao' | 'info' | 'sucesso' | 'neutro'
> = {
  Detectada: 'critico',
  EmAnalise: 'atencao',
  Justificada: 'info',
  Corrigida: 'info',
  Resolvida: 'sucesso',
}

/**
 * O que a transição significa, em uma frase.
 *
 * `Justificada` e `Corrigida` levam as duas ao mesmo lugar, e a diferença entre
 * elas é a informação que mais importa depois: "quantas divergências eram erro
 * de verdade?".
 */
export const EXPLICACAO_STATUS: Record<StatusInconsistencia, string> = {
  Detectada: 'O motor encontrou. Ninguém olhou ainda.',
  EmAnalise: 'Alguém assumiu e está conferindo.',
  Justificada: 'Estava certo, e o motivo está escrito.',
  Corrigida: 'Estava errado e foi arrumado.',
  Resolvida: 'Encerrada. Pode ser reaberta.',
}

export const ROTULO_ANDAMENTO: Record<TipoAndamento, string> = {
  Comentario: 'Comentário',
  Transicao: 'Mudança de status',
  Atribuicao: 'Responsável',
  Evidencia: 'Evidência',
}

/**
 * Quem trata inconsistência.
 *
 * "Auditor lê tudo e não altera dado operacional" — Security Gate da Fase 7.
 * Esconder é conforto visual; quem barra é a política do backend, e há teste de
 * integração provando o 403.
 */
export function podeTratar(perfil: Perfil | undefined): boolean {
  return (
    perfil === 'AdministradorPlataforma' ||
    perfil === 'AdministradorEmpresa' ||
    perfil === 'AnalistaRh'
  )
}

export interface FiltroInconsistencias {
  status?: StatusInconsistencia
  severidade?: Severidade
  idFolha?: string
  idResponsavel?: string
  pendentes?: boolean
  pagina?: number
}

export async function listarInconsistencias(
  filtro: FiltroInconsistencias = {},
): Promise<PaginaInconsistencias> {
  const busca = new URLSearchParams()

  if (filtro.status) busca.set('status', filtro.status)
  if (filtro.severidade) busca.set('severidade', filtro.severidade)
  if (filtro.idFolha) busca.set('idFolha', filtro.idFolha)
  if (filtro.idResponsavel) busca.set('idResponsavel', filtro.idResponsavel)
  if (filtro.pendentes) busca.set('pendentes', 'true')
  if (filtro.pagina) busca.set('pagina', String(filtro.pagina))

  const consulta = busca.toString()

  return obter<PaginaInconsistencias>(`/api/inconsistencias${consulta ? `?${consulta}` : ''}`)
}

export async function obterInconsistencia(id: string): Promise<Inconsistencia> {
  return obter<Inconsistencia>(`/api/inconsistencias/${id}`)
}

export async function transitar(
  id: string,
  status: StatusInconsistencia,
  texto?: string,
): Promise<Inconsistencia> {
  return enviar<Inconsistencia>(`/api/inconsistencias/${id}/status`, { status, texto })
}

export async function atribuir(id: string, idResponsavel: string | null): Promise<Inconsistencia> {
  return enviar<Inconsistencia>(`/api/inconsistencias/${id}/responsavel`, { idResponsavel })
}

export async function comentar(id: string, texto: string): Promise<Inconsistencia> {
  return enviar<Inconsistencia>(`/api/inconsistencias/${id}/comentarios`, { texto })
}

export async function registrarEvidencia(id: string, texto: string): Promise<Inconsistencia> {
  return enviar<Inconsistencia>(`/api/inconsistencias/${id}/evidencias`, { texto })
}

export async function listarAuditoria(filtro: {
  acao?: string
  entidade?: string
  pagina?: number
} = {}): Promise<PaginaEventos> {
  const busca = new URLSearchParams()

  if (filtro.acao) busca.set('acao', filtro.acao)
  if (filtro.entidade) busca.set('entidade', filtro.entidade)
  if (filtro.pagina) busca.set('pagina', String(filtro.pagina))

  const consulta = busca.toString()

  return obter<PaginaEventos>(`/api/auditoria${consulta ? `?${consulta}` : ''}`)
}

export async function obterPainel(idEmpresa?: string): Promise<Painel> {
  return obter<Painel>(`/api/painel${idEmpresa ? `?idEmpresa=${idEmpresa}` : ''}`)
}
