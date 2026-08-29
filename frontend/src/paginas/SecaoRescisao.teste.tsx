import { render, screen, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { SecaoRescisao } from './SecaoRescisao'

function responder(corpo: unknown): Response {
  return new Response(JSON.stringify(corpo), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}

const SUPORTADA = {
  idContrato: 'c1',
  matricula: '1001',
  motivo: 'DispensaSemJustaCausa',
  dataDesligamento: '2026-05-20',
  salarioReferencia: 3000,
  suportado: true,
  motivoDoBloqueio: null,
  fonte: 'Lei 8.036/1990 art. 18 par. 1o; Lei 12.506/2011; Sumula 171 do TST',
  aviso: {
    devedor: 'Empregador',
    anosCompletos: 2,
    diasBase: 30,
    diasAcrescidos: 6,
    dias: 36,
    reduzido: false,
  },
  feriasProporcionais: {
    inicioPeriodo: '2026-01-10',
    fimPeriodo: '2027-01-09',
    avos: 4,
    fracao: '4/12',
    meses: [],
  },
  diasFeriasVencidas: 60,
  avos13: 5,
  fracao13: '5/12',
  valorBaseFgts: { informado: 10000, conhecidoPeloSistema: 8000, abaixoDoConhecido: false },
  total: 14933.33,
  verbas: [
    { codigo: 'SALDO', nome: 'Saldo de salario', valor: 2000, referencia: '20/30', memoria: [] },
    { codigo: 'AVISO', nome: 'Aviso previo indenizado', valor: 3600, referencia: '36 dias', memoria: [] },
    { codigo: 'MULTAFGTS', nome: 'Indenizacao compensatoria do FGTS', valor: 4000, referencia: '40%', memoria: [] },
  ],
}

const BLOQUEADA = {
  ...SUPORTADA,
  motivo: 'Aposentadoria',
  suportado: false,
  motivoDoBloqueio:
    'A aposentadoria espontanea NAO extingue por si o contrato de trabalho, e o tratamento das verbas depende do que aconteceu depois dela.',
  fonte: 'sem fonte oficial alcancada',
  aviso: null,
  valorBaseFgts: null,
  total: 0,
  verbas: [],
}

function renderizar(corpo: unknown) {
  vi.stubGlobal('fetch', vi.fn().mockImplementation(() => Promise.resolve(responder(corpo))))

  return render(<SecaoRescisao idContrato="c1" />)
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('SecaoRescisao', () => {
  it('mostra as verbas, o total e a fonte', async () => {
    renderizar(SUPORTADA)

    // Esperar por algo que só existe DEPOIS do fetch, não pelo cabeçalho.
    expect(await screen.findByText('Saldo de salario')).toBeInTheDocument()

    const multa = screen.getByText('Indenizacao compensatoria do FGTS').closest('tr')!
    expect(within(multa).getByText('40%')).toBeInTheDocument()
    expect(within(multa).getByText(/R\$\s*4\.000,00/)).toBeInTheDocument()

    expect(screen.getByText(/R\$\s*14\.933,33/)).toBeInTheDocument()

    // A fonte fica visível: quem confere uma rescisão precisa saber de onde
    // cada regra veio.
    expect(screen.getByText(/Lei 8\.036\/1990 art\. 18/)).toBeInTheDocument()
  })

  it('o valor base do FGTS é digitado, e o conhecido aparece ao lado', async () => {
    renderizar(SUPORTADA)

    expect(
      await screen.findByLabelText('Valor base do FGTS para fins rescisórios'),
    ).toBeInTheDocument()

    // O que o sistema sabe aparece para comparação, nunca como substituto.
    expect(screen.getByText(/R\$\s*8\.000,00/)).toBeInTheDocument()
    expect(screen.getByText(/correção e juros que este sistema não conhece/)).toBeInTheDocument()
  })

  it('avisa quando o informado está abaixo do que o sistema depositou', async () => {
    renderizar({
      ...SUPORTADA,
      valorBaseFgts: { informado: 5000, conhecidoPeloSistema: 8000, abaixoDoConhecido: true },
    })

    expect(await screen.findByText(/A multa sairia menor que a devida/)).toBeInTheDocument()
  })

  it('motivo bloqueado: explica e NÃO mostra número algum', async () => {
    renderizar(BLOQUEADA)

    expect(
      await screen.findByText(/Este motivo não é calculado pelo Prisma RH/),
    ).toBeInTheDocument()
    expect(screen.getByText(/aposentadoria espontanea NAO extingue/)).toBeInTheDocument()

    // Sem tabela de verbas e sem campo de valor base: um número com cara de
    // exato sobre regra não confirmada seria pior que nenhum número.
    expect(screen.queryByText('Saldo de salario')).not.toBeInTheDocument()
    expect(
      screen.queryByLabelText('Valor base do FGTS para fins rescisórios'),
    ).not.toBeInTheDocument()

    // Mas o CONTEXTO continua: dias de férias e avos de 13º.
    expect(screen.getByText('60 dias')).toBeInTheDocument()
    expect(screen.getByText('5/12')).toBeInTheDocument()
  })

  it('mostra quem deve o aviso prévio', async () => {
    renderizar(SUPORTADA)

    // "36 dias" também aparece na coluna Ref. da verba, então a busca é
    // escopada ao cartão - senão o teste passa ou falha por qual dos dois o
    // DOM devolveu primeiro.
    const cartao = (await screen.findByText('Aviso prévio')).closest('div')!

    expect(within(cartao).getByText('36 dias')).toBeInTheDocument()
    expect(within(cartao).getByText(/devido pelo empregador/)).toBeInTheDocument()
  })

  it('pedido de demissão: o aviso é devido PELO empregado', async () => {
    renderizar({
      ...SUPORTADA,
      motivo: 'PedidoDeDemissao',
      aviso: {
        devedor: 'Empregado',
        anosCompletos: 2,
        diasBase: 30,
        diasAcrescidos: 0,
        dias: 30,
        reduzido: false,
      },
    })

    expect(await screen.findByText('30 dias')).toBeInTheDocument()
    expect(screen.getByText(/devido pelo empregado/)).toBeInTheDocument()
  })
})
