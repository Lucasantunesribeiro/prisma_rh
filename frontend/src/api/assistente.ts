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
