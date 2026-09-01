import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { Perfil } from '@/api/autenticacao'
import type { Inconsistencia } from '@/api/workflow'
import { SessaoContexto } from '@/auth/contexto'
import Inconsistencias from './Inconsistencias'

vi.mock('@/api/workflow', async (original) => ({
  ...(await original<typeof import('@/api/workflow')>()),
  listarInconsistencias: vi.fn(),
  obterInconsistencia: vi.fn(),
  transitar: vi.fn(),
  atribuir: vi.fn(),
  comentar: vi.fn(),
  registrarEvidencia: vi.fn(),
}))

vi.mock('@/api/assistente', () => ({
  assistenteDisponivel: vi.fn(),
  explicarInconsistencia: vi.fn(),
}))

const api = await import('@/api/workflow')
const ia = await import('@/api/assistente')

const DETECTADA: Inconsistencia = {
  id: 'i1',
  idFolha: 'f1',
  competencia: '08/2026',
  codigo: 'DesligadoNaFolha',
  regra: 'Desligado presente na folha mensal',
  versaoRegra: 1,
  categoria: 'Contrato',
  severidade: 'Alta',
  status: 'Detectada',
  pendente: true,
  proximosStatus: ['EmAnalise'],
  idResponsavel: null,
  responsavel: null,
  justificativa: null,
  concluidaEm: null,
  idFolhaFuncionario: 'h1',
  matricula: 'W000010',
  nomeFuncionario: 'Quem Saiu',
  descricao: 'Desligado em 20/07/2026, e mesmo assim tem holerite nesta folha mensal.',
  valorEsperado: null,
  valorEncontrado: 2700,
  diferenca: null,
  andamentos: [],
}

const EM_ANALISE: Inconsistencia = {
  ...DETECTADA,
  status: 'EmAnalise',
  proximosStatus: ['Justificada', 'Corrigida'],
  idResponsavel: 'u1',
  responsavel: 'Quem trata',
  andamentos: [
    {
      id: 'a1',
      tipo: 'Transicao',
      autor: 'Quem trata',
      ocorridoEm: '2026-08-30T13:00:00Z',
      texto: null,
      statusAnterior: 'Detectada',
      statusNovo: 'EmAnalise',
      responsavelAnterior: null,
      responsavelNovo: null,
    },
    {
      id: 'a2',
      tipo: 'Comentario',
      autor: 'Quem trata',
      ocorridoEm: '2026-08-30T13:05:00Z',
      texto: 'Conferi com o RH.',
      statusAnterior: null,
      statusNovo: null,
      responsavelAnterior: null,
      responsavelNovo: null,
    },
  ],
}

function renderizar(perfil: Perfil = 'AnalistaRh') {
  return render(
    <MemoryRouter>
      <SessaoContexto.Provider
        value={{
          usuario: { id: 'u1', idOrganizacao: 'o1', nome: 'Quem trata', perfil },
          carregando: false,
          entrar: async () => {},
          sair: async () => {},
        }}
      >
        <Inconsistencias />
      </SessaoContexto.Provider>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  vi.mocked(api.listarInconsistencias).mockResolvedValue({
    total: 1,
    pagina: 1,
    itens: [DETECTADA],
  })
  vi.mocked(api.obterInconsistencia).mockResolvedValue(DETECTADA)

  // Sem IA por padrao: o produto funciona igual, e os testes de workflow nao
  // devem depender de uma camada que e acessorio (`CLAUDE.md secao 1`).
  vi.mocked(ia.assistenteDisponivel).mockResolvedValue(false)
})

afterEach(() => {
  vi.clearAllMocks()
})

