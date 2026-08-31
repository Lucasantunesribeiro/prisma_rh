import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { Empresa } from '@/api/empresas'
import type { Painel as PainelDados } from '@/api/workflow'
import { EmpresaContexto } from '@/layout/contexto'
import Painel from './Painel'

vi.mock('@/api/workflow', async (original) => ({
  ...(await original<typeof import('@/api/workflow')>()),
  obterPainel: vi.fn(),
}))

const api = await import('@/api/workflow')

const EMPRESA: Empresa = {
  id: 'e1',
  razaoSocial: 'Empresa de teste',
  nomeFantasia: 'Teste',
  cnpj: '11222333000181',
  cnpjFormatado: '11.222.333/0001-81',
  ativa: true,
  criadaEm: '2026-01-01T00:00:00Z',
}

const COM_DADOS: PainelDados = {
  folhasCalculadas: 8,
  folhasFechadas: 3,
  inconsistenciasTotais: 10,
  inconsistenciasPendentes: 4,
  inconsistenciasResolvidas: 6,
  percentualConformidade: 60,
  porSeveridade: [
    { rotulo: 'Alta', quantidade: 7 },
    { rotulo: 'Media', quantidade: 3 },
  ],
  porStatus: [
    { rotulo: 'Detectada', quantidade: 4 },
    { rotulo: 'Resolvida', quantidade: 6 },
  ],
  porRegra: [{ rotulo: 'Desligado presente na folha mensal', quantidade: 7 }],
  porResponsavel: [
    { idResponsavel: 'u1', responsavel: 'Ana', quantidade: 3 },
    { idResponsavel: null, responsavel: 'Sem responsável', quantidade: 1 },
  ],
  evolucao: [
    { competencia: '07/2026', folhas: 4, inconsistencias: 6, resolvidas: 4 },
    { competencia: '08/2026', folhas: 4, inconsistencias: 4, resolvidas: 2 },
  ],
}

const VAZIO: PainelDados = {
  folhasCalculadas: 0,
  folhasFechadas: 0,
  inconsistenciasTotais: 0,
  inconsistenciasPendentes: 0,
  inconsistenciasResolvidas: 0,
  percentualConformidade: null,
  porSeveridade: [],
  porStatus: [],
  porRegra: [],
  porResponsavel: [],
  evolucao: [],
}

function renderizar() {
  return render(
    <MemoryRouter>
      <EmpresaContexto.Provider
        value={{
          empresas: [EMPRESA],
          empresaAtual: EMPRESA,
          selecionar: () => {},
          carregando: false,
        }}
      >
        <Painel />
      </EmpresaContexto.Provider>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  vi.mocked(api.obterPainel).mockResolvedValue(COM_DADOS)
})

afterEach(() => {
  vi.clearAllMocks()
})

describe('Painel', () => {
  it('mostra os indicadores da empresa selecionada', async () => {
    renderizar()

    expect(await screen.findByText('Folhas calculadas')).toBeInTheDocument()
    expect(screen.getByText('8')).toBeInTheDocument()
    expect(screen.getByText('60%')).toBeInTheDocument()

    // A empresa do shell vira o filtro: o painel fala da empresa que a pessoa
    // está olhando, e não da organização inteira.
    expect(api.obterPainel).toHaveBeenCalledWith('e1')
  })

  it('mostra as regras com maior incidência pelo NOME', async () => {
    renderizar()

    // O painel é lido por quem não conhece o enum do sistema.
    expect(await screen.findByText('Desligado presente na folha mensal')).toBeInTheDocument()
  })

  it('mostra pendências por responsável, inclusive as sem dono', async () => {
    renderizar()

    expect(await screen.findByText('Ana')).toBeInTheDocument()
    expect(screen.getByText('Sem responsável')).toBeInTheDocument()
  })

  it('mostra a evolução por competência', async () => {
    renderizar()

    expect(await screen.findByText('07/2026')).toBeInTheDocument()
    expect(screen.getByText('08/2026')).toBeInTheDocument()
  })

  /**
   * Sem inconsistência não há conformidade a medir.
   *
   * "100%" numa organização que nunca rodou análise seria uma afirmação que o
   * sistema não tem como sustentar.
   */
  it('sem folha calculada, explica em vez de mostrar zeros', async () => {
    vi.mocked(api.obterPainel).mockResolvedValue(VAZIO)

    renderizar()

    expect(await screen.findByText(/Ainda não há folha calculada/)).toBeInTheDocument()
    expect(screen.queryByText('Conformidade')).not.toBeInTheDocument()
  })

  it('mostra erro e permite tentar novamente', async () => {
    vi.mocked(api.obterPainel).mockRejectedValueOnce(new Error('API fora do ar'))

    renderizar()

    expect(await screen.findByText('API fora do ar')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: /tentar novamente/i }))

    expect(await screen.findByText('Folhas calculadas')).toBeInTheDocument()
  })
})
