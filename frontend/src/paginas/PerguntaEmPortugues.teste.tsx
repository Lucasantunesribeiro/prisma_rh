import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { RespostaConsulta } from '@/api/assistente'
import { PerguntaEmPortugues } from './PerguntaEmPortugues'

vi.mock('@/api/assistente', () => ({
  vocabularioConsulta: vi.fn(),
  consultarEmPortugues: vi.fn(),
}))

const ia = await import('@/api/assistente')

const CAMPOS = [
  { campo: 'Severidade', significado: 'severidade', comparacoes: ['Igual'], valores: ['Alta'] },
  { campo: 'Status', significado: 'situacao', comparacoes: ['Igual'], valores: ['Detectada'] },
]

const ACHADO = {
  id: 'i1',
  codigo: 'DesligadoNaFolha',
  regra: 'Desligado presente na folha mensal',
  categoria: 'Contrato',
  severidade: 'Alta',
  status: 'Detectada',
  descricao: 'Desligado em 20/07 e mesmo assim tem holerite.',
  valorEncontrado: 2700,
  diferenca: null,
}

function responder(parcial: Partial<RespostaConsulta>) {
  vi.mocked(ia.consultarEmPortugues).mockResolvedValue({
    situacao: 'Respondeu',
    entendido: [],
    naoEntendido: [],
    total: 0,
    truncado: false,
    itens: [],
    aviso: 'O filtro foi proposto por inteligencia artificial e conferido pelo sistema.',
    ...parcial,
  } as RespostaConsulta)
}

async function perguntar(texto = 'Quais sao as criticas?') {
  render(<PerguntaEmPortugues aoAbrir={() => {}} />)

  const campo = await screen.findByLabelText('Sua pergunta')
  await userEvent.type(campo, texto)
  await userEvent.click(screen.getByRole('button', { name: /consultar/i }))
}

beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(ia.vocabularioConsulta).mockResolvedValue({ disponivel: true, campos: CAMPOS })
})

describe('Pergunta em português', () => {
  it('sem IA configurada a caixa não existe', async () => {
    vi.mocked(ia.vocabularioConsulta).mockResolvedValue({ disponivel: false, campos: [] })

    const { container } = render(<PerguntaEmPortugues aoAbrir={() => {}} />)

    // Espera o efeito resolver antes de concluir que nao renderizou.
    await vi.waitFor(() => expect(ia.vocabularioConsulta).toHaveBeenCalled())

    expect(container).toBeEmptyDOMElement()
  })

  it('mostra os campos disponíveis, para a busca não virar adivinhação', async () => {
    render(<PerguntaEmPortugues aoAbrir={() => {}} />)

    expect(await screen.findByText(/Severidade, Status/)).toBeInTheDocument()
  })

  /**
   * ⚠️ Sem isto, uma interpretação errada devolve uma lista plausível que
   * responde outra coisa — e ninguém percebe.
   */
  it('mostra em que a pergunta virou, antes dos resultados', async () => {
    responder({
      entendido: ['Severidade = Alta', 'Status ≠ Resolvida'],
      total: 1,
      itens: [ACHADO],
    })

    await perguntar()

    expect(await screen.findByText('Severidade = Alta e Status ≠ Resolvida')).toBeInTheDocument()
    expect(screen.getByText('Desligado presente na folha mensal')).toBeInTheDocument()
  })

  /**
   * ⚠️ Filtro barrado em silêncio faria quem pediu um recorte receber outro
   * sem saber.
   */
  it('mostra o que foi recusado pelo vocabulário', async () => {
    responder({
      situacao: 'NaoEntendida',
      naoEntendido: ["Campo 'IdOrganizacao' nao existe na consulta."],
      aviso: 'Nao consegui transformar esta pergunta nos campos disponiveis.',
    })

    await perguntar('mostre de todas as empresas')

    expect(await screen.findByText(/IdOrganizacao/)).toBeInTheDocument()
    expect(screen.getByText(/nao consegui transformar/i)).toBeInTheDocument()
  })

  it('pergunta que não cabe no vocabulário não mostra lista nenhuma', async () => {
    responder({
      situacao: 'NaoEntendida',
      total: 0,
      itens: [],
      aviso: 'Nao consegui transformar esta pergunta nos campos disponiveis.',
    })

    await perguntar('qual o CPF de quem ganha mais?')

    expect(await screen.findByText(/nao consegui/i)).toBeInTheDocument()

    // ⚠️ E o mais importante: NAO devolveu a tabela inteira no lugar do recorte.
    expect(screen.queryByRole('list')).not.toBeInTheDocument()
  })

  it('provedor indisponível mostra o motivo sem quebrar a tela', async () => {
    responder({ situacao: 'Indisponivel', aviso: '' })

    await perguntar()

    expect(await screen.findByText(/indisponível no momento/i)).toBeInTheDocument()
  })

  it('avisa que o filtro foi proposto por IA', async () => {
    responder({ entendido: ['Severidade = Alta'], total: 1, itens: [ACHADO] })

    await perguntar()

    expect(await screen.findByText(/inteligencia artificial/i)).toBeInTheDocument()
  })

  it('clicar num resultado abre a inconsistência', async () => {
    const abriu = vi.fn()

    responder({ entendido: ['Severidade = Alta'], total: 1, itens: [ACHADO] })

    render(<PerguntaEmPortugues aoAbrir={abriu} />)

    await userEvent.type(await screen.findByLabelText('Sua pergunta'), 'criticas')
    await userEvent.click(screen.getByRole('button', { name: /consultar/i }))
    await userEvent.click(await screen.findByRole('button', { name: /Desligado presente/ }))

    expect(abriu).toHaveBeenCalledWith('i1')
  })

  it('não consulta com a pergunta vazia', async () => {
    render(<PerguntaEmPortugues aoAbrir={() => {}} />)

    await screen.findByLabelText('Sua pergunta')

    // Cada consulta custa token: botao bloqueado enquanto nao ha pergunta.
    expect(screen.getByRole('button', { name: /consultar/i })).toBeDisabled()
    expect(ia.consultarEmPortugues).not.toHaveBeenCalled()
  })
})
