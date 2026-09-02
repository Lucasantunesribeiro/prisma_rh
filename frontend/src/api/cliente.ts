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

/**
 * Mensagem para as respostas que chegam **sem corpo**.
 *
 * O limitador de taxa do ASP.NET devolve só o **429**, sem `ProblemDetails` —
 * e `Falha na requisicao (429)` não diz a quem está usando o que aconteceu nem
 * o que fazer. Num portfólio público isso importa mais que o normal: a cota de
 * IA é **por organização**, e todos os visitantes da demonstração entram pela
 * mesma conta, então dividem o mesmo teto.
 *
 * ⚠️ A mensagem diz **espere**, e não "tente de novo": repetir na hora é o que
 * mantém a janela cheia.
 */
function mensagemPadrao(status: number): string {
  if (status === 429) {
    return 'Muitas requisições em pouco tempo. Espere um instante antes de repetir.'
  }

  if (status === 503) {
    return 'O serviço está indisponível no momento.'
  }

  return `Falha na requisicao (${status}).`
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

/**
 * Onde o token do *double submit* fica **nesta origem**.
 *
 * ## Por que não dá para ler do cookie
 *
 * ⚠️ Descoberto em 02/09/2026, recarregando a página em produção: a sessão
 * caía. A leitura era `document.cookie`, e `document.cookie` é **por origem**.
 *
 * ```text
 * tela  ->  portfolio-prisma-rh.vercel.app
 * API   ->  ...lambda-url.us-east-1.on.aws     <- o cookie mora AQUI
 * ```
 *
 * A página da Vercel nunca enxergou aquele cookie. Funcionava em
 * desenvolvimento — onde tudo é `localhost` — e **nunca** funcionou publicado:
 * `renovar` e `sair` respondiam 403, e um F5 deslogava.
 *
 * O navegador continua **enviando** o cookie sozinho (`SameSite=None`), então
 * o servidor tem a metade dele. O que faltava era a nossa metade, e ela agora
 * chega **no corpo** de `entrar` e `renovar`.
 *
 * ## Por que `sessionStorage`, e não memória
 *
 * Memória morre no F5, que é exatamente o momento em que o token é preciso
 * para pedir a renovação. `sessionStorage` sobrevive ao recarregamento e morre
 * ao fechar a aba — mais curto que `localStorage`, e suficiente.
 *
 * ⚠️ Não enfraquece o double submit. A proteção é o site atacante **não
 * descobrir o valor**, e ele continua sem: não lê esta origem, não lê o cookie
 * (agora `HttpOnly`) e não lê a resposta (o CORS tem allowlist).
 */
const CHAVE_CSRF = 'prismarh.csrf'

/** Acesso tolerante: navegador com armazenamento bloqueado não pode quebrar a tela. */
function armazenamento(): Storage | null {
  try {
    return window.sessionStorage
  } catch {
    return null
  }
}

export function guardarTokenCsrf(token: string | null): void {
  const alvo = armazenamento()

  if (!alvo) return

  try {
    if (token) alvo.setItem(CHAVE_CSRF, token)
    else alvo.removeItem(CHAVE_CSRF)
  } catch {
    // Cota cheia ou modo restrito. A tela continua funcionando; o que se perde
    // e a restauracao apos recarregar, e nao a sessao atual.
  }
}

function tokenCsrf(): string | null {
  try {
    const valor = armazenamento()?.getItem(CHAVE_CSRF)?.trim()

    return valor ? valor : null
  } catch {
    return null
  }
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

/**
 * Há sessão que valha a pena tentar restaurar?
 *
 * ## Por que isto existe
 *
 * Toda primeira visita disparava `POST /renovar`, que voltava **403** — sem o
 * par do *double submit* não há o que repetir no cabeçalho. O comportamento
 * estava certo, mas quem abrisse o console via um erro vermelho na cara logo
 * ao entrar no site, e erro vermelho de rotina ensina a ignorar erro vermelho.
 *
 * ## Qual é o sinal certo
 *
 * O token guardado nesta origem, e **não** o cookie: em produção a tela e a
 * API estão em domínios diferentes, e a tela nunca enxergou aquele cookie. Ver
 * `CHAVE_CSRF`.
 *
 * Sem token guardado não houve login nesta aba, então não há refresh a
 * restaurar e a chamada seria uma ida à rede garantidamente perdida.
 *
 * ⚠️ **Isto não enfraquece a guarda.** O endpoint continua exigindo tudo o que
 * exigia; o que muda é a tela parar de bater numa porta que ela já sabe estar
 * trancada. Um atacante não ganha nada — ele nunca dependeu desta função.
 */
export function haSessaoRestauravel(): boolean {
  return tokenCsrf() !== null
}

/** Tenta trocar o cookie de refresh por um novo access token. */
export async function renovarSessao(): Promise<boolean> {
  // Sem o par do double submit, `renovar` responderia 403. Poupa a requisicao
  // e o erro no console (ver `haSessaoRestauravel`).
  if (!haSessaoRestauravel()) {
    return false
  }

  const resposta = await fetch(`${URL_BASE_API}${ROTAS_AUTENTICACAO}/renovar`, {
    method: 'POST',
    credentials: 'include',
    headers: { Accept: 'application/json', ...cabecalhosCsrf() },
  })

  if (!resposta.ok) {
    definirAccessToken(null)

    // ⚠️ Limpa o token do double submit ao falhar.
    //
    // O token vive no `sessionStorage`, que SOBREVIVE ao cookie de refresh.
    // Quando a família de refresh morre (expira, é rotacionada ou derrubada por
    // reúso) mas o token velho continua guardado, `haSessaoRestauravel()` acha
    // que há sessão, dispara `renovar` e leva 403 a cada navegação. Apagar aqui
    // faz a sessão morta parar de bater numa porta já trancada, depois de UMA
    // tentativa — em vez de um 403 repetido no console.
    guardarTokenCsrf(null)
    return false
  }

  const sessao = (await resposta.json()) as { accessToken: string; tokenCsrf?: string }

  // ⚠️ O token do double submit ROTACIONA junto com o refresh. Guardar o novo
  // e obrigatorio: manter o antigo faria a renovacao seguinte falhar com 403.
  if (sessao.tokenCsrf) {
    guardarTokenCsrf(sessao.tokenCsrf)
  }

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
    corpo.detail ?? corpo.title ?? corpo.detalhe ?? mensagemPadrao(resposta.status),
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
