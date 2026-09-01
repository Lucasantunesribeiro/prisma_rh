import { enviar, obter } from './cliente'

// A camada de IA (Fase 11).

export type SituacaoIa =
  | 'Respondeu'
  | 'NaoConfigurada'
  | 'LimiteAtingido'
  | 'Indisponivel'
  | 'Recusada'

export interface Explicacao {
  situacao: SituacaoIa
  texto: string

  /**
   * Sempre `true` quando há texto.
   *
   * A tela é **obrigada** a rotular (`CLAUDE.md §37.3`): o leitor precisa saber
   * que aquilo foi escrito por máquina e pode estar errado. Sem o rótulo, um
   * texto gerado passa por apuração do sistema.
   */
  geradoPorIa: boolean

  doCache: boolean
  aviso: string
}

/**
 * O assistente está configurado neste ambiente?
 *
 * A tela pergunta antes de mostrar o botão. Sem IA, o produto funciona igual —
 * ela é acessório, e não requisito (`CLAUDE.md §1`).
 */
export async function assistenteDisponivel(): Promise<boolean> {
  const resposta = await obter<{ disponivel: boolean }>('/api/assistente/disponivel')

  return resposta.disponivel
}

/**
 * Pede a explicação de uma inconsistência já detectada.
 *
 * Falha do provedor **não** é erro de tela: a API devolve 200 com o motivo
 * dentro, e o analista continua com a descrição que o motor determinístico
 * gerou — que é a informação que importa.
 */
export async function explicarInconsistencia(id: string): Promise<Explicacao> {
  return enviar<Explicacao>(`/api/assistente/inconsistencias/${id}/explicacao`, {})
}

// ------------------------------------------------------------- Fase 11B

export interface ContagemResumo {
  rotulo: string
  quantidade: number
}

/**
 * ⚠️ **Todos estes números vêm de consulta determinística no backend**, e não
 * do modelo (`CLAUDE.md §37.3`). A tela mostra o retrato ao lado da prosa
 * justamente para que uma divergência entre os dois seja visível.
 */
export interface RetratoDaFolha {
  competencia: string
  tipo: string
  situacao: string
  versaoCalculo: number
  holerites: number
  totalProventos: number
  totalDescontos: number
  totalLiquido: number
  inconsistencias: number
  pendentes: number
  porSeveridade: ContagemResumo[]
  porCategoria: ContagemResumo[]
  competenciaAnterior: string | null
  variacaoLiquido: number | null
  inconsistenciasAnterior: number | null
}

export interface ResumoExecutivo {
  situacao: SituacaoIa
  retrato: RetratoDaFolha
  texto: string
  geradoPorIa: boolean
  doCache: boolean
  aviso: string
}

/**
 * O resumo executivo da folha.
 *
 * O retrato vem **sempre**, mesmo com a IA fora do ar — ele não depende do
 * modelo. O que se perde nesse caso é o parágrafo.
 */
export async function resumirFolha(idFolha: string): Promise<ResumoExecutivo> {
  return enviar<ResumoExecutivo>(`/api/assistente/folhas/${idFolha}/resumo`, {})
}

// ------------------------------------------------------------- Fase 11C

export interface CampoConsulta {
  campo: string
  significado: string
  comparacoes: string[]
  valores: string[]
}

export interface AchadoConsulta {
  id: string
  codigo: string
  regra: string
  categoria: string
  severidade: string
  status: string
  descricao: string
  valorEncontrado: number | null
  diferenca: number | null
}

export interface RespostaConsulta {
  situacao: SituacaoIa | 'NaoEntendida'

  /** Em que a pergunta virou. A tela mostra — sem isso ninguém confere. */
  entendido: string[]

  /** O que o modelo propôs e o vocabulário barrou. */
  naoEntendido: string[]

  total: number
  truncado: boolean
  itens: AchadoConsulta[]
  aviso: string
}

/** Os campos que uma pergunta pode usar. Sem isso a busca vira adivinhação. */
export async function vocabularioConsulta(): Promise<{
  disponivel: boolean
  campos: CampoConsulta[]
}> {
  return obter('/api/assistente/consultas/vocabulario')
}

/**
 * Pergunta em português.
 *
 * O modelo **propõe** um filtro; o backend confere campo, comparação e tipo, e
 * só então monta a consulta — sempre sob o filtro global de organização.
 */
export async function consultarEmPortugues(pergunta: string): Promise<RespostaConsulta> {
  return enviar<RespostaConsulta>('/api/assistente/consultas', { pergunta })
}
