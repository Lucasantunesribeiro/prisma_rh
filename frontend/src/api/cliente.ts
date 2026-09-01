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

/**
 * O par do *double submit cookie* (Fase 10).
 *
 * Em produção o refresh usa `SameSite=None`, porque o frontend fica na Vercel e
 * a API na AWS — domínios diferentes, e `Lax` simplesmente não enviaria o
 * cookie. Só que `None` **reabre o CSRF que o `Lax` fechava de graça**: agora
 * qualquer site consegue fazer o navegador anexar o cookie.
 *
 * A defesa: um segundo cookie, este legível por JavaScript, que a tela repete
 * num cabeçalho. Um site atacante consegue fazer o navegador **enviar** o
 * cookie, mas a *same-origin policy* o impede de **ler** o valor para repetir
 * no cabeçalho — e sem os dois iguais o servidor recusa.
 */
export const COOKIE_CSRF = 'prismarh_csrf'
export const CABECALHO_CSRF = 'X-CSRF-Token'

function tokenCsrf(): string | null {
  // `document.cookie` é uma string única com tudo separado por `; `. Não há
  // API melhor, e por isso a leitura é feita à mão.
  const achado = document.cookie
    .split('; ')
    .find((c) => c.startsWith(`${COOKIE_CSRF}=`))

  return achado ? decodeURIComponent(achado.slice(COOKIE_CSRF.length + 1)) : null
}

/**
 * Cabeçalhos das rotas que dependem do cookie — `renovar` e `sair`.
 *
 * Em desenvolvimento o cookie é `Lax` e o servidor não exige o token; o
 * cabeçalho simplesmente não é enviado, e nada quebra.
 */
export function cabecalhosCsrf(): Record<string, string> {
  const token = tokenCsrf()

  return token ? { [CABECALHO_CSRF]: token } : {}
}

/** Tenta trocar o cookie de refresh por um novo access token. */
export async function renovarSessao(): Promise<boolean> {
  const resposta = await fetch(`${URL_BASE_API}${ROTAS_AUTENTICACAO}/renovar`, {
    method: 'POST',
    credentials: 'include',
    headers: { Accept: 'application/json', ...cabecalhosCsrf() },
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

/**
 * Envelope das listagens paginadas (Fase 10).
 *
 * `/api/folhas`, `/api/rubricas` e `/api/cargos` deixaram de devolver array e
 * passaram a devolver este formato — as três crescem sem limite natural, e o
 * `CLAUDE.md §24.19 item 3` as nomeava como vetor de exaustão.
 */
export interface PaginaApi<T> {
  total: number
  paginaAtual: number
  tamanho: number
  itens: T[]
}

/**
 * Lê uma listagem paginada e devolve só os itens.
 *
 * Pede o teto do servidor (200). As telas que consomem estas listagens são
 * seletores e catálogos — mostram tudo de uma vez, e paginar a interface delas
 * seria complicar o que hoje cabe numa lista. Se algum dia passarem de 200, a
 * tela precisará de paginação de verdade, e este ponto é onde isso aparece.
 */
export async function obterPaginado<T>(caminho: string): Promise<T[]> {
  const separador = caminho.includes('?') ? '&' : '?'
  const pagina = await obter<PaginaApi<T>>(`${caminho}${separador}tamanho=200`)

  return pagina.itens
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
