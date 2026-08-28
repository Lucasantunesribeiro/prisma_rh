import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import Folhas from './Folhas'
import type { UsuarioAutenticado } from '@/api/autenticacao'
import { SessaoContexto } from '@/auth/contexto'
import { EmpresaContexto } from '@/layout/contexto'

function responder(corpo: unknown): Response {
  return new Response(JSON.stringify(corpo), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}

const EMPRESAS = {
  total: 1,
  pagina: 1,
  tamanho: 25,
  itens: [
    {
      id: 'e1',
      razaoSocial: 'Indústria Modelo S.A.',
      nomeFantasia: 'Modelo',
      cnpj: '11222333000181',
      cnpjFormatado: '11.222.333/0001-81',
      ativa: true,
      criadaEm: '2026-01-01T12:00:00Z',
    },
  ],
}

/** A mesma empresa e a mesma competência, com os dois tipos: é o que a Fase 4E permitiu. */
const FOLHAS = [
  {
    id: 'p1',
    idEmpresa: 'e1',
    empresa: 'Indústria Modelo S.A.',
    competencia: '08/2026',
    tipo: 'Mensal',
    situacao: 'Calculada',
    versaoCalculo: 1,
    quantidadeFuncionarios: 3,
    totalProventos: 9000,
    totalDescontos: 900,
    totalLiquido: 8100,
    calculadaEm: '2026-08-20T12:00:00Z',
    fechadaEm: null,
  },
  {
    id: 'p2',
    idEmpresa: 'e1',
    empresa: 'Indústria Modelo S.A.',
    competencia: '08/2026',
    tipo: 'Ferias',
    situacao: 'Calculada',
    versaoCalculo: 1,
    quantidadeFuncionarios: 1,
    totalProventos: 4000,
    totalDescontos: 300,
    totalLiquido: 3700,
    calculadaEm: '2026-08-21T12:00:00Z',
    fechadaEm: null,
  },
]

function renderizar(perfil: UsuarioAutenticado['perfil']) {
  vi.stubGlobal(
    'fetch',
    vi.fn().mockImplementation((url: string) => {
      const texto = String(url)
      if (texto.includes('/api/empresas')) return Promise.resolve(responder(EMPRESAS))
      if (texto.includes('/api/folhas')) return Promise.resolve(responder(FOLHAS))
      return Promise.resolve(responder({}))
    }),
  )

  const usuario: UsuarioAutenticado = {
    id: 'u1',
    idOrganizacao: 'o1',
    nome: 'Quem olha',
    email: 'quem@x.com',
    perfil,
  }

  // A pagina vive dentro do ApplicationShell, que publica a empresa atual.
  // PaginaContexto NAO e provido de proposito: usePagina trata a ausencia, e
  // exigi-lo aqui so acoplaria o teste ao shell sem provar nada a mais.
  return render(
    <MemoryRouter>
      <SessaoContexto.Provider
        value={{ usuario, carregando: false, entrar: async () => {}, sair: async () => {} }}
      >
        <EmpresaContexto.Provider
          value={{
            empresas: EMPRESAS.itens,
            empresaAtual: EMPRESAS.itens[0],
            selecionar: () => {},
            carregando: false,
          }}
        >
          <Folhas />
        </EmpresaContexto.Provider>
      </SessaoContexto.Provider>
    </MemoryRouter>,
  )
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('Folhas', () => {
  it('mostra a mensal e a de férias da MESMA competência', async () => {
    renderizar('AdministradorEmpresa')

    // As duas linhas dizem 08/2026. Antes da Fase 4E isso era impossível: o
    // índice único não tinha o tipo, e a segunda folha era recusada.
    expect(await screen.findAllByText('08/2026')).toHaveLength(2)

    const mensal = screen.getByText('Mensal').closest('tr')!
    const ferias = screen.getByText('Férias').closest('tr')!

    expect(within(mensal).getByText('08/2026')).toBeInTheDocument()
    expect(within(ferias).getByText('08/2026')).toBeInTheDocument()

    // E os totais são de cada uma, não somados.
    expect(within(ferias).getByText(/R\$\s*3\.700,00/)).toBeInTheDocument()
  })

  it('quem processa folha escolhe o tipo ao abrir', async () => {
    renderizar('AnalistaRh')

    expect(await screen.findAllByText('08/2026')).toHaveLength(2)
    expect(screen.getByRole('button', { name: /abrir folha/i })).toBeInTheDocument()
  })

  it('visualizador não abre folha', async () => {
    renderizar('Visualizador')

    expect(await screen.findAllByText('08/2026')).toHaveLength(2)

    // Esconder botão não é segurança - a política ProcessarFolha recusa de
    // qualquer jeito. A tela só evita propor uma ação que daria 403.
    expect(screen.queryByRole('button', { name: /abrir folha/i })).not.toBeInTheDocument()
  })
})
