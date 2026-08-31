import { act, renderHook, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { TrabalhoAssincrono } from '@/api/importacoes'
import { INTERVALO_MS, MAXIMO_TENTATIVAS, useTrabalho } from './useTrabalho'

vi.mock('@/api/importacoes', async (original) => ({
  ...(await original<typeof import('@/api/importacoes')>()),
  obterTrabalho: vi.fn(),
}))

const api = await import('@/api/importacoes')

const BASE: TrabalhoAssincrono = {
  id: 't1',
  tipo: 'ImportacaoFuncionarios',
  status: 'Enfileirado',
  pendente: true,
  tentativas: 0,
  idRecurso: null,
  erro: null,
  criadoEm: '2026-08-31T12:00:00Z',
  concluidoEm: null,
}

const concluido: TrabalhoAssincrono = {
  ...BASE,
  status: 'Concluido',
  pendente: false,
  tentativas: 1,
  idRecurso: 'imp-1',
  concluidoEm: '2026-08-31T12:00:10Z',
}

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true })
})

afterEach(() => {
  vi.useRealTimers()
  vi.clearAllMocks()
})

describe('useTrabalho — acompanhamento de trabalho assíncrono', () => {
  it('não pergunta nada quando não há trabalho', () => {
    renderHook(() => useTrabalho(null))

    expect(api.obterTrabalho).not.toHaveBeenCalled()
  })

  it('para de perguntar assim que o trabalho conclui', async () => {
    vi.mocked(api.obterTrabalho).mockResolvedValue(concluido)

    const { result } = renderHook(() => useTrabalho('t1'))

    await waitFor(() => expect(result.current.trabalho?.status).toBe('Concluido'))

    expect(result.current.acompanhando).toBe(false)
    expect(api.obterTrabalho).toHaveBeenCalledTimes(1)
  })

  it('continua perguntando enquanto está pendente', async () => {
    vi.mocked(api.obterTrabalho)
      .mockResolvedValueOnce(BASE)
      .mockResolvedValueOnce({ ...BASE, status: 'Processando' })
      .mockResolvedValue(concluido)

    const { result } = renderHook(() => useTrabalho('t1'))

    await waitFor(() => expect(api.obterTrabalho).toHaveBeenCalledTimes(1))

    await act(async () => {
      await vi.advanceTimersByTimeAsync(INTERVALO_MS)
    })
    await act(async () => {
      await vi.advanceTimersByTimeAsync(INTERVALO_MS)
    })

    await waitFor(() => expect(result.current.trabalho?.pendente).toBe(false))
    expect(result.current.acompanhando).toBe(false)
  })

  /**
   * ⚠️ Sem teto, uma aba esquecida aberta pergunta para sempre.
   *
   * A Lambda tem timeout de 60 s e no máximo 3 tentativas — cinco minutos
   * cobrem o pior caso. Passar disso significa que algo travou, e a tela
   * precisa dizer isso em vez de girar eternamente.
   */
  it('desiste depois do teto de tentativas', async () => {
    vi.mocked(api.obterTrabalho).mockResolvedValue(BASE)

    const { result } = renderHook(() => useTrabalho('t1'))

    await act(async () => {
      await vi.advanceTimersByTimeAsync(INTERVALO_MS * (MAXIMO_TENTATIVAS + 2))
    })

    await waitFor(() => expect(result.current.desistiu).toBe(true))
    expect(result.current.acompanhando).toBe(false)
  })

  /**
   * Falha de rede não encerra o acompanhamento na primeira: a API pode ter
   * piscado. O erro fica visível e a próxima pergunta acontece.
   */
  it('erro de rede não encerra o acompanhamento', async () => {
    vi.mocked(api.obterTrabalho)
      .mockRejectedValueOnce(new Error('rede fora'))
      .mockResolvedValue(concluido)

    const { result } = renderHook(() => useTrabalho('t1'))

    await waitFor(() => expect(result.current.erro).toBe('rede fora'))
    expect(result.current.acompanhando).toBe(true)

    await act(async () => {
      await vi.advanceTimersByTimeAsync(INTERVALO_MS)
    })

    await waitFor(() => expect(result.current.trabalho?.status).toBe('Concluido'))
  })

  /**
   * Sair da tela encerra o laço. Sem isto, navegar para outra página deixaria o
   * polling rodando contra um componente que não existe mais.
   */
  it('desmontar interrompe o polling', async () => {
    vi.mocked(api.obterTrabalho).mockResolvedValue(BASE)

    const { unmount } = renderHook(() => useTrabalho('t1'))

    await waitFor(() => expect(api.obterTrabalho).toHaveBeenCalledTimes(1))

    unmount()

    await act(async () => {
      await vi.advanceTimersByTimeAsync(INTERVALO_MS * 5)
    })

    expect(api.obterTrabalho).toHaveBeenCalledTimes(1)
  })
})
