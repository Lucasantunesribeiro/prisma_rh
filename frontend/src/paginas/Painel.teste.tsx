import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { Empresa } from '@/api/empresas'
import type { UsuarioAutenticado } from '@/api/autenticacao'
import type { Inconsistencia, Painel as PainelDados } from '@/api/workflow'
import { SessaoContexto } from '@/auth/contexto'
import { EmpresaContexto } from '@/layout/contexto'
import Painel from './Painel'

vi.mock('@/api/workflow', async (original) => ({
  ...(await original<typeof import('@/api/workflow')>()),
  obterPainel: vi.fn(),
  listarInconsistencias: vi.fn(),
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

function renderizar(perfil: UsuarioAutenticado['perfil'] = 'AnalistaRh') {
  const usuario: UsuarioAutenticado = {
    id: 'u1',
    idOrganizacao: 'o1',
    nome: 'Quem olha',
    email: 'quem@x.com',
    perfil,
  }

  return render(
    <MemoryRouter>
      <SessaoContexto.Provider
        value={{ usuario, carregando: false, entrar: async () => {}, sair: async () => {} }}
      >
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
      </SessaoContexto.Provider>
    </MemoryRouter>,
  )
}

const ACHADO_LIQUIDO_NEGATIVO = {
  id: 'i1',
  idFolha: 'folha-do-roteiro',
  competencia: '08/2026',
  codigo: 'LiquidoNegativo',
  regra: 'Líquido negativo',
} as Inconsistencia

beforeEach(() => {
  vi.mocked(api.obterPainel).mockResolvedValue(COM_DADOS)
  vi.mocked(api.listarInconsistencias).mockResolvedValue({
    total: 1,
    pagina: 1,
    itens: [ACHADO_LIQUIDO_NEGATIVO],
  })
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

  describe('roteiro da demonstração', () => {
    it('para a conta pública, leva à folha onde o líquido negativo foi detectado', async () => {
      renderizar('Visualizador')

      const roteiro = await screen.findByRole('region', { name: 'Roteiro da demonstração' })
      expect(roteiro).toBeInTheDocument()

      // O link vem do achado da API, e não de um id fixo no código.
      expect(await screen.findByRole('link', { name: 'Abra a folha' })).toHaveAttribute(
        'href',
        '/folhas/folha-do-roteiro',
      )
      expect(screen.getByRole('link', { name: 'Inconsistências' })).toHaveAttribute('href', '/inconsistencias')
      expect(screen.getByRole('link', { name: 'Auditoria' })).toHaveAttribute('href', '/auditoria')

      // O visitante entende que o negativo é achado deliberado, e não defeito.
      expect(screen.getByText(/Sobre o líquido negativo/)).toBeInTheDocument()
      expect(screen.getByText(/de propósito/)).toBeInTheDocument()
    })

    it('sem o achado, o roteiro continua e aponta para a lista de folhas', async () => {
      vi.mocked(api.listarInconsistencias).mockRejectedValue(new Error('fora do ar'))
      renderizar('Visualizador')

      expect(await screen.findByRole('link', { name: 'Abra a folha' })).toHaveAttribute('href', '/folhas')
      // O painel continua de pé: os números não dependem do roteiro.
      expect(await screen.findByText('Folhas calculadas')).toBeInTheDocument()
    })

    it('quem opera a folha não vê tutorial no painel', async () => {
      renderizar('AnalistaRh')

      expect(await screen.findByText('Folhas calculadas')).toBeInTheDocument()
      expect(screen.queryByRole('region', { name: 'Roteiro da demonstração' })).not.toBeInTheDocument()
    })
  })
})
