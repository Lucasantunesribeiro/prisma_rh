import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import Status from './Status'
import type { RespostaSaude } from '@/api/saude'

function responderCom(corpo: RespostaSaude, status = 200): Response {
  return new Response(JSON.stringify(corpo), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('Status', () => {
  it('mostra API e banco disponiveis quando a API responde saudavel', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        responderCom({
          status: 'saudavel',
          verificacoes: [{ nome: 'banco-de-dados', status: 'saudavel', descricao: null }],
        }),
      ),
    )

    render(<Status />)

    expect(await screen.findByText('Sistema operacional')).toBeInTheDocument()
    expect(screen.getByText('API')).toBeInTheDocument()
    expect(screen.getByText('Banco de dados')).toBeInTheDocument()
    expect(screen.getAllByText('disponível')).toHaveLength(2)
  })

  it('NAO quebra com a resposta MINIMA de producao (sem verificacoes)', async () => {
    // ⚠️ Regressao de 02/09/2026: producao devolve so `{status}` (rota anonima
    // nao revela topologia), e a tela fazia `.find` num `verificacoes`
    // undefined, derrubando o app inteiro. O corpo abaixo e o que a producao
    // realmente manda; a tela tem de renderizar e mostrar o banco como saudavel
    // por proxy do status geral.
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(responderCom({ status: 'saudavel' } as RespostaSaude)),
    )

    render(<Status />)

    expect(await screen.findByText('Sistema operacional')).toBeInTheDocument()
    expect(screen.getByText('Banco de dados')).toBeInTheDocument()
    expect(screen.getAllByText('disponível')).toHaveLength(2)
  })

  it('mostra o banco indisponivel quando apenas o banco falha', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        responderCom(
          {
            status: 'indisponivel',
            verificacoes: [{ nome: 'banco-de-dados', status: 'indisponivel', descricao: null }],
          },
          503,
        ),
      ),
    )

    render(<Status />)

    expect(await screen.findByText('indisponível')).toBeInTheDocument()
    expect(screen.getByText('disponível')).toBeInTheDocument()
    expect(screen.getByText('Sistema com indisponibilidade')).toBeInTheDocument()
  })

  it('mostra estado de erro e permite tentar novamente quando a API nao responde', async () => {
    const fetchSimulado = vi
      .fn()
      .mockRejectedValueOnce(new Error('Failed to fetch'))
      .mockResolvedValueOnce(
        responderCom({
          status: 'saudavel',
          verificacoes: [{ nome: 'banco-de-dados', status: 'saudavel', descricao: null }],
        }),
      )

    vi.stubGlobal('fetch', fetchSimulado)

    render(<Status />)

    expect(
      await screen.findByText(/Não foi possível acessar a API/i),
    ).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: /tentar novamente/i }))

    await waitFor(() => {
      expect(screen.getByText('Sistema operacional')).toBeInTheDocument()
    })
  })
})
