import { render, screen, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import FuncionarioDetalhe from './FuncionarioDetalhe'
import { SessaoContexto } from '@/auth/contexto'
import type { UsuarioAutenticado } from '@/api/autenticacao'

function responder(corpo: unknown): Response {
  return new Response(JSON.stringify(corpo), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}

const FUNCIONARIO = {
  id: 'f1',
  nome: 'Carla Analista',
  cpf: '11144477735',
  cpfFormatado: '111.444.777-35',
  dataNascimento: '1990-05-20',
  ativo: true,
}

const CONTRATO = {
  id: 'c1',
  idFuncionario: 'f1',
  idEmpresa: 'e1',
  matricula: '000123',
  dataAdmissao: '2026-01-15',
  dataDesligamento: null,
  situacao: 'Ativo',
  vigenciaAtual: {
    id: 'v2',
    validoDe: '2026-06-01',
    validoAte: null,
    salario: 4200,
    idCargo: 'cg1',
    idEstabelecimento: 'es1',
    jornadaMensalHoras: 220,
    motivo: 'AlteracaoSalarial',
  },
}

const VIGENCIAS = [
  CONTRATO.vigenciaAtual,
  {
    id: 'v1',
    validoDe: '2026-01-15',
    validoAte: '2026-05-31',
    salario: 3000,
    idCargo: 'cg1',
    idEstabelecimento: 'es1',
    jornadaMensalHoras: 220,
    motivo: 'Admissao',
  },
]

const CARGOS = [{ id: 'cg1', codigo: 'AN', nome: 'Analista', ativo: true }]

const DEPENDENTES = [
  {
    id: 'd1',
    idFuncionario: 'f1',
    nome: 'Helena Souza Prado',
    dataNascimento: '2018-03-22',
    relacao: 'Filho',
    dedutivelIrrf: true,
    inicioDeducaoIrrf: '2026-01-01',
    fimDeducaoIrrf: null,
  },
  {
    id: 'd2',
    idFuncionario: 'f1',
    nome: 'Marta Souza Prado',
    dataNascimento: '1962-07-04',
    relacao: 'Mae',
    dedutivelIrrf: false,
    inicioDeducaoIrrf: null,
    fimDeducaoIrrf: null,
  },
]

/**
 * A ordem importa: `/dependentes` precisa vir ANTES de `/api/funcionarios/`,
 * senao a rota do dependente cai no ramo do funcionario e a secao recebe um
 * objeto no lugar da lista - o teste passaria sem exercitar nada.
 */
function rotear(url: string, dependentes: unknown = DEPENDENTES) {
  if (url.includes('/dependentes')) return responder(dependentes)
  if (url.includes('/vigencias')) return responder(VIGENCIAS)
  if (url.includes('/contratos')) return responder([CONTRATO])
  if (url.includes('/api/cargos')) return responder(CARGOS)
  if (url.includes('/api/funcionarios/')) return responder(FUNCIONARIO)
  return responder({})
}

function renderizar(perfil: UsuarioAutenticado['perfil']) {
  const usuario: UsuarioAutenticado = {
    id: 'u1',
    idOrganizacao: 'o1',
    nome: 'Quem olha',
    email: 'quem@x.com',
    perfil,
  }

  return render(
    <MemoryRouter initialEntries={['/funcionarios/f1']}>
      <SessaoContexto.Provider
        value={{
          usuario,
          carregando: false,
          entrar: async () => {},
          sair: async () => {},
        }}
      >
        <Routes>
          <Route path="/funcionarios/:id" element={<FuncionarioDetalhe />} />
        </Routes>
      </SessaoContexto.Provider>
    </MemoryRouter>,
  )
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('FuncionarioDetalhe', () => {
  it('mostra a linha do tempo com o salario ANTIGO preservado', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation((url: string) => Promise.resolve(rotear(String(url)))))

    renderizar('AdministradorEmpresa')

    expect(await screen.findByText('Carla Analista')).toBeInTheDocument()

    // O que o ROADMAP exige: alteracao nao reescreve o passado.
    //
    // O salario antigo aparece DUAS vezes de proposito: como o "de" da
    // mudanca na vigencia nova, e como o valor da vigencia antiga. E
    // justamente essa duplicidade que conta a historia.
    const antigos = await screen.findAllByText(/R\$\s*3\.000,00/)
    expect(antigos.length).toBeGreaterThanOrEqual(2)

    // O valor antigo aparece riscado na linha da mudanca.
    expect(antigos.some((no) => no.className.includes('line-through'))).toBe(true)

    // E ha uma seta explicando que aquilo virou outra coisa.
    expect(screen.getAllByLabelText('alterado para').length).toBeGreaterThanOrEqual(1)

    expect(screen.getAllByText(/R\$\s*4\.200,00/).length).toBeGreaterThanOrEqual(2)

    // E o periodo fechado continua legivel.
    expect(screen.getByText(/15\/01\/2026 até 31\/05\/2026/)).toBeInTheDocument()
    expect(screen.getByText(/01\/06\/2026 — vigência atual/)).toBeInTheDocument()
  })

  it('nao oferece o formulario de alteracao para quem nao pode administrar pessoas', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation((url: string) => Promise.resolve(rotear(String(url)))))

    renderizar('Visualizador')

    expect(await screen.findByText('Carla Analista')).toBeInTheDocument()

    // Esconder o botao e so conforto visual - quem barra e o backend. Mas
    // mostrar um formulario que sempre devolve 403 seria pior ainda.
    expect(screen.queryByRole('button', { name: /registrar alteração/i })).not.toBeInTheDocument()

    // A leitura do historico continua disponivel. findAllBy porque a linha do
    // tempo carrega num efeito proprio, depois do cabecalho.
    expect((await screen.findAllByText(/R\$\s*3\.000,00/)).length).toBeGreaterThan(0)
  })

  it('formata data civil sem deslocar o dia por fuso horario', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation((url: string) => Promise.resolve(rotear(String(url)))))

    renderizar('AdministradorEmpresa')

    // new Date('2026-01-15') seria interpretado como UTC e, no Brasil,
    // exibiria 14/01. A formatacao nao passa por Date de proposito.
    expect(await screen.findByText(/Admissão em 15\/01\/2026/)).toBeInTheDocument()
  })

  it('mostra os dependentes, separando quem abate IRRF de quem nao abate', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation((url: string) => Promise.resolve(rotear(String(url)))))

    renderizar('AdministradorEmpresa')

    expect(await screen.findByText('Helena Souza Prado')).toBeInTheDocument()
    expect(screen.getByText('Marta Souza Prado')).toBeInTheDocument()

    // A distincao que a tela existe para deixar obvia: cadastrar dependente
    // nao faz o imposto cair. So abate quem tem periodo declarado.
    const helena = screen.getByText('Helena Souza Prado').closest('tr')!
    const marta = screen.getByText('Marta Souza Prado').closest('tr')!

    expect(within(helena).getByText('Sim')).toBeInTheDocument()
    expect(within(helena).getByText(/01\/01\/2026.*em diante/)).toBeInTheDocument()
    expect(within(marta).getByText('Não')).toBeInTheDocument()
  })

  it('visualizador nao vê acao de cadastrar nem de remover dependente', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation((url: string) => Promise.resolve(rotear(String(url)))))

    renderizar('Visualizador')

    expect(await screen.findByText('Helena Souza Prado')).toBeInTheDocument()

    // Esconder botao nao e mecanismo de seguranca - o backend recusa de
    // qualquer jeito. A tela so evita propor uma acao que daria 403.
    expect(screen.queryByRole('button', { name: /novo dependente/i })).not.toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: /remover Helena Souza Prado/i }),
    ).not.toBeInTheDocument()
  })

  it('sem dependentes, explica o efeito no IRRF', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockImplementation((url: string) => Promise.resolve(rotear(String(url), []))),
    )

    renderizar('AdministradorEmpresa')

    expect(await screen.findByText('Nenhum dependente')).toBeInTheDocument()
  })
})
