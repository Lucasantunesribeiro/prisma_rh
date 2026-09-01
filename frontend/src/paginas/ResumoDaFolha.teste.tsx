import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { ResumoExecutivo, RetratoDaFolha } from '@/api/assistente'
import { ResumoDaFolha } from './ResumoDaFolha'

vi.mock('@/api/assistente', () => ({
  assistenteDisponivel: vi.fn(),
  resumirFolha: vi.fn(),
}))

const ia = await import('@/api/assistente')

const RETRATO: RetratoDaFolha = {
  competencia: '08/2026',
  tipo: 'Mensal',
  situacao: 'Aberta',
  versaoCalculo: 2,
  holerites: 12,
  totalProventos: 48000,
  totalDescontos: 9500,
  totalLiquido: 38500,
  inconsistencias: 6,
  pendentes: 4,
  porSeveridade: [
    { rotulo: 'Alta', quantidade: 2 },
    { rotulo: 'Media', quantidade: 4 },
  ],
  porCategoria: [{ rotulo: 'Contrato', quantidade: 6 }],
  competenciaAnterior: '07/2026',
  variacaoLiquido: 1200,
  inconsistenciasAnterior: 3,
}

function responder(parcial: Partial<ResumoExecutivo>) {
  vi.mocked(ia.resumirFolha).mockResolvedValue({
    situacao: 'Respondeu',
    retrato: RETRATO,
    texto: 'A folha tem seis divergencias, quatro ainda pendentes.',
    geradoPorIa: true,
    doCache: false,
    aviso: 'Texto gerado por inteligencia artificial. Os numeros ao lado vem do calculo do sistema.',
    ...parcial,
  } as ResumoExecutivo)
}

async function gerar() {
  render(<ResumoDaFolha idFolha="f1" />)
  await userEvent.click(await screen.findByRole('button', { name: /gerar resumo/i }))
}

beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(ia.assistenteDisponivel).mockResolvedValue(true)
})

describe('Resumo executivo da folha', () => {
  it('sem IA configurada a caixa não existe', async () => {
    vi.mocked(ia.assistenteDisponivel).mockResolvedValue(false)

    const { container } = render(<ResumoDaFolha idFolha="f1" />)

    await vi.waitFor(() => expect(ia.assistenteDisponivel).toHaveBeenCalled())

    expect(container).toBeEmptyDOMElement()
  })

  it('só gera quando alguém clica', async () => {
    render(<ResumoDaFolha idFolha="f1" />)

    await screen.findByRole('button', { name: /gerar resumo/i })

    // Cada resumo custa token: abrir a folha nao paga por texto que ninguem leu.
    expect(ia.resumirFolha).not.toHaveBeenCalled()
  })

  /**
   * ⚠️ **O teste que a 11B existe para satisfazer.**
   *
   * Com o provedor fora do ar a tela perde o parágrafo e **mantém os números**,
   * porque eles nunca vieram do modelo (`ROADMAP.md`, Fase 11B).
   */
  it('provedor fora do ar: some a prosa, ficam os números', async () => {
    responder({ situacao: 'Indisponivel', texto: '', geradoPorIa: false })

    await gerar()

    expect(await screen.findByText(/indisponível no momento/i)).toBeInTheDocument()

    expect(screen.getByText('12')).toBeInTheDocument()
    expect(screen.getByText('6')).toBeInTheDocument()
    expect(screen.getByText('4')).toBeInTheDocument()
  })

  it('mostra os números do sistema ao lado da prosa', async () => {
    responder({})

    await gerar()

    expect(await screen.findByText(/seis divergencias/)).toBeInTheDocument()

    expect(screen.getByText('Holerites')).toBeInTheDocument()
    expect(screen.getByText('12')).toBeInTheDocument()
    expect(screen.getByText(/Alta: 2/)).toBeInTheDocument()
    expect(screen.getByText(/contra 07\/2026/)).toBeInTheDocument()
  })

  /**
   * ⚠️ Sem o rótulo, texto de máquina fica visualmente indistinguível de
   * apuração do sistema (`CLAUDE.md §37.3`).
   */
  it('a prosa vem rotulada como gerada por IA', async () => {
    responder({})

    await gerar()

    expect(await screen.findByText(/inteligencia artificial/i)).toBeInTheDocument()
  })

  it('falha de rede mostra erro sem quebrar a página', async () => {
    vi.mocked(ia.resumirFolha).mockRejectedValue(new Error('sem rede'))

    await gerar()

    expect(await screen.findByRole('alert')).toHaveTextContent(/não foi possível/i)
  })
})