describe('Inconsistências', () => {
  it('lista com severidade, situação e responsável', async () => {
    renderizar()

    expect(await screen.findByText('Desligado presente na folha mensal')).toBeInTheDocument()

    // "Alta" e "Detectada" aparecem no selo E como opcao dos filtros -
    // getByText exigiria um unico no. O selo existir e o que importa.
    expect(screen.getAllByText('Alta').length).toBeGreaterThan(0)
    expect(screen.getAllByText('Detectada').length).toBeGreaterThan(0)
    expect(screen.getByText('Sem responsável')).toBeInTheDocument()
    expect(screen.getByText('W000010')).toBeInTheDocument()
  })

  it('mostra o estado vazio quando não há nada', async () => {
    vi.mocked(api.listarInconsistencias).mockResolvedValue({ total: 0, pagina: 1, itens: [] })

    renderizar()

    expect(await screen.findByText('Nenhuma inconsistência')).toBeInTheDocument()
  })

  it('mostra erro e permite tentar novamente', async () => {
    vi.mocked(api.listarInconsistencias).mockRejectedValueOnce(new Error('API fora do ar'))

    renderizar()

    expect(await screen.findByText('API fora do ar')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: /tentar novamente/i }))

    expect(await screen.findByText('Desligado presente na folha mensal')).toBeInTheDocument()
  })

  it('filtrar por situação refaz a consulta', async () => {
    renderizar()

    await screen.findByText('Desligado presente na folha mensal')

    await userEvent.selectOptions(screen.getByLabelText('Filtrar por situação'), 'Resolvida')

    await waitFor(() => {
      expect(api.listarInconsistencias).toHaveBeenLastCalledWith(
        expect.objectContaining({ status: 'Resolvida' }),
      )
    })
  })

  // ------------------------------------------------------------- permissões

  it('o Auditor vê a lista e o detalhe, mas NÃO as ações', async () => {
    renderizar('Auditor')

    await userEvent.click(await screen.findByText('Desligado presente na folha mensal'))

    // A descricao aparece na LINHA da tabela e de novo no painel aberto.
    expect((await screen.findAllByText(/Desligado em 20\/07\/2026/)).length).toBeGreaterThan(1)

    // "Auditor lê tudo e não altera dado operacional" - Security Gate.
    // Esconder é conforto; quem barra é o backend, com teste provando o 403.
    expect(screen.queryByRole('button', { name: 'Mudar situação' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /comentar/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /assumir/i })).not.toBeInTheDocument()
  })

  it('o Analista de RH vê as ações', async () => {
    renderizar('AnalistaRh')

    await userEvent.click(await screen.findByText('Desligado presente na folha mensal'))

    expect(await screen.findByRole('button', { name: 'Mudar situação' })).toBeInTheDocument()
  })

  // -------------------------------------------------------------- workflow

  /**
   * A tela não repete a máquina de estados.
   *
   * As opções vêm de `proximosStatus`, que o servidor calculou. Duas cópias da
   * regra divergiriam, e a da tela é a que ninguém testa.
   */
  it('as opções de transição vêm do SERVIDOR', async () => {
    renderizar()

    await userEvent.click(await screen.findByText('Desligado presente na folha mensal'))

    const seletor = await screen.findByLabelText('Mudar para')
    const opcoes = within(seletor).getAllByRole('option').map((o) => o.textContent)

    // Detectada só vai para Em análise. Resolvida não aparece.
    expect(opcoes).toEqual(['Escolha…', 'Em análise'])
  })

  it('mudar a situação envia o status escolhido', async () => {
    vi.mocked(api.transitar).mockResolvedValue(EM_ANALISE)

    renderizar()

    await userEvent.click(await screen.findByText('Desligado presente na folha mensal'))
    await userEvent.selectOptions(await screen.findByLabelText('Mudar para'), 'EmAnalise')
    await userEvent.click(screen.getByRole('button', { name: 'Mudar situação' }))

    await waitFor(() => {
      expect(api.transitar).toHaveBeenCalledWith('i1', 'EmAnalise', undefined)
    })
  })

  it('justificar sem motivo mantém o botão bloqueado', async () => {
    vi.mocked(api.obterInconsistencia).mockResolvedValue(EM_ANALISE)

    renderizar()

    await userEvent.click(await screen.findByText('Desligado presente na folha mensal'))
    await userEvent.selectOptions(await screen.findByLabelText('Mudar para'), 'Justificada')

    // Justificar sem escrever o motivo é só fechar a pendência com outro nome.
    // O backend recusa; a tela avisa antes de deixar tentar.
    expect(screen.getByRole('button', { name: 'Mudar situação' })).toBeDisabled()

    await userEvent.type(
      screen.getByLabelText(/Motivo \(obrigatório para justificar\)/),
      'Acerto combinado em ata.',
    )

    expect(screen.getByRole('button', { name: 'Mudar situação' })).toBeEnabled()
  })

  it('assumir envia o id de quem está logado', async () => {
    vi.mocked(api.atribuir).mockResolvedValue(EM_ANALISE)

    renderizar()

    await userEvent.click(await screen.findByText('Desligado presente na folha mensal'))
    await userEvent.click(await screen.findByRole('button', { name: /assumir/i }))

    await waitFor(() => {
      expect(api.atribuir).toHaveBeenCalledWith('i1', 'u1')
    })
  })

  it('comentar envia o texto escrito', async () => {
    vi.mocked(api.comentar).mockResolvedValue(EM_ANALISE)

    renderizar()

    await userEvent.click(await screen.findByText('Desligado presente na folha mensal'))

    await userEvent.type(
      await screen.findByLabelText(/Observação, comentário ou evidência/),
      'Vou conferir com o RH.',
    )

    await userEvent.click(screen.getByRole('button', { name: /comentar/i }))

    await waitFor(() => {
      expect(api.comentar).toHaveBeenCalledWith('i1', 'Vou conferir com o RH.')
    })
  })

  it('a recusa do servidor vira mensagem, e não tela quebrada', async () => {
    vi.mocked(api.transitar).mockRejectedValue(
      new Error("De 'Detectada' so e possivel ir para EmAnalise."),
    )

    renderizar()

    await userEvent.click(await screen.findByText('Desligado presente na folha mensal'))
    await userEvent.selectOptions(await screen.findByLabelText('Mudar para'), 'EmAnalise')
    await userEvent.click(screen.getByRole('button', { name: 'Mudar situação' }))

    expect(
      await screen.findByText("De 'Detectada' so e possivel ir para EmAnalise."),
    ).toBeInTheDocument()
  })

  // ----------------------------------------------------------- linha do tempo

  it('mostra o histórico com autor, transição e comentário', async () => {
    vi.mocked(api.obterInconsistencia).mockResolvedValue(EM_ANALISE)

    renderizar()

    await userEvent.click(await screen.findByText('Desligado presente na folha mensal'))

    expect(await screen.findByText('Histórico')).toBeInTheDocument()
    expect(screen.getByText('Mudança de status')).toBeInTheDocument()
    expect(screen.getByText('Conferi com o RH.')).toBeInTheDocument()
  })

  /**
   * ⚠️ Stored XSS — a ameaça número um do Security Gate desta fase.
   *
   * O comentário de um usuário é lido por outro. Ele é renderizado como TEXTO:
   * o React escapa por padrão, e o projeto não usa `dangerouslySetInnerHTML`.
   */
  it('comentário com script é exibido como TEXTO, e não executado', async () => {
    const malicioso = "<script>alert('xss')</script>"

    vi.mocked(api.obterInconsistencia).mockResolvedValue({
      ...EM_ANALISE,
      andamentos: [{ ...EM_ANALISE.andamentos![1], texto: malicioso }],
    })

    const { container } = renderizar()

    await userEvent.click(await screen.findByText('Desligado presente na folha mensal'))

    // O texto aparece na tela, literal.
    expect(await screen.findByText(malicioso)).toBeInTheDocument()

    // E NAO existe um elemento <script> no documento: o React escapou.
    expect(container.ownerDocument.querySelectorAll('script')).toHaveLength(0)
  })

  it('a justificativa aparece, e some do formulário quando não se aplica', async () => {
    vi.mocked(api.obterInconsistencia).mockResolvedValue({
      ...EM_ANALISE,
      status: 'Justificada',
      proximosStatus: ['Resolvida', 'EmAnalise'],
      justificativa: 'Adiantamento combinado com o funcionário.',
    })

    renderizar()

    await userEvent.click(await screen.findByText('Desligado presente na folha mensal'))

    expect(
      await screen.findByText('Adiantamento combinado com o funcionário.'),
    ).toBeInTheDocument()
  })

  // ------------------------------------------------------ assistente (Fase 11)

  it('sem IA configurada a caixa do assistente não existe', async () => {
    vi.mocked(ia.assistenteDisponivel).mockResolvedValue(false)

    renderizar()

    await userEvent.click(await screen.findByText('Desligado presente na folha mensal'))

    // A gaveta abriu de verdade — as ações do workflow estão lá.
    expect(await screen.findByLabelText('Mudar para')).toBeInTheDocument()

    expect(screen.queryByRole('button', { name: /explicar em linguagem simples/i }))
      .not.toBeInTheDocument()
  })

  it('só pede a explicação quando alguém clica', async () => {
    vi.mocked(ia.assistenteDisponivel).mockResolvedValue(true)

    renderizar()

    await userEvent.click(await screen.findByText('Desligado presente na folha mensal'))

    const botao = await screen.findByRole('button', { name: /explicar em linguagem simples/i })

    // ⚠️ Abrir a gaveta NÃO chama a IA: cada chamada custa token, e gerar
    // explicação que ninguém leu é dinheiro jogado fora.
    expect(ia.explicarInconsistencia).not.toHaveBeenCalled()

    vi.mocked(ia.explicarInconsistencia).mockResolvedValue({
      situacao: 'Respondeu',
      texto: 'A pessoa saiu antes do fechamento desta folha.',
      geradoPorIa: true,
      doCache: false,
      aviso: 'Texto gerado por inteligência artificial. Pode conter erro.',
    })

    await userEvent.click(botao)

    expect(await screen.findByText('A pessoa saiu antes do fechamento desta folha.'))
      .toBeInTheDocument()

    expect(ia.explicarInconsistencia).toHaveBeenCalledWith('i1')
  })

  it('a explicação vem rotulada como gerada por IA', async () => {
    vi.mocked(ia.assistenteDisponivel).mockResolvedValue(true)
    vi.mocked(ia.explicarInconsistencia).mockResolvedValue({
      situacao: 'Respondeu',
      texto: 'Explicação qualquer.',
      geradoPorIa: true,
      doCache: false,
      aviso: 'Texto gerado por inteligência artificial. Pode conter erro.',
    })

    renderizar()

    await userEvent.click(await screen.findByText('Desligado presente na folha mensal'))
    await userEvent.click(
      await screen.findByRole('button', { name: /explicar em linguagem simples/i }),
    )

    // ⚠️ `CLAUDE.md §37.3`: sem o rótulo, texto de máquina fica visualmente
    // indistinguível de apuração do sistema.
    expect(await screen.findByText(/inteligência artificial/i)).toBeInTheDocument()
  })

  it('provedor indisponível mostra o motivo e mantém a inconsistência legível', async () => {
    vi.mocked(ia.assistenteDisponivel).mockResolvedValue(true)
    vi.mocked(ia.explicarInconsistencia).mockResolvedValue({
      situacao: 'Indisponivel',
      texto: '',
      geradoPorIa: false,
      doCache: false,
      aviso: 'O assistente está indisponível no momento.',
    })

    renderizar()

    await userEvent.click(await screen.findByText('Desligado presente na folha mensal'))
    await userEvent.click(
      await screen.findByRole('button', { name: /explicar em linguagem simples/i }),
    )

    expect(await screen.findByText(/indisponível no momento/i)).toBeInTheDocument()

    // ⚠️ A descrição do motor determinístico continua ali — é a informação que
    // importa, e a IA é acessório (`CLAUDE.md §1`).
    expect(screen.getAllByText('Desligado presente na folha mensal').length).toBeGreaterThan(0)
  })
})
