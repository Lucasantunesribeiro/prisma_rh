import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ExecucaoAnalise } from '@/api/analises'
import { SecaoAnalise } from './SecaoAnalise'

vi.mock('@/api/analises', async (original) => ({
  ...(await original<typeof import('@/api/analises')>()),
  listarExecucoes: vi.fn(),
  obterExecucao: vi.fn(),
  analisarFolha: vi.fn(),
}))

const api = await import('@/api/analises')

const COM_ACHADOS: ExecucaoAnalise = {
  id: 'e1',
  idFolha: 'f1',
  competencia: '2026-08',
  versaoCalculoDaFolha: 2,
  executadaEm: '2026-08-30T13:00:00Z',
  regrasExecutadas: 6,
  totalResultados: 2,
  resultadosAltos: 1,
  resultadosMedios: 1,
  resultadosBaixos: 0,
  desatualizada: false,
  resultados: [
    {
      id: 'r1',
      codigo: 'DesligadoNaFolha',
      regra: 'Desligado presente na folha mensal',
      versaoRegra: 1,
      categoria: 'Contrato',
      severidade: 'Alta',
      idFolhaFuncionario: 'h1',
      matricula: 'A000010',
      nomeFuncionario: 'Quem Saiu',
      descricao: 'Desligado em 20/07/2026, antes de 08/2026, e mesmo assim tem holerite.',
      valorEsperado: null,
      valorEncontrado: 2700,
      diferenca: null,
      contexto: 'desligamento=2026-07-20',
    },
    {
      id: 'r2',
      codigo: 'DescontoAcimaDoLimite',
      regra: 'Desconto acima do limite',
      versaoRegra: 1,
      categoria: 'Valores',
      severidade: 'Media',
      idFolhaFuncionario: 'h2',
      matricula: 'A000020',
      nomeFuncionario: 'Desconto Alto',
      descricao: 'Descontos de R$ 800,00 sobre proventos de R$ 1.000,00.',
      valorEsperado: 700,
      valorEncontrado: 800,
      diferenca: 100,
      contexto: 'percentualPraticado=80',
    },
  ],
}

const LIMPA: ExecucaoAnalise = {
  ...COM_ACHADOS,
  id: 'e2',
  totalResultados: 0,
  resultadosAltos: 0,
  resultadosMedios: 0,
  resultados: [],
}

function renderizar(
  { situacao = 'Calculada', podeExecutar = true } = {},
) {
  return render(
    <SecaoAnalise idFolha="f1" situacao={situacao} podeExecutar={podeExecutar} />,
  )
}

beforeEach(() => {
  vi.mocked(api.listarExecucoes).mockResolvedValue({ total: 0, pagina: 1, itens: [] })
})

afterEach(() => {
  vi.clearAllMocks()
})

describe('Conferência da folha', () => {
  it('diz que a folha ainda não foi conferida quando não há execução', async () => {
    renderizar()

    // Folha sem achado e folha nunca analisada mostrariam zero inconsistências
    // se a tela não distinguisse as duas — e são situações opostas.
    expect(await screen.findByText(/ainda não foi conferida/)).toBeInTheDocument()
  })

  it('mostra os achados da última execução, com severidade e valores', async () => {
    vi.mocked(api.listarExecucoes).mockResolvedValue({
      total: 1,
      pagina: 1,
      itens: [COM_ACHADOS],
    })
    vi.mocked(api.obterExecucao).mockResolvedValue(COM_ACHADOS)

    renderizar()

    expect(await screen.findByText('Desligado presente na folha mensal')).toBeInTheDocument()
    expect(screen.getByText('Desconto acima do limite')).toBeInTheDocument()
    expect(screen.getByText('Alta')).toBeInTheDocument()
    expect(screen.getByText('Média')).toBeInTheDocument()
    expect(screen.getByText('A000010 — Quem Saiu')).toBeInTheDocument()
    expect(screen.getByText('6 regras em 30/08/2026, 10:00:00')).toBeInTheDocument()
  })

  it('diz explicitamente quando não há inconsistência', async () => {
    vi.mocked(api.listarExecucoes).mockResolvedValue({ total: 1, pagina: 1, itens: [LIMPA] })
    vi.mocked(api.obterExecucao).mockResolvedValue(LIMPA)

    renderizar()

    expect(await screen.findByText('Nenhuma inconsistência encontrada.')).toBeInTheDocument()
  })

  it('avisa quando a folha foi recalculada depois da análise', async () => {
    const velha = { ...COM_ACHADOS, desatualizada: true }

    vi.mocked(api.listarExecucoes).mockResolvedValue({ total: 1, pagina: 1, itens: [velha] })
    vi.mocked(api.obterExecucao).mockResolvedValue(velha)

    renderizar()

    // Dizer que envelheceu é melhor que apagar: apagar perderia o histórico.
    expect(await screen.findByText(/fala de uma versão anterior/)).toBeInTheDocument()
  })

  it('analisar chama a API e mostra o resultado', async () => {
    vi.mocked(api.analisarFolha).mockResolvedValue(COM_ACHADOS)

    renderizar()

    await userEvent.click(await screen.findByRole('button', { name: 'Analisar' }))

    await waitFor(() => {
      expect(api.analisarFolha).toHaveBeenCalledWith('f1')
    })

    expect(await screen.findByText('Desligado presente na folha mensal')).toBeInTheDocument()
  })

  it('em rascunho a análise fica bloqueada, com o motivo', async () => {
    renderizar({ situacao: 'Rascunho' })

    expect(await screen.findByText(/Calcule a folha antes de conferir/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Analisar' })).toBeDisabled()
  })

  it('quem não processa folha não vê o botão de analisar', async () => {
    vi.mocked(api.listarExecucoes).mockResolvedValue({ total: 1, pagina: 1, itens: [LIMPA] })
    vi.mocked(api.obterExecucao).mockResolvedValue(LIMPA)

    renderizar({ podeExecutar: false })

    expect(await screen.findByText('Nenhuma inconsistência encontrada.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /analisar/i })).not.toBeInTheDocument()
  })

  it('erro da API vira mensagem, e não tela quebrada', async () => {
    vi.mocked(api.listarExecucoes).mockRejectedValue(new Error('API fora do ar'))

    renderizar()

    expect(await screen.findByText('API fora do ar')).toBeInTheDocument()
  })
})
