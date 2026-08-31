import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ConsultaCnpj, Empresa } from '@/api/empresas'
import { SessaoContexto } from '@/auth/contexto'
import Empresas from './Empresas'

vi.mock('@/api/empresas', async (original) => ({
  ...(await original<typeof import('@/api/empresas')>()),
  listarEmpresas: vi.fn(),
  criarEmpresa: vi.fn(),
  consultarCnpj: vi.fn(),
}))

vi.mock('./Estabelecimentos', () => ({
  PainelEstabelecimentos: () => null,
}))

const api = await import('@/api/empresas')

/** Fictício de verdade: conferido na Receita, não existe. */
const CNPJ = '99999999000191'

const ENCONTRADA: ConsultaCnpj = {
  situacao: 'Encontrada',
  mensagem: 'Dados encontrados na Receita Federal.',
  dados: {
    razaoSocial: 'INDUSTRIA EXEMPLO S.A.',
    nomeFantasia: 'EXEMPLO',
    situacaoCadastral: 'ATIVA',
    ativaNaReceita: true,
  },
  jaCadastrada: false,
}

const EMPRESA: Empresa = {
  id: 'e1',
  razaoSocial: 'Empresa existente',
  nomeFantasia: null,
  cnpj: '11111111000191',
  cnpjFormatado: '11.111.111/0001-91',
  ativa: true,
  criadaEm: '2026-01-01T00:00:00Z',
}

function renderizar() {
  return render(
    <MemoryRouter>
      <SessaoContexto.Provider
        value={{
          usuario: { id: 'u1', idOrganizacao: 'o1', nome: 'Quem cadastra', perfil: 'AdministradorEmpresa' },
          carregando: false,
          entrar: async () => {},
          sair: async () => {},
        }}
      >
        <Empresas />
      </SessaoContexto.Provider>
    </MemoryRouter>,
  )
}

async function abrirFormulario() {
  renderizar()
  await userEvent.click(await screen.findByRole('button', { name: /nova empresa/i }))
}

beforeEach(() => {
  vi.mocked(api.listarEmpresas).mockResolvedValue({
    total: 1,
    pagina: 1,
    tamanho: 100,
    itens: [EMPRESA],
  })
  vi.mocked(api.consultarCnpj).mockResolvedValue(ENCONTRADA)
  vi.mocked(api.criarEmpresa).mockResolvedValue(EMPRESA)
})

afterEach(() => {
  vi.clearAllMocks()
})

