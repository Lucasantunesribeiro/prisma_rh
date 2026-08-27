import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
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
      basesIncidentes: 'Inss, Fgts, Irrf',
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
      basesIncidentes: 'Nenhuma',
      memoria: [{ ordem: 1, descricao: 'Valor informado no lançamento manual', expressao: 'Vale-transporte', valor: 180 }],
    },
  ],
  bases: [
    { base: 'Inss', valor: 3320, composta: ['SAL'] },
    { base: 'Fgts', valor: 3320, composta: ['SAL'] },
    { base: 'Irrf', valor: 3320, composta: ['SAL'] },
  ],
}

/** Mesmo holerite, com a linha informativa do FGTS (Fase 4C). */
const HOLERITE_COM_FGTS = {
  ...HOLERITE,
  lancamentos: [
    ...HOLERITE.lancamentos,
    {
      id: 'l3',
      codigoRubrica: 'FGTS',
      nomeRubrica: 'FGTS sobre a folha',
      tipo: 'Informativo',
      origem: 'Calculado',
      referencia: null,
      valor: 265.6,
      ordem: 3,
      basesIncidentes: 'Nenhuma',
      memoria: [
        { ordem: 1, descricao: 'Base de calculo do FGTS', expressao: '3.320,00', valor: 3320 },
        { ordem: 2, descricao: 'Deposito do empregador, aliquota 8%', expressao: '3.320,00 x 8%', valor: 265.6 },
      ],
    },
  ],
}

const RUBRICAS = [
  { id: 'r1', codigo: 'SAL', nome: 'Salário base', tipo: 'Provento', estrategia: 'SalarioBaseProporcional', basesIncidentes: 'Inss, Fgts, Irrf', ativa: true },
  { id: 'r2', codigo: 'VT', nome: 'Vale-transporte', tipo: 'Desconto', estrategia: 'ValorInformado', basesIncidentes: 'Nenhuma', ativa: true },
]

function renderizar(perfil: UsuarioAutenticado['perfil'], folha = FOLHA, holerite = HOLERITE) {
  vi.stubGlobal(
    'fetch',
    vi.fn().mockImplementation((url: string) => {
      const texto = String(url)
      if (texto.includes('/funcionarios/')) return Promise.resolve(responder(holerite))
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
  it('mostra os totais na faixa e o funcionário com os avos do mês', async () => {
    renderizar('AdministradorEmpresa')

    expect(await screen.findByText(/Folha mensal · agosto de 2026/)).toBeInTheDocument()
    expect(screen.getByText('Indústria Modelo S.A.')).toBeInTheDocument()

    // O líquido aparece duas vezes de propósito: na faixa de resumo e na
    // linha do funcionário. Com um funcionário só, os dois batem.
    expect(await screen.findAllByText(/R\$\s*3\.590,00/)).toHaveLength(2)
    expect(screen.getByText('30/30')).toBeInTheDocument()
  })

  it('abre a memória de cálculo em drawer, a partir da rubrica calculada', async () => {
    const usuario = userEvent.setup()
    renderizar('AdministradorEmpresa')

    // Clicar na linha abre o holerite.
    const linha = (await screen.findAllByText('Bruno Carvalho Lima'))[0].closest('tr')!
    await usuario.click(linha)

    // A memória NÃO fica exposta junto do holerite: ela é de apoio.
    expect(screen.queryByText('3.000,00 x 14/30')).not.toBeInTheDocument()

    // Só a rubrica calculada abre memória; a manual não é um botão.
    await usuario.click(await screen.findByRole('button', { name: 'Salário base' }))

    expect(await screen.findByText('3.000,00 x 14/30')).toBeInTheDocument()
    expect(screen.getByText('3.600,00 x 16/30')).toBeInTheDocument()
    expect(screen.getByText(/Soma das vigências do mês/)).toBeInTheDocument()
  })

  it('não oferece calcular nem fechar para quem só visualiza', async () => {
    renderizar('Visualizador')

    expect(await screen.findByText('Indústria Modelo S.A.')).toBeInTheDocument()

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

    expect(await screen.findByText('Fechada')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /recalcular/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /fechar folha/i })).not.toBeInTheDocument()
  })

  it('mostra as bases de cálculo e quais rubricas as formaram', async () => {
    renderizar('AdministradorEmpresa')

    const usuario = userEvent.setup()
    const linha = (await screen.findAllByText('Bruno Carvalho Lima'))[0].closest('tr')!
    await usuario.click(linha)

    const titulo = await screen.findByText('Bases de cálculo')
    const secao = titulo.closest('section')!

    // As três bases, e a memória derivada de cada uma. Escopado à seção
    // porque "SAL" também aparece na linha do lançamento acima.
    expect(within(secao).getByText('INSS')).toBeInTheDocument()
    expect(within(secao).getByText('FGTS')).toBeInTheDocument()
    expect(within(secao).getByText('IRRF')).toBeInTheDocument()
    expect(within(secao).getAllByText('SAL')).toHaveLength(3)

    // O vale-transporte é desconto: não compõe base nenhuma.
    expect(within(secao).queryByText(/VT/)).not.toBeInTheDocument()
    expect(within(secao).getAllByText(/R\$\s*3\.320,00/)).toHaveLength(3)
  })

  it('sem informativo, o holerite não mostra a coluna', async () => {
    renderizar('AdministradorEmpresa')

    const usuario = userEvent.setup()
    const linha = (await screen.findAllByText('Bruno Carvalho Lima'))[0].closest('tr')!
    await usuario.click(linha)

    await screen.findByText('Bases de cálculo')

    // A coluna só aparece quando há o que pôr nela: o holerite comum
    // continua com duas colunas de dinheiro.
    expect(screen.queryByRole('columnheader', { name: 'Informativo' })).not.toBeInTheDocument()
  })

  it('mostra o FGTS em coluna própria, fora do líquido', async () => {
    renderizar('AdministradorEmpresa', FOLHA, HOLERITE_COM_FGTS)

    const usuario = userEvent.setup()
    const linha = (await screen.findAllByText('Bruno Carvalho Lima'))[0].closest('tr')!
    await usuario.click(linha)

    expect(await screen.findByRole('columnheader', { name: 'Informativo' })).toBeInTheDocument()

    const linhaFgts = (await screen.findByText('FGTS sobre a folha')).closest('tr')!
    const celulas = within(linhaFgts).getAllByRole('cell')

    // Proventos e Descontos vazios, valor na coluna informativa: é o que
    // prova que o depósito do empregador não foi somado ao holerite.
    expect(celulas[3]).toHaveTextContent('')
    expect(celulas[4]).toHaveTextContent('')
    expect(celulas[5]).toHaveTextContent(/R\$\s*265,60/)

    // O líquido continua sendo proventos menos descontos.
    expect(screen.getAllByText(/R\$\s*3\.590,00/).length).toBeGreaterThan(0)
    expect(screen.getByText(/não entram\s+no líquido/)).toBeInTheDocument()
  })
})
