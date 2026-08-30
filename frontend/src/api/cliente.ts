/**
 * Cliente HTTP do Prisma RH.
 *
 * Duas regras que sustentam a seguranca da sessao:
 *
 * 1. O access token vive SO em memoria. Nada de localStorage: qualquer XSS
 *    leria o localStorage e roubaria a sessao inteira.
 * 2. O refresh token nunca passa por aqui. Ele mora num cookie httpOnly que o
 *    JavaScript nao enxerga; `credentials: 'include'` faz o navegador envia-lo
 *    sozinho para as rotas de autenticacao.
 */

export const URL_BASE_API = import.meta.env.VITE_API_URL ?? 'http://localhost:5080'

let accessToken: string | null = null
let aoPerderSessao: (() => void) | null = null

export function definirAccessToken(token: string | null): void {
  accessToken = token
}

export function obterAccessToken(): string | null {
  return accessToken
}

export function registrarPerdaDeSessao(callback: (() => void) | null): void {
  aoPerderSessao = callback
}

export class ErroApi extends Error {
  // Campos declarados fora do construtor: o tsconfig usa erasableSyntaxOnly,
  // que proibe propriedades de parametro por nao serem apagaveis na compilacao.
  readonly status: number
  readonly detalhes?: Record<string, string[]>

  constructor(status: number, mensagem: string, detalhes?: Record<string, string[]>) {
    super(mensagem)
    this.name = 'ErroApi'
    this.status = status
    this.detalhes = detalhes
  }
}

const ROTAS_AUTENTICACAO = '/api/autenticacao'

async function chamar(caminho: string, opcoes: RequestInit, jaRenovou: boolean): Promise<Response> {
  // FormData define o proprio Content-Type, COM a fronteira do multipart.
  // Escrever 'application/json' por cima quebraria o envio de arquivo de um
  // jeito silencioso: o servidor receberia um corpo que nao consegue separar.
  const ehFormulario = typeof FormData !== 'undefined' && opcoes.body instanceof FormData

  const cabecalhos: Record<string, string> = {
    Accept: 'application/json',
    ...(opcoes.body && !ehFormulario ? { 'Content-Type': 'application/json' } : {}),
    ...((opcoes.headers as Record<string, string>) ?? {}),
  }

  if (accessToken) {
    cabecalhos.Authorization = `Bearer ${accessToken}`
  }

  const resposta = await fetch(URL_BASE_API + caminho, {
    ...opcoes,
    credentials: 'include',
    headers: cabecalhos,
  })

  // Uma unica tentativa de renovacao, nunca um laco: se a renovacao tambem
  // devolver 401, insistir so multiplicaria requisicoes ate o navegador travar.
  if (resposta.status === 401 && !jaRenovou && !caminho.startsWith(ROTAS_AUTENTICACAO)) {
    const renovou = await renovarSessao()

    if (renovou) {
      return chamar(caminho, opcoes, true)
    }

    definirAccessToken(null)
    aoPerderSessao?.()
  }

  return resposta
}

/** Tenta trocar o cookie de refresh por um novo access token. */
export async function renovarSessao(): Promise<boolean> {
  const resposta = await fetch(`${URL_BASE_API}${ROTAS_AUTENTICACAO}/renovar`, {
    method: 'POST',
    credentials: 'include',
    headers: { Accept: 'application/json' },
  })

  if (!resposta.ok) {
    definirAccessToken(null)
    return false
  }

  const sessao = (await resposta.json()) as { accessToken: string }
  definirAccessToken(sessao.accessToken)
  return true
}

async function interpretar<T>(resposta: Response): Promise<T> {
  if (resposta.status === 204) {
    return undefined as T
  }

  const texto = await resposta.text()
  const corpo = texto ? JSON.parse(texto) : {}

  if (resposta.ok) {
    return corpo as T
  }

  throw new ErroApi(
    resposta.status,
    corpo.detail ?? corpo.title ?? corpo.detalhe ?? `Falha na requisicao (${resposta.status}).`,
    corpo.errors,
  )
}

export async function obter<T>(caminho: string): Promise<T> {
  return interpretar<T>(await chamar(caminho, { method: 'GET' }, false))
}

export async function enviar<T>(caminho: string, corpo: unknown, metodo = 'POST'): Promise<T> {
  return interpretar<T>(
    await chamar(caminho, { method: metodo, body: JSON.stringify(corpo) }, false),
  )
}

export async function remover(caminho: string): Promise<void> {
  await interpretar<void>(await chamar(caminho, { method: 'DELETE' }, false))
}

/**
 * POST de arquivo, como multipart.
 *
 * Passa pelo mesmo `chamar` do resto: renovacao de sessao, cabecalho de
 * autorizacao e tratamento de erro sao os mesmos. So o corpo muda.
 */
export async function enviarArquivo<T>(caminho: string, formulario: FormData): Promise<T> {
  return interpretar<T>(await chamar(caminho, { method: 'POST', body: formulario }, false))
}

/**
 * GET de conteudo binario.
 *
 * Existe porque o access token vive SO em memoria: um `<a href>` apontando para
 * a API sairia sem o cabecalho Authorization e voltaria 401. O jeito de baixar
 * um arquivo autenticado e buscar por fetch e entregar o blob ao navegador.
 */
export async function obterArquivo(caminho: string): Promise<Blob> {
  const resposta = await chamar(caminho, { method: 'GET' }, false)

  if (!resposta.ok) {
    throw new ErroApi(resposta.status, `Nao foi possivel baixar o arquivo (${resposta.status}).`)
  }

  return resposta.blob()
}
