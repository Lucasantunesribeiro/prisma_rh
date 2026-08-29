import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
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
  // O aviso de 36 dias PROJETA a saída: é ela que vai para a CTPS.
  dataProjetada: '2026-06-25',
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
    avos: 6,
    fracao: '6/12',
    meses: [],
  },
  diasFeriasVencidas: 60,
  avos13: 5,
  fracao13: '5/12',
  avosDoAviso: 1,
  valorBaseFgts: {
    informado: 10000,
    conhecidoPeloSistema: 8000,
    abaixoDoConhecido: false,
    observacao: 'Extrato do FGTS Digital',
    informadoEm: '2026-08-29T14:05:00Z',
  },
  fgtsConhecidoPeloSistema: 8000,
  total: 17100,
  verbas: [
    { codigo: 'SALDO', nome: 'Saldo de salario', valor: 2000, referencia: '20/30', memoria: [] },
    {
      codigo: 'AVISO',
      nome: 'Aviso previo indenizado',
      valor: 3600,
      referencia: '36 dias',
      memoria: [],
    },
    {
      codigo: 'DEC13PROP',
      nome: '13o salario proporcional',
      valor: 1250,
      referencia: '5/12',
      memoria: [],
    },
    {
      codigo: 'DEC13AV',
      nome: '13o sobre o aviso previo indenizado',
      valor: 250,
      referencia: '1/12',
      memoria: [],
    },
    {
      codigo: 'MULTAFGTS',
      nome: 'Indenizacao compensatoria do FGTS',
      valor: 4000,
      referencia: '40%',
      memoria: [],
    },
  ],
}

const BLOQUEADA = {
  ...SUPORTADA,
  motivo: 'Aposentadoria',
  dataProjetada: '2026-05-20',
  suportado: false,
  motivoDoBloqueio:
    'A aposentadoria espontanea NAO extingue por si o contrato de trabalho, e o tratamento das verbas depende do que aconteceu depois dela.',
  fonte: 'sem fonte oficial alcancada',
  aviso: null,
  avosDoAviso: 0,
  valorBaseFgts: null,
  total: 0,
  verbas: [],
}

/** Devolve o corpo em todo GET; 204 no PUT, como faz a API de verdade. */
function renderizar(corpo: unknown) {
  const fetchFalso = vi.fn().mockImplementation((_url: string, init?: RequestInit) =>
    Promise.resolve(
      init?.method === 'PUT' ? new Response(null, { status: 204 }) : responder(corpo),
    ),
  )

  vi.stubGlobal('fetch', fetchFalso)

  return { ...render(<SecaoRescisao idContrato="c1" />), fetchFalso }
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

    expect(screen.getByText(/R\$\s*17\.100,00/)).toBeInTheDocument()

    // A fonte fica visível: quem confere uma rescisão precisa saber de onde
    // cada regra veio.
    expect(screen.getByText(/Lei 8\.036\/1990 art\. 18/)).toBeInTheDocument()
  })

  it('o 13º aparece em DUAS verbas: o proporcional e o do aviso', async () => {
    renderizar(SUPORTADA)

    // Separadas de propósito: o 13º sobre o aviso indenizado tem INSS e FGTS,
    // mas NÃO tem IRRF. Numa linha só, a base de imposto sairia errada.
    const proporcional = (await screen.findByText('13o salario proporcional')).closest('tr')!
    expect(within(proporcional).getByText(/R\$\s*1\.250,00/)).toBeInTheDocument()

    const doAviso = screen.getByText('13o sobre o aviso previo indenizado').closest('tr')!
    expect(within(doAviso).getByText(/R\$\s*250,00/)).toBeInTheDocument()
  })

  it('mostra a saída projetada pelo aviso, que é a que vai para a CTPS', async () => {
    renderizar(SUPORTADA)

    expect(await screen.findByText('25/06/2026')).toBeInTheDocument()
    expect(screen.getByText(/pela projeção do aviso/)).toBeInTheDocument()
  })

  it('o valor base do FGTS é digitado, e o conhecido aparece ao lado', async () => {
    renderizar(SUPORTADA)

    const caixa = await screen.findByLabelText('Valor base do FGTS para fins rescisórios')

    // Vem preenchida com o que está GRAVADO — o valor deixou de ser um
    // parâmetro de leitura e passou a ser um dado do contrato.
    expect(caixa).toHaveValue('10000.00')

    // O que o sistema sabe aparece para comparação, nunca como substituto.
    expect(screen.getByText(/R\$\s*8\.000,00/)).toBeInTheDocument()
    expect(screen.getByText(/correção e juros que este sistema não conhece/)).toBeInTheDocument()
    expect(screen.getByText(/Informado em 29\/08\/2026/)).toBeInTheDocument()
  })

  it('salvar GRAVA por PUT e reapura', async () => {
    const { fetchFalso } = renderizar(SUPORTADA)

    await screen.findByLabelText('Valor base do FGTS para fins rescisórios')

    const usuario = userEvent.setup()
    await usuario.click(screen.getByRole('button', { name: 'Salvar' }))

    await waitFor(() => {
      const puts = fetchFalso.mock.calls.filter(([, init]) => init?.method === 'PUT')

      expect(puts).toHaveLength(1)
      expect(puts[0][0]).toContain('/rescisao/valor-base-fgts')
      // No CORPO, não na query string: é um dado gravado com autor e data.
      expect(JSON.parse(puts[0][1].body as string)).toEqual({ valor: 10000 })
    })
  })

  it('sem valor base informado, avisa que a multa NÃO é calculada', async () => {
    renderizar({
      ...SUPORTADA,
      valorBaseFgts: null,
      verbas: SUPORTADA.verbas.filter((v) => v.codigo !== 'MULTAFGTS'),
    })

    expect(await screen.findByText(/multa do FGTS não é calculada/)).toBeInTheDocument()
    expect(screen.queryByText('Indenizacao compensatoria do FGTS')).not.toBeInTheDocument()

    // A caixa fica vazia: nulo é "não informado", e zero seria outra coisa.
    expect(screen.getByLabelText('Valor base do FGTS para fins rescisórios')).toHaveValue('')
  })

  it('avisa quando o informado está abaixo do que o sistema depositou', async () => {
    renderizar({
      ...SUPORTADA,
      valorBaseFgts: {
        informado: 5000,
        conhecidoPeloSistema: 8000,
        abaixoDoConhecido: true,
        observacao: null,
        informadoEm: '2026-08-29T14:05:00Z',
      },
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

  it('pedido de demissão: o aviso é devido PELO empregado e não projeta', async () => {
    renderizar({
      ...SUPORTADA,
      motivo: 'PedidoDeDemissao',
      // Sem aviso indenizado não há projeção: a saída é a data de desligamento.
      dataProjetada: '2026-05-20',
      avosDoAviso: 0,
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
    expect(screen.queryByText(/pela projeção do aviso/)).not.toBeInTheDocument()
  })
})
