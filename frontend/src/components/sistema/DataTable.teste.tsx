import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import { DataTable, type Coluna } from './DataTable'

type Folha = { id: string; competencia: string; situacao: string }

const ITENS: Folha[] = [
  { id: 'f1', competencia: '08/2026', situacao: 'Fechada' },
  { id: 'f2', competencia: '09/2026', situacao: 'Calculada' },
]

const COLUNAS: Coluna<Folha>[] = [
  { cabecalho: 'Situação', celula: (f) => <span>{f.situacao}</span> },
  { cabecalho: 'Competência', principal: true, celula: (f) => <span>{f.competencia}</span> },
]

function comRotas(tabela: React.ReactNode) {
  return render(
    <MemoryRouter initialEntries={['/folhas']}>
      <Routes>
        <Route path="/folhas" element={tabela} />
        <Route path="/folhas/:id" element={<p>página da folha</p>} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('DataTable — ação da linha', () => {
  it('navegação vira LINK de verdade, na coluna principal, com nome que começa pela ação', () => {
    comRotas(
      <DataTable
        rotulo="Folhas"
        colunas={COLUNAS}
        itens={ITENS}
        chave={(f) => f.id}
        acaoLinha={{ rotulo: 'Abrir folha', para: (f) => `/folhas/${f.id}` }}
      />,
    )

    const link = screen.getByRole('link', { name: 'Abrir folha 08/2026' })
    expect(link).toHaveAttribute('href', '/folhas/f1')

    // A coluna marcada como principal, e não a primeira (um selo de situação).
    expect(link.closest('td')).toHaveTextContent('08/2026')
    expect(screen.queryByRole('link', { name: /Fechada/ })).not.toBeInTheDocument()
  })

  it('Tab chega ao link e Enter navega', async () => {
    const usuario = userEvent.setup()
    comRotas(
      <DataTable
        rotulo="Folhas"
        colunas={COLUNAS}
        itens={ITENS}
        chave={(f) => f.id}
        acaoLinha={{ rotulo: 'Abrir folha', para: (f) => `/folhas/${f.id}` }}
      />,
    )

    await usuario.tab()
    expect(screen.getByRole('link', { name: 'Abrir folha 08/2026' })).toHaveFocus()

    await usuario.keyboard('{Enter}')
    expect(await screen.findByText('página da folha')).toBeInTheDocument()
  })

  it('o clique em qualquer ponto da linha aciona o mesmo elemento, uma vez só', async () => {
    const usuario = userEvent.setup()
    const aoAbrir = vi.fn()

    render(
      <DataTable
        rotulo="Folhas"
        colunas={COLUNAS}
        itens={ITENS}
        chave={(f) => f.id}
        acaoLinha={{ rotulo: 'Abrir folha', aoAbrir }}
      />,
    )

    // Clique fora do botão (no selo da primeira coluna): atalho de mouse.
    await usuario.click(screen.getByText('Calculada'))
    expect(aoAbrir).toHaveBeenCalledTimes(1)
    expect(aoAbrir).toHaveBeenLastCalledWith(ITENS[1])

    // Clique NO botão: ele trata, e a linha não repete.
    await usuario.click(screen.getByRole('button', { name: 'Abrir folha 08/2026' }))
    expect(aoAbrir).toHaveBeenCalledTimes(2)
    expect(aoAbrir).toHaveBeenLastCalledWith(ITENS[0])
  })

  it('sem ação, a linha não ganha botão nem link', () => {
    render(<DataTable rotulo="Folhas" colunas={COLUNAS} itens={ITENS} chave={(f) => f.id} />)

    expect(screen.queryByRole('button')).not.toBeInTheDocument()
    expect(screen.queryByRole('link')).not.toBeInTheDocument()
  })
})
