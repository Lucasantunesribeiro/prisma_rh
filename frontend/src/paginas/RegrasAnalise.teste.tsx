import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { Regra } from '@/api/analises'
import type { Perfil } from '@/api/autenticacao'
import { SessaoContexto } from '@/auth/contexto'
import RegrasAnalise from './RegrasAnalise'

vi.mock('@/api/analises', async (original) => ({
  ...(await original<typeof import('@/api/analises')>()),
  listarRegras: vi.fn(),
  configurarRegra: vi.fn(),
}))

const api = await import('@/api/analises')

const SEM_PARAMETRO: Regra = {
  codigo: 'AusenteDaFolha',
  nome: 'Funcionario elegivel ausente da folha',
  explicacao: 'Procura contrato vigente na competencia sem holerite.',
  categoria: 'Ausencia',
  versao: 1,
  ativa: true,
  severidade: 'Alta',
  severidadePadrao: 'Alta',
  configurada: false,
  alteradoEm: null,
  parametros: [],
}

const COM_PARAMETRO: Regra = {
  codigo: 'DescontoAcimaDoLimite',
  nome: 'Desconto acima do limite',
  explicacao: 'Procura holerite em que os descontos passam do percentual configurado.',
  categoria: 'Valores',
  versao: 1,
  ativa: true,
  severidade: 'Media',
  severidadePadrao: 'Media',
  configurada: false,
  alteradoEm: null,
  parametros: [
    {
      chave: 'percentualMaximo',
      rotulo: 'Percentual maximo de desconto',
      explicacao: 'Acima disso, o holerite entra no relatorio.',
      tipo: 'Percentual',
      padrao: '70',
      minimo: '1',
      maximo: '100',
      valor: '70',
    },
  ],
}

function renderizar(perfil: Perfil = 'AdministradorEmpresa') {
  return render(
    <MemoryRouter>
      <SessaoContexto.Provider
        value={{
          usuario: { id: 'u1', idOrganizacao: 'o1', nome: 'Quem configura', email: 'teste@teste.exemplo', perfil },
          carregando: false,
          entrar: async () => {},
          sair: async () => {},
        }}
      >
        <RegrasAnalise />
      </SessaoContexto.Provider>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  vi.mocked(api.listarRegras).mockResolvedValue([SEM_PARAMETRO, COM_PARAMETRO])
})

afterEach(() => {
  vi.clearAllMocks()
})

describe('Regras de conferência', () => {
  it('lista o catálogo com categoria, versão e severidade', async () => {
    renderizar()

    expect(await screen.findByText('Desconto acima do limite')).toBeInTheDocument()
    expect(screen.getByText('Funcionario elegivel ausente da folha')).toBeInTheDocument()
    expect(screen.getByText('Ausência · v1')).toBeInTheDocument()
    expect(screen.getByText('Valores · v1')).toBeInTheDocument()

    // "Alta" aparece no selo da regra E como opção do select de severidade.
    // getByText exigiria um unico no; o que importa aqui e que o selo existe.
    expect(screen.getAllByText('Alta').length).toBeGreaterThan(0)
  })

  it('mostra "no padrão" enquanto a organização não mexeu', async () => {
    renderizar()

    // Regra sem configuração roda ativa, no padrão. Dizer isso evita que a
    // pessoa ache que precisa salvar para a regra passar a valer.
    expect(await screen.findAllByText('no padrão')).toHaveLength(2)
  })

  it('mostra erro e permite tentar novamente', async () => {
    vi.mocked(api.listarRegras).mockRejectedValueOnce(new Error('API fora do ar'))

    renderizar()

    expect(await screen.findByText('API fora do ar')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: /tentar novamente/i }))

    expect(await screen.findByText('Desconto acima do limite')).toBeInTheDocument()
  })

  // ------------------------------------------------------------ permissões

  it('o Analista de RH vê as regras mas NÃO as edita', async () => {
    renderizar('AnalistaRh')

    expect(await screen.findByText('Desconto acima do limite')).toBeInTheDocument()

    // Configurar é administração — afrouxar tolerância é o jeito mais barato
    // de fazer uma divergência sumir. Esconder é conforto; quem barra é o
    // backend, e há teste de integração provando o 403.
    expect(screen.queryByRole('button', { name: 'Salvar' })).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Severidade')).not.toBeInTheDocument()
    expect(
      screen.getByText(/Somente a administração da empresa altera regras/),
    ).toBeInTheDocument()
  })

  it('o Auditor também não edita', async () => {
    renderizar('Auditor')

    expect(await screen.findByText('Desconto acima do limite')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Salvar' })).not.toBeInTheDocument()
  })

  // ---------------------------------------------------------- configuração

  it('o botão de salvar só liga quando algo muda', async () => {
    renderizar()

    await screen.findByText('Desconto acima do limite')

    const salvar = screen.getAllByRole('button', { name: 'Salvar' })[0]

    expect(salvar).toBeDisabled()

    await userEvent.click(screen.getAllByRole('checkbox')[0])

    expect(salvar).toBeEnabled()
  })

  it('salvar envia ativa, severidade e parâmetros', async () => {
    vi.mocked(api.configurarRegra).mockResolvedValue({
      ...COM_PARAMETRO,
      configurada: true,
      severidade: 'Alta',
    })

    renderizar()

    await screen.findByText('Desconto acima do limite')

    const seletores = screen.getAllByLabelText('Severidade')

    await userEvent.selectOptions(seletores[1], 'Alta')
    await userEvent.click(screen.getAllByRole('button', { name: 'Salvar' })[1])

    await waitFor(() => {
      expect(api.configurarRegra).toHaveBeenCalledWith('DescontoAcimaDoLimite', {
        ativa: true,
        severidade: 'Alta',
        parametros: { percentualMaximo: '70' },
      })
    })
  })

  /**
   * A faixa do campo vem do SERVIDOR, e não do frontend.
   *
   * `min`/`max` são conforto de digitação. Quem decide é o backend — há teste
   * de integração provando que 150 num campo de 1 a 100 volta 400.
   */
  it('o campo numérico usa a faixa declarada pelo servidor', async () => {
    renderizar()

    const campo = await screen.findByLabelText('Percentual maximo de desconto')

    expect(campo).toHaveAttribute('min', '1')
    expect(campo).toHaveAttribute('max', '100')
    expect(screen.getByText('de 1 a 100')).toBeInTheDocument()
  })

  it('a recusa do servidor vira mensagem, e não tela quebrada', async () => {
    vi.mocked(api.configurarRegra).mockRejectedValue(
      new Error("'Percentual maximo de desconto' precisa estar entre 1 e 100."),
    )

    renderizar()

    await screen.findByText('Desconto acima do limite')

    await userEvent.click(screen.getAllByRole('checkbox')[1])
    await userEvent.click(screen.getAllByRole('button', { name: 'Salvar' })[1])

    expect(
      await screen.findByText("'Percentual maximo de desconto' precisa estar entre 1 e 100."),
    ).toBeInTheDocument()
  })
})
