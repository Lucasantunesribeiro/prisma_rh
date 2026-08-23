import { render, screen, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import FolhaDetalhe from './FolhaDetalhe'
import { SessaoContexto } from '@/auth/contexto'
import type { UsuarioAutenticado } from '@/api/autenticacao'

function responder(corpo: unknown): Response {
  return new Response(JSON.stringify(corpo), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}

const RESUMO = {
  id: 'h1',
  idFuncionario: 'f1',
  funcionario: 'Bruno Carvalho Lima',
  matricula: '1001',
  avos: 30,
  divisor: 30,
  salarioReferencia: 3600,
  totalProventos: 3770,
  totalDescontos: 180,
  liquido: 3590,
}

const FOLHA = {
  folha: {
    id: 'p1',
    idEmpresa: 'e1',
    empresa: 'Indústria Modelo S.A.',
    competencia: '08/2026',
    situacao: 'Calculada',
    versaoCalculo: 2,
    quantidadeFuncionarios: 1,
    totalProventos: 3770,
    totalDescontos: 180,
    totalLiquido: 3590,
    calculadaEm: '2026-08-23T12:00:00Z' as string | null,
    fechadaEm: null as string | null,
  },
  funcionarios: [RESUMO],
}

const HOLERITE = {
  resumo: RESUMO,
  competencia: '08/2026',
  situacaoFolha: 'Calculada',
  lancamentos: [
    {
      id: 'l1',
      codigoRubrica: 'SAL',
      nomeRubrica: 'Salário base',
      tipo: 'Provento',
      origem: 'Calculado',
      referencia: '30/30',
      valor: 3320,
      ordem: 1,
      memoria: [
        { ordem: 1, descricao: 'Vigência de 01/08 a 14/08', expressao: '3.000,00 x 14/30', valor: 1400 },
        { ordem: 2, descricao: 'Vigência de 15/08 a 31/08', expressao: '3.600,00 x 16/30', valor: 1920 },
        { ordem: 3, descricao: 'Soma das vigências do mês', expressao: '1.400,00 + 1.920,00', valor: 3320 },
      ],
    },
    {
      id: 'l2',
      codigoRubrica: 'VT',
      nomeRubrica: 'Vale-transporte',
      tipo: 'Desconto',
      origem: 'Manual',
      referencia: null,
      valor: 180,
      ordem: 2,
      memoria: [{ ordem: 1, descricao: 'Valor informado no lançamento manual', expressao: 'Vale-transporte', valor: 180 }],
    },
  ],
}

const RUBRICAS = [
  { id: 'r1', codigo: 'SAL', nome: 'Salário base', tipo: 'Provento', estrategia: 'SalarioBaseProporcional', ativa: true },
  { id: 'r2', codigo: 'VT', nome: 'Vale-transporte', tipo: 'Desconto', estrategia: 'ValorInformado', ativa: true },
]

function renderizar(perfil: UsuarioAutenticado['perfil'], folha = FOLHA) {
  vi.stubGlobal(
    'fetch',
    vi.fn().mockImplementation((url: string) => {
      const texto = String(url)
      if (texto.includes('/funcionarios/')) return Promise.resolve(responder(HOLERITE))
      if (texto.includes('/api/rubricas')) return Promise.resolve(responder(RUBRICAS))
      if (texto.includes('/api/folhas/')) return Promise.resolve(responder(folha))
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

  return render(
    <MemoryRouter initialEntries={['/folhas/p1']}>
      <SessaoContexto.Provider
        value={{ usuario, carregando: false, entrar: async () => {}, sair: async () => {} }}
      >
        <Routes>
          <Route path="/folhas/:id" element={<FolhaDetalhe />} />
        </Routes>
      </SessaoContexto.Provider>
    </MemoryRouter>,
  )
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('FolhaDetalhe', () => {
  it('mostra os totais e o funcionário com os avos do mês', async () => {
    renderizar('AdministradorEmpresa')

    expect(await screen.findByText(/Indústria Modelo S.A. · 08\/2026/)).toBeInTheDocument()
    expect(screen.getByText(/agosto de 2026/)).toBeInTheDocument()

    // O líquido aparece duas vezes de propósito: no totalizador da folha e
    // na linha do funcionário. Com um funcionário só, os dois batem.
    expect(await screen.findAllByText(/R\$\s*3\.590,00/)).toHaveLength(2)
    expect(screen.getByText('30/30')).toBeInTheDocument()
  })

  it('abre a memória de cálculo e mostra os dois trechos da vigência', async () => {
    renderizar('AdministradorEmpresa')

    const linha = (await screen.findAllByText('Bruno Carvalho Lima'))[0].closest('tr')!
    within(linha).getByRole('button', { name: 'Ver' }).click()

    // O que o ROADMAP chama de "exibir memória de cálculo": a conta, não só
    // o resultado.
    expect(await screen.findByText('3.000,00 x 14/30')).toBeInTheDocument()
    expect(screen.getByText('3.600,00 x 16/30')).toBeInTheDocument()
    expect(screen.getByText(/Soma das vigências do mês/)).toBeInTheDocument()
  })

  it('não oferece calcular nem fechar para quem só visualiza', async () => {
    renderizar('Visualizador')

    expect(await screen.findByText(/Indústria Modelo S.A./)).toBeInTheDocument()

    // Esconder o botão é conforto visual - quem barra é a política
    // ProcessarFolha no backend. Mas mostrar um botão que sempre devolve 403
    // seria pior.
    expect(screen.queryByRole('button', { name: /recalcular/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /fechar folha/i })).not.toBeInTheDocument()

    // A leitura continua disponível.
    expect(screen.getByText('30/30')).toBeInTheDocument()
  })

  it('folha fechada não oferece recalcular', async () => {
    const fechada = {
      ...FOLHA,
      folha: { ...FOLHA.folha, situacao: 'Fechada', fechadaEm: '2026-09-01T10:00:00Z' },
    }

    renderizar('AdministradorEmpresa', fechada)

    expect(await screen.findByText('fechada')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /recalcular/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /fechar folha/i })).not.toBeInTheDocument()
  })
})
