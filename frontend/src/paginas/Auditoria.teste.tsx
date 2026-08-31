import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { EventoAuditoria } from '@/api/workflow'
import Auditoria from './Auditoria'

vi.mock('@/api/workflow', async (original) => ({
  ...(await original<typeof import('@/api/workflow')>()),
  listarAuditoria: vi.fn(),
}))

const api = await import('@/api/workflow')

const EVENTOS: EventoAuditoria[] = [
  {
    id: 'e1',
    acao: 'ValorBaseFgtsInformado',
    entidade: 'ValorBaseFgtsRescisorio',
    idEntidade: 'v1',
    usuario: 'Admin I',
    descricao: 'Valor base do FGTS rescisorio corrigido de 10.000,00 para 12.500,00.',
    contexto: 'contrato=c1;anterior=10000.00;novo=12500.00',
    ocorridoEm: '2026-08-30T13:00:00Z',
  },
  {
    id: 'e2',
    acao: 'RegraAnaliseConfigurada',
    entidade: 'RegraAnalise',
    idEntidade: 'r1',
    usuario: 'Admin I',
    descricao: "Regra 'Variacao salarial fora da tolerancia' configurada: ativa, severidade Baixa.",
    contexto: 'codigo=VariacaoSalarial;ativa=True;severidade=Baixa;percentualTolerancia=95',
    ocorridoEm: '2026-08-30T12:00:00Z',
  },
]

function renderizar() {
  return render(
    <MemoryRouter>
      <Auditoria />
    </MemoryRouter>,
  )
}

beforeEach(() => {
  vi.mocked(api.listarAuditoria).mockResolvedValue({ total: 2, pagina: 1, itens: EVENTOS })
})

afterEach(() => {
  vi.clearAllMocks()
})

describe('Auditoria', () => {
  it('lista os eventos com quem, o quê e o contexto', async () => {
    renderizar()

    expect(
      await screen.findByText(
        'Valor base do FGTS rescisorio corrigido de 10.000,00 para 12.500,00.',
      ),
    ).toBeInTheDocument()

    expect(screen.getAllByText('Admin I')).toHaveLength(2)

    // "Valor base do FGTS" aparece na LINHA e tambem como opcao do filtro -
    // getByText exigiria um unico no. O que importa e que a linha existe.
    expect(screen.getAllByText('Valor base do FGTS').length).toBeGreaterThan(1)
    expect(screen.getAllByText('Regra de conferência').length).toBeGreaterThan(1)
    expect(
      screen.getByText('contrato=c1;anterior=10000.00;novo=12500.00'),
    ).toBeInTheDocument()
  })

  /**
   * ⚠️ A tela **não tem** botão de criar, editar ou apagar.
   *
   * Não é esquecimento: não existe rota para nenhum dos três, para perfil
   * nenhum, inclusive Administrador da Plataforma (`CLAUDE.md §24.17`). Uma
   * trilha que alguém pode editar não é trilha.
   */
  it('NAO oferece nenhuma ação de escrita', async () => {
    renderizar()

    await screen.findAllByText('Valor base do FGTS')

    // A tela nao tem botao NENHUM: so a tabela e o filtro. `queryAllByRole`
    // porque `getAllByRole` estoura quando nao ha nada - e "nao ha nada" e
    // exatamente o que este teste afirma.
    const botoes = screen.queryAllByRole('button').map((b) => b.textContent?.toLowerCase() ?? '')

    expect(botoes.some((t) => t.includes('novo') || t.includes('criar'))).toBe(false)
    expect(botoes.some((t) => t.includes('editar') || t.includes('alterar'))).toBe(false)
    expect(botoes.some((t) => t.includes('excluir') || t.includes('apagar'))).toBe(false)

    // O texto da tela diz isso a quem lê, e não só ao revisor de código.
    expect(screen.getByText(/ninguém edita, de nenhum perfil/)).toBeInTheDocument()
  })

  it('filtrar por entidade refaz a consulta', async () => {
    renderizar()

    await screen.findByText('Valor base do FGTS')

    await userEvent.selectOptions(screen.getByLabelText('Filtrar por entidade'), 'RegraAnalise')

    await waitFor(() => {
      expect(api.listarAuditoria).toHaveBeenLastCalledWith({ entidade: 'RegraAnalise' })
    })
  })

  it('mostra o estado vazio quando nada foi registrado', async () => {
    vi.mocked(api.listarAuditoria).mockResolvedValue({ total: 0, pagina: 1, itens: [] })

    renderizar()

    expect(await screen.findByText('Nada registrado ainda')).toBeInTheDocument()
  })

  it('mostra erro e permite tentar novamente', async () => {
    vi.mocked(api.listarAuditoria).mockRejectedValueOnce(new Error('API fora do ar'))

    renderizar()

    expect(await screen.findByText('API fora do ar')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: /tentar novamente/i }))

    expect((await screen.findAllByText('Valor base do FGTS')).length).toBeGreaterThan(0)
  })
})
