import { render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { LimiteDeErro } from './LimiteDeErro'

/**
 * Prova que um erro de render de um filho vira fallback contido, e nao a queda
 * do app inteiro. Foi essa ausencia que fez `/status` apagar a tela toda em
 * 02/09/2026.
 */
function Explode(): never {
  throw new Error('boom')
}

describe('LimiteDeErro', () => {
  beforeEach(() => {
    // React loga o erro capturado no console; silencia para nao poluir a suite.
    vi.spyOn(console, 'error').mockImplementation(() => {})
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('mostra o fallback quando um filho lanca no render', () => {
    render(
      <LimiteDeErro>
        <Explode />
      </LimiteDeErro>,
    )

    expect(screen.getByRole('alert')).toBeInTheDocument()
    expect(screen.getByText('Algo deu errado nesta página')).toBeInTheDocument()
  })

  it('renderiza os filhos normalmente quando nao ha erro', () => {
    render(
      <LimiteDeErro>
        <p>conteudo saudavel</p>
      </LimiteDeErro>,
    )

    expect(screen.getByText('conteudo saudavel')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })
})