describe('Empresas — consulta de CNPJ (Fase 8)', () => {
  it('só deixa buscar com os quatorze dígitos', async () => {
    await abrirFormulario()

    const buscar = screen.getByRole('button', { name: /buscar/i })
    expect(buscar).toBeDisabled()

    await userEvent.type(screen.getByLabelText('CNPJ'), '9999')
    expect(buscar).toBeDisabled()

    await userEvent.type(screen.getByLabelText('CNPJ'), '9999000191')
    expect(buscar).toBeEnabled()
  })

  it('envia só os dígitos, sem a máscara', async () => {
    await abrirFormulario()

    await userEvent.type(screen.getByLabelText('CNPJ'), '99.999.999/0001-91')
    await userEvent.click(screen.getByRole('button', { name: /buscar/i }))

    await waitFor(() => {
      expect(api.consultarCnpj).toHaveBeenCalledWith(CNPJ)
    })
  })

  /**
   * ⚠️ A regra que o responsável pediu por escrito: nada é preenchido sozinho.
   *
   * A resposta fica FORA do formulário até alguém clicar. Preencher
   * automaticamente pareceria mais prático e apagaria o que a pessoa digitou
   * sem ela ver.
   */
  it('não preenche o formulário sozinho', async () => {
    await abrirFormulario()

    await userEvent.type(screen.getByLabelText('CNPJ'), CNPJ)
    await userEvent.click(screen.getByRole('button', { name: /buscar/i }))

    // O dado aparece na área da consulta...
    expect(await screen.findByText('INDUSTRIA EXEMPLO S.A.')).toBeInTheDocument()

    // ...e o campo continua vazio.
    expect(screen.getByLabelText('Razão social')).toHaveValue('')
    expect(screen.getByLabelText('Nome fantasia')).toHaveValue('')
  })

  it('preenche quando a pessoa clica em usar os dados', async () => {
    await abrirFormulario()

    await userEvent.type(screen.getByLabelText('CNPJ'), CNPJ)
    await userEvent.click(screen.getByRole('button', { name: /buscar/i }))
    await userEvent.click(await screen.findByRole('button', { name: /usar estes dados/i }))

    expect(screen.getByLabelText('Razão social')).toHaveValue('INDUSTRIA EXEMPLO S.A.')
    expect(screen.getByLabelText('Nome fantasia')).toHaveValue('EXEMPLO')
  })

  /**
   * Sobrescrever é permitido; sobrescrever **em silêncio** não é. O que vai ser
   * substituído aparece antes, com o valor que a pessoa digitou.
   */
  it('avisa antes de substituir o que já estava escrito', async () => {
    await abrirFormulario()

    await userEvent.type(screen.getByLabelText('Razão social'), 'Nome que eu digitei')
    await userEvent.type(screen.getByLabelText('CNPJ'), CNPJ)
    await userEvent.click(screen.getByRole('button', { name: /buscar/i }))

    expect(await screen.findByText(/substitui o que você digitou/)).toBeInTheDocument()
    expect(screen.getByText(/Nome que eu digitei/)).toBeInTheDocument()
  })

  it('nome fantasia vazio na Receita não apaga o que a pessoa escreveu', async () => {
    vi.mocked(api.consultarCnpj).mockResolvedValue({
      ...ENCONTRADA,
      dados: { ...ENCONTRADA.dados!, nomeFantasia: null },
    })

    await abrirFormulario()

    await userEvent.type(screen.getByLabelText('Nome fantasia'), 'Apelido da empresa')
    await userEvent.type(screen.getByLabelText('CNPJ'), CNPJ)
    await userEvent.click(screen.getByRole('button', { name: /buscar/i }))
    await userEvent.click(await screen.findByRole('button', { name: /usar estes dados/i }))

    // "A Receita não sabe" é diferente de "não tem".
    expect(screen.getByLabelText('Nome fantasia')).toHaveValue('Apelido da empresa')
  })

  it('avisa quando o CNPJ não está ativo na Receita', async () => {
    vi.mocked(api.consultarCnpj).mockResolvedValue({
      ...ENCONTRADA,
      dados: { ...ENCONTRADA.dados!, situacaoCadastral: 'BAIXADA', ativaNaReceita: false },
    })

    await abrirFormulario()

    await userEvent.type(screen.getByLabelText('CNPJ'), CNPJ)
    await userEvent.click(screen.getByRole('button', { name: /buscar/i }))

    expect(await screen.findByText(/não está ativo na Receita/)).toBeInTheDocument()
  })

  it('avisa quando a empresa já existe nesta organização', async () => {
    vi.mocked(api.consultarCnpj).mockResolvedValue({ ...ENCONTRADA, jaCadastrada: true })

    await abrirFormulario()

    await userEvent.type(screen.getByLabelText('CNPJ'), CNPJ)
    await userEvent.click(screen.getByRole('button', { name: /buscar/i }))

    expect(await screen.findByText(/Já existe uma empresa com este CNPJ/)).toBeInTheDocument()
  })

  // ------------------------------------------------------------------- falhas

  /**
   * ⚠️ A propriedade que a fase inteira precisa preservar.
   *
   * `CLAUDE.md §1`: o Prisma RH não depende de outro sistema para funcionar.
   * Com a BrasilAPI fora do ar, o formulário manual precisa continuar
   * exatamente igual — e o cadastro precisa ir até o fim.
   */
  it.each([
    ['Indisponivel' as const, 'A consulta externa está indisponível no momento.'],
    ['NaoEncontrada' as const, 'CNPJ não encontrado na base da Receita Federal.'],
    ['Recusada' as const, 'A Receita Federal recusou este CNPJ.'],
  ])('com a consulta em %s o cadastro manual continua funcionando', async (situacao, mensagem) => {
    vi.mocked(api.consultarCnpj).mockResolvedValue({
      situacao,
      mensagem,
      dados: null,
      jaCadastrada: false,
    })

    await abrirFormulario()

    await userEvent.type(screen.getByLabelText('CNPJ'), CNPJ)
    await userEvent.click(screen.getByRole('button', { name: /buscar/i }))

    expect(await screen.findByText(mensagem)).toBeInTheDocument()

    // Nenhum dado para aproveitar, e nenhum botão prometendo o contrário.
    expect(screen.queryByRole('button', { name: /usar estes dados/i })).not.toBeInTheDocument()

    // E o cadastro à mão vai até o fim.
    await userEvent.type(screen.getByLabelText('Razão social'), 'Empresa digitada à mão')
    await userEvent.click(screen.getByRole('button', { name: /criar empresa/i }))

    await waitFor(() => {
      expect(api.criarEmpresa).toHaveBeenCalledWith({
        razaoSocial: 'Empresa digitada à mão',
        cnpj: CNPJ,
        nomeFantasia: null,
      })
    })
  })

  it('erro de rede na consulta não derruba a tela', async () => {
    vi.mocked(api.consultarCnpj).mockRejectedValue(new Error('Falha de conexão'))

    await abrirFormulario()

    await userEvent.type(screen.getByLabelText('CNPJ'), CNPJ)
    await userEvent.click(screen.getByRole('button', { name: /buscar/i }))

    expect(await screen.findByText('Falha de conexão')).toBeInTheDocument()
    expect(screen.getByLabelText('Razão social')).toBeEnabled()
  })

  it('mudar o CNPJ descarta o resultado anterior', async () => {
    await abrirFormulario()

    await userEvent.type(screen.getByLabelText('CNPJ'), CNPJ)
    await userEvent.click(screen.getByRole('button', { name: /buscar/i }))

    expect(await screen.findByText('INDUSTRIA EXEMPLO S.A.')).toBeInTheDocument()

    // Sem isso, a tela mostraria os dados de um CNPJ ao lado de outro número.
    await userEvent.type(screen.getByLabelText('CNPJ'), '1')

    expect(screen.queryByText('INDUSTRIA EXEMPLO S.A.')).not.toBeInTheDocument()
  })
})
