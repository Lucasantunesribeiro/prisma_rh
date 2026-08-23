import { afterEach, describe, expect, it, vi } from 'vitest'
import { consultarSaude, URL_BASE_API } from './saude'

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('consultarSaude', () => {
  it('consulta /health na URL base configurada e devolve o corpo JSON', async () => {
    const fetchSimulado = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          status: 'saudavel',
          verificacoes: [{ nome: 'banco-de-dados', status: 'saudavel', descricao: null }],
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      ),
    )

    vi.stubGlobal('fetch', fetchSimulado)

    const saude = await consultarSaude()

    expect(fetchSimulado).toHaveBeenCalledWith(`${URL_BASE_API}/health`, expect.anything())
    expect(saude.status).toBe('saudavel')
    expect(saude.verificacoes[0].nome).toBe('banco-de-dados')
  })
})
