import { render, screen } from '@testing-library/react'
import { lazy, Suspense } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ehFalhaDeDownload } from '@/lib/falhaDeDownload'
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

  it('falha ao BAIXAR a pagina sob demanda vira convite a recarregar, e nao tela branca', async () => {
    // Exatamente o que o React.lazy recebe quando o pedaco da rota nao chega:
    // rede caiu, ou um deploy trocou o nome do arquivo com a aba aberta.
    const PaginaQueNaoBaixa = lazy(() =>
      Promise.reject(
        new TypeError('Failed to fetch dynamically imported module: /assets/Folhas-abc123.js'),
      ),
    )

    render(
      <LimiteDeErro>
        <Suspense fallback={<p>abrindo</p>}>
          <PaginaQueNaoBaixa />
        </Suspense>
      </LimiteDeErro>,
    )

    expect(await screen.findByText('Não foi possível abrir esta página')).toBeInTheDocument()
    expect(screen.getByRole('alert')).toBeInTheDocument()
    // "Tentar novamente" nao serviria: o lazy repete a falha guardada.
    expect(screen.getByRole('button', { name: 'Recarregar a página' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Tentar novamente' })).not.toBeInTheDocument()
  })

  it('reconhece a falha de download pelas mensagens dos navegadores, e so por elas', () => {
    expect(ehFalhaDeDownload(new TypeError('Failed to fetch dynamically imported module: x'))).toBe(true)
    expect(ehFalhaDeDownload(new TypeError('Importing a module script failed.'))).toBe(true)
    expect(ehFalhaDeDownload(new Error('error loading dynamically imported module'))).toBe(true)
    expect(ehFalhaDeDownload(new Error('boom'))).toBe(false)
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
