import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { definirAccessToken, obter, registrarPerdaDeSessao } from './cliente'

function responder(corpo: unknown, status = 200): Response {
  return new Response(JSON.stringify(corpo), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

beforeEach(() => {
  definirAccessToken(null)
  registrarPerdaDeSessao(null)
})

afterEach(() => {
  vi.unstubAllGlobals()
  definirAccessToken(null)
})

describe('cliente HTTP', () => {
  it('envia o access token no cabecalho e o cookie junto', async () => {
    const fetchSimulado = vi.fn().mockResolvedValue(responder({ ok: true }))
    vi.stubGlobal('fetch', fetchSimulado)

    definirAccessToken('token-abc')
    await obter('/api/empresas')

    const [, opcoes] = fetchSimulado.mock.calls[0]
    expect(opcoes.credentials).toBe('include')
    expect(opcoes.headers.Authorization).toBe('Bearer token-abc')
  })

  it('renova UMA vez ao receber 401 e repete a chamada original', async () => {
    const fetchSimulado = vi
      .fn()
      // 1) chamada original -> 401
      .mockResolvedValueOnce(responder({ title: 'expirado' }, 401))
      // 2) renovacao -> novo token
      .mockResolvedValueOnce(responder({ accessToken: 'token-novo' }))
      // 3) repeticao -> sucesso
      .mockResolvedValueOnce(responder({ total: 0, itens: [] }))

    vi.stubGlobal('fetch', fetchSimulado)
    definirAccessToken('token-velho')

    const resultado = await obter<{ total: number }>('/api/empresas')

    expect(resultado.total).toBe(0)
    expect(fetchSimulado).toHaveBeenCalledTimes(3)
    expect(String(fetchSimulado.mock.calls[1][0])).toContain('/api/autenticacao/renovar')

    // A repeticao precisa usar o token NOVO, nao o que ja falhou.
    expect(fetchSimulado.mock.calls[2][1].headers.Authorization).toBe('Bearer token-novo')
  })

  it('nao entra em laco quando a renovacao tambem falha', async () => {
    const fetchSimulado = vi
      .fn()
      .mockResolvedValueOnce(responder({ title: 'expirado' }, 401))
      .mockResolvedValueOnce(responder({ title: 'sessao morta' }, 401))

    vi.stubGlobal('fetch', fetchSimulado)

    const perdeuSessao = vi.fn()
    registrarPerdaDeSessao(perdeuSessao)
    definirAccessToken('token-velho')

    await expect(obter('/api/empresas')).rejects.toThrow()

    // Original + renovacao. Nada de terceira tentativa, nada de repeticao infinita.
    expect(fetchSimulado).toHaveBeenCalledTimes(2)
    expect(perdeuSessao).toHaveBeenCalledOnce()
  })

  it('nao tenta renovar quando o proprio login devolve 401', async () => {
    const fetchSimulado = vi.fn().mockResolvedValue(responder({ detail: 'invalida' }, 401))
    vi.stubGlobal('fetch', fetchSimulado)

    await expect(obter('/api/autenticacao/eu')).rejects.toThrow()

    // Renovar dentro da propria rota de autenticacao criaria recursao.
    expect(fetchSimulado).toHaveBeenCalledTimes(1)
  })
})
