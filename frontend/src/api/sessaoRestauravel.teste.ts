import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { guardarTokenCsrf, haSessaoRestauravel, renovarSessao } from './cliente'

/**
 * O curto-circuito da restauração de sessão.
 *
 * ## O defeito que isto corrige
 *
 * Toda **primeira visita** disparava `POST /renovar`, que voltava **403** da
 * `GuardaCsrf` — sem cookie não há token anti-CSRF para repetir no cabeçalho.
 * O comportamento estava correto, mas quem abrisse o console via um erro
 * vermelho logo ao entrar no site.
 *
 * Erro vermelho de rotina ensina a ignorar erro vermelho — e aí o próximo, que
 * importa, passa junto.
 *
 * ## Qual é o sinal certo
 *
 * ⚠️ **Não é o cookie.** Em produção a tela está na Vercel e a API na Lambda —
 * domínios diferentes —, e `document.cookie` é por origem: a tela nunca
 * enxergou aquele cookie. A primeira versão deste teste usava cookie e passava
 * em `jsdom`, que é *same-origin*; foi recarregar a página em produção para o
 * defeito aparecer.
 *
 * O sinal certo é o token do *double submit* guardado **nesta origem**, que
 * chega no corpo de `entrar` e de `renovar`. Sem ele não houve login nesta
 * aba, e não há refresh a restaurar.
 *
 * ⚠️ **A guarda do servidor não foi tocada.** O endpoint continua exigindo
 * tudo; o que mudou é a tela parar de bater numa porta que ela já sabe estar
 * trancada.
 */
beforeEach(() => guardarTokenCsrf(null))

afterEach(() => {
  vi.unstubAllGlobals()
  guardarTokenCsrf(null)
})

describe('restauração de sessão', () => {
  it('primeira visita, sem token guardado: NÃO chama a API', async () => {
    const fetchFalso = vi.fn()
    vi.stubGlobal('fetch', fetchFalso)

    expect(haSessaoRestauravel()).toBe(false)
    expect(await renovarSessao()).toBe(false)

    // ⚠️ O ponto do defeito: zero requisicao, zero 403 no console.
    expect(fetchFalso).not.toHaveBeenCalled()
  })

  it('sessão existente: chama a API normalmente', async () => {
    guardarTokenCsrf('token-de-teste')

    const fetchFalso = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ accessToken: 'a', expiraEm: new Date().toISOString() }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchFalso)

    expect(haSessaoRestauravel()).toBe(true)
    expect(await renovarSessao()).toBe(true)
    expect(fetchFalso).toHaveBeenCalledTimes(1)
  })

  it('token apagado depois de existir (logout): volta a não chamar', async () => {
    guardarTokenCsrf('token-de-teste')
    expect(haSessaoRestauravel()).toBe(true)

    guardarTokenCsrf(null)

    const fetchFalso = vi.fn()
    vi.stubGlobal('fetch', fetchFalso)

    expect(await renovarSessao()).toBe(false)
    expect(fetchFalso).not.toHaveBeenCalled()
  })

  it('renovação legítima que o servidor recusa devolve false, sem laço', async () => {
    guardarTokenCsrf('token-de-teste')

    const fetchFalso = vi.fn().mockResolvedValue(new Response(null, { status: 401 }))
    vi.stubGlobal('fetch', fetchFalso)

    expect(await renovarSessao()).toBe(false)

    // Uma tentativa, e só. O laço 401 -> renovar -> 401 nunca começa.
    expect(fetchFalso).toHaveBeenCalledTimes(1)
  })

  it('token vazio conta como ausente', async () => {
    guardarTokenCsrf('   ')

    const fetchFalso = vi.fn()
    vi.stubGlobal('fetch', fetchFalso)

    expect(haSessaoRestauravel()).toBe(false)
    expect(fetchFalso).not.toHaveBeenCalled()
  })
})
