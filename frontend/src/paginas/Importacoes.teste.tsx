import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { Perfil } from '@/api/autenticacao'
import type {
  ConfirmacaoImportacao,
  ImportacaoDetalhe,
  ImportacaoResumo,
  PreviewImportacao,
} from '@/api/importacoes'
import { SessaoContexto } from '@/auth/contexto'
import Importacoes from './Importacoes'

vi.mock('@/api/importacoes', async (original) => ({
  ...(await original<typeof import('@/api/importacoes')>()),
  listarImportacoes: vi.fn(),
  obterImportacao: vi.fn(),
  previewFuncionarios: vi.fn(),
  confirmarFuncionarios: vi.fn(),
  baixarModelo: vi.fn(),
}))

const api = await import('@/api/importacoes')

const HISTORICO: ImportacaoResumo[] = [
  {
    id: 'i1',
    nomeOriginalArquivo: 'admissoes-agosto.csv',
    formato: 'Csv',
    tamanhoBytes: 2048,
    hashSha256: 'a'.repeat(64),
    enviadaEm: '2026-08-30T13:45:00Z',
    status: 'Aplicada',
    totalLinhas: 2,
    linhasValidas: 2,
    linhasComErro: 0,
  },
  {
    id: 'i2',
    nomeOriginalArquivo: 'lote-errado.xlsx',
    formato: 'Xlsx',
    tamanhoBytes: 9000,
    hashSha256: 'b'.repeat(64),
    enviadaEm: '2026-08-29T10:00:00Z',
    status: 'Recusada',
    totalLinhas: 3,
    linhasValidas: 1,
    linhasComErro: 2,
  },
]

const PREVIA_BOA: PreviewImportacao = {
  nomeArquivo: 'funcionarios.csv',
  formato: 'Csv',
  tamanhoBytes: 120,
  hashSha256: 'c'.repeat(64),
  colunas: ['nome', 'cpf', 'data de nascimento'],
  mapeamento: { nome: 'nome', cpf: 'cpf', dataNascimento: 'data de nascimento' },
  total: 1,
  validas: 1,
  comErro: 0,
  importavel: true,
  errosDoArquivo: [],
  linhas: [
    {
      linha: 2,
      nome: 'Ana Paula',
      cpf: '111.***.**7-35',
      dataNascimento: '1991-03-14',
      erros: [],
    },
  ],
}

const PREVIA_RUIM: PreviewImportacao = {
  ...PREVIA_BOA,
  total: 1,
  validas: 0,
  comErro: 1,
  importavel: false,
  linhas: [
    {
      linha: 2,
      nome: 'Bruno',
      cpf: null,
      dataNascimento: null,
      erros: ['CPF invalido.', 'Data de nascimento invalida. Use dd/mm/aaaa ou aaaa-mm-dd.'],
    },
  ],
}

function arquivo(nome = 'funcionarios.csv'): File {
  return new File(['nome;cpf;data de nascimento\n'], nome, { type: 'text/csv' })
}

function renderizar(perfil: Perfil = 'AnalistaRh') {
  return render(
    <MemoryRouter>
      <SessaoContexto.Provider
        value={{
          usuario: {
            id: 'u1',
            idOrganizacao: 'o1',
            nome: 'Quem importa',
            email: 'quem.importa@teste.exemplo',
            perfil,
          },
          carregando: false,
          entrar: async () => {},
          sair: async () => {},
        }}
      >
        <Importacoes />
      </SessaoContexto.Provider>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  vi.mocked(api.listarImportacoes).mockResolvedValue({
    total: HISTORICO.length,
    pagina: 1,
    itens: HISTORICO,
  })
})

afterEach(() => {
  vi.clearAllMocks()
})

describe('Importações', () => {
  // ------------------------------------------------------------- histórico

  it('lista o histórico com formato e situação', async () => {
    renderizar()

    expect(await screen.findByText('admissoes-agosto.csv')).toBeInTheDocument()
    expect(screen.getByText('lote-errado.xlsx')).toBeInTheDocument()
    expect(screen.getByText('Aplicada')).toBeInTheDocument()
    expect(screen.getByText('Recusada')).toBeInTheDocument()
    expect(screen.getByText('XLSX')).toBeInTheDocument()
  })

  it('mostra o estado vazio quando não há importação nenhuma', async () => {
    vi.mocked(api.listarImportacoes).mockResolvedValue({ total: 0, pagina: 1, itens: [] })

    renderizar()

    expect(await screen.findByText('Nenhuma importação ainda')).toBeInTheDocument()
  })

  it('mostra erro e permite tentar novamente quando o histórico falha', async () => {
    vi.mocked(api.listarImportacoes).mockRejectedValueOnce(new Error('API fora do ar'))

    renderizar()

    expect(await screen.findByText('API fora do ar')).toBeInTheDocument()

    vi.mocked(api.listarImportacoes).mockResolvedValue({
      total: 1,
      pagina: 1,
      itens: [HISTORICO[0]],
    })

    await userEvent.click(screen.getByRole('button', { name: /tentar novamente/i }))

    expect(await screen.findByText('admissoes-agosto.csv')).toBeInTheDocument()
  })

  // ------------------------------------------------------------ permissões

  it('o Auditor vê o histórico mas NÃO vê o envio', async () => {
    renderizar('Auditor')

    expect(await screen.findByText('admissoes-agosto.csv')).toBeInTheDocument()

    // Esconder o campo é conforto visual. Quem barra o Auditor é a política do
    // backend, e há teste de integração provando o 403.
    expect(screen.queryByLabelText('Arquivo')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /modelo csv/i })).not.toBeInTheDocument()
  })

  it('o Analista de RH vê o envio', async () => {
    renderizar('AnalistaRh')

    expect(await screen.findByLabelText('Arquivo')).toBeInTheDocument()
  })

  // ---------------------------------------------------------------- prévia

  it('escolher um arquivo mostra a prévia sem gravar nada', async () => {
    vi.mocked(api.previewFuncionarios).mockResolvedValue(PREVIA_BOA)

    renderizar()

    await userEvent.upload(await screen.findByLabelText('Arquivo'), arquivo())

    expect(await screen.findByText('Ana Paula')).toBeInTheDocument()
    expect(screen.getByText('111.***.**7-35')).toBeInTheDocument()

    // A prévia NÃO confirma: nada de gravação enquanto ninguém clicou.
    expect(api.confirmarFuncionarios).not.toHaveBeenCalled()
  })

  it('erros de linha aparecem um a um, e o botão de importar fica bloqueado', async () => {
    vi.mocked(api.previewFuncionarios).mockResolvedValue(PREVIA_RUIM)

    renderizar()

    await userEvent.upload(await screen.findByLabelText('Arquivo'), arquivo())

    expect(await screen.findByText('CPF invalido.')).toBeInTheDocument()
    expect(
      screen.getByText('Data de nascimento invalida. Use dd/mm/aaaa ou aaaa-mm-dd.'),
    ).toBeInTheDocument()

    const botao = screen.getByRole('button', { name: /importar/i })

    expect(botao).toBeDisabled()

    // Com o arquivo recusado, "Importar 0 funcionários" soaria como uma ação
    // que alguém poderia querer executar. O texto de um botão desabilitado
    // ainda é a explicação do que falta.
    expect(botao).toHaveTextContent('Importar')
    expect(botao).not.toHaveTextContent('0 funcionários')
  })

  it('o botão concorda em número com o que vai ser importado', async () => {
    vi.mocked(api.previewFuncionarios).mockResolvedValue(PREVIA_BOA)

    renderizar()

    await userEvent.upload(await screen.findByLabelText('Arquivo'), arquivo())

    expect(await screen.findByRole('button', { name: 'Importar 1 funcionário' })).toBeEnabled()
  })

  it('erro do arquivo inteiro é mostrado como alerta', async () => {
    vi.mocked(api.previewFuncionarios).mockResolvedValue({
      ...PREVIA_RUIM,
      linhas: [],
      errosDoArquivo: ["Linha 1: A coluna obrigatoria 'cpf' nao existe no arquivo."],
    })

    renderizar()

    await userEvent.upload(await screen.findByLabelText('Arquivo'), arquivo())

    expect(
      await screen.findByText("Linha 1: A coluna obrigatoria 'cpf' nao existe no arquivo."),
    ).toBeInTheDocument()
  })

  /**
   * A recusa do servidor vira mensagem, e não tela quebrada.
   *
   * O caso é o de um arquivo com extensão certa e conteúdo de outra coisa — que
   * o `accept` do campo não pega, porque `accept` filtra o seletor de arquivos
   * e nada mais. Quem decide é o backend, e há teste de integração provando a
   * recusa por conteúdo.
   */
  it('arquivo recusado pela API vira mensagem, e não tela quebrada', async () => {
    vi.mocked(api.previewFuncionarios).mockRejectedValue(
      new Error('O conteudo e uma planilha, mas o arquivo tem extensao .csv.'),
    )

    renderizar()

    await userEvent.upload(await screen.findByLabelText('Arquivo'), arquivo('planilha.csv'))

    expect(
      await screen.findByText('O conteudo e uma planilha, mas o arquivo tem extensao .csv.'),
    ).toBeInTheDocument()
  })

  // ------------------------------------------------------------ mapeamento

  it('trocar a coluna de um campo pede uma prévia nova com o mapeamento escolhido', async () => {
    vi.mocked(api.previewFuncionarios).mockResolvedValue({
      ...PREVIA_BOA,
      colunas: ['Nome Completo', 'Documento', 'Nascimento'],
      mapeamento: { nome: 'Nome Completo', cpf: 'Documento', dataNascimento: 'Nascimento' },
    })

    renderizar()

    await userEvent.upload(await screen.findByLabelText('Arquivo'), arquivo())

    const seletor = await screen.findByLabelText('CPF')

    await userEvent.selectOptions(seletor, 'Nascimento')

    await waitFor(() => {
      expect(api.previewFuncionarios).toHaveBeenCalledTimes(2)
    })

    // A segunda chamada leva o mapeamento; a primeira não levava nenhum.
    expect(vi.mocked(api.previewFuncionarios).mock.calls[1][1]).toEqual({
      nome: 'Nome Completo',
      cpf: 'Nascimento',
      dataNascimento: 'Nascimento',
    })
  })

  // ----------------------------------------------------------- confirmação

  /**
   * ⚠️ A afirmação central da tela.
   *
   * A confirmação reenvia o ARQUIVO. Não manda totais, nem linhas aprovadas,
   * nem o hash da prévia — porque o servidor relê e revalida tudo, e nada do
   * que esta tela concluiu é aproveitado por ele.
   */
  it('confirmar reenvia o arquivo, e nunca as conclusões da prévia', async () => {
    vi.mocked(api.previewFuncionarios).mockResolvedValue(PREVIA_BOA)

    const gravada: ConfirmacaoImportacao = {
      idImportacao: 'i9',
      status: 'Aplicada',
      formato: 'Csv',
      hashSha256: 'c'.repeat(64),
      total: 1,
      validas: 1,
      comErro: 0,
      funcionariosCriados: 1,
      errosDoArquivo: [],
      linhas: [],
    }

    vi.mocked(api.confirmarFuncionarios).mockResolvedValue(gravada)

    renderizar()

    const enviado = arquivo()

    await userEvent.upload(await screen.findByLabelText('Arquivo'), enviado)
    await userEvent.click(await screen.findByRole('button', { name: /importar/i }))

    expect(await screen.findByText('1 funcionário importado')).toBeInTheDocument()

    const [arquivoEnviado, mapeamentoEnviado] = vi.mocked(api.confirmarFuncionarios).mock.calls[0]

    expect(arquivoEnviado.name).toBe(enviado.name)
    expect(mapeamentoEnviado).toEqual(PREVIA_BOA.mapeamento)

    // O histórico é recarregado: uma vez ao abrir, outra depois de importar.
    await waitFor(() => {
      expect(api.listarImportacoes).toHaveBeenCalledTimes(2)
    })
  })

  it('confirmação recusada diz que ninguém foi criado', async () => {
    vi.mocked(api.previewFuncionarios).mockResolvedValue(PREVIA_BOA)
    vi.mocked(api.confirmarFuncionarios).mockResolvedValue({
      idImportacao: 'i9',
      status: 'Recusada',
      formato: 'Csv',
      hashSha256: 'c'.repeat(64),
      total: 1,
      validas: 0,
      comErro: 1,
      funcionariosCriados: 0,
      errosDoArquivo: ['Linha 2: CPF invalido.'],
      linhas: [],
    })

    renderizar()

    await userEvent.upload(await screen.findByLabelText('Arquivo'), arquivo())
    await userEvent.click(await screen.findByRole('button', { name: /importar/i }))

    expect(
      await screen.findByText('Importação recusada — nenhum funcionário foi criado'),
    ).toBeInTheDocument()
    expect(screen.getByText('Linha 2: CPF invalido.')).toBeInTheDocument()
  })

  // -------------------------------------------------------------- relatório

  it('abrir uma importação do histórico mostra o relatório linha a linha', async () => {
    const detalhe: ImportacaoDetalhe = {
      ...HISTORICO[1],
      linhas: [
        { numeroNoArquivo: 2, situacao: 'Valida', erros: [] },
        { numeroNoArquivo: 3, situacao: 'ComErro', erros: ['CPF invalido.'] },
      ],
    }

    vi.mocked(api.obterImportacao).mockResolvedValue(detalhe)

    renderizar()

    await userEvent.click(await screen.findByText('lote-errado.xlsx'))

    expect(await screen.findByText('Linhas com erro (1)')).toBeInTheDocument()
    expect(screen.getByText('Linha 3')).toBeInTheDocument()
    expect(screen.getByText('CPF invalido.')).toBeInTheDocument()

    // O hash identifica o arquivo sem que o conteúdo dele tenha sido guardado.
    expect(screen.getByText('b'.repeat(64))).toBeInTheDocument()
  })

  // ----------------------------------------------------------------- modelo

  it('baixar o modelo chama a API nos dois formatos', async () => {
    vi.mocked(api.baixarModelo).mockResolvedValue()

    renderizar()

    await userEvent.click(await screen.findByRole('button', { name: /modelo csv/i }))
    await userEvent.click(screen.getByRole('button', { name: /modelo xlsx/i }))

    await waitFor(() => {
      expect(api.baixarModelo).toHaveBeenCalledWith('csv')
      expect(api.baixarModelo).toHaveBeenCalledWith('xlsx')
    })
  })
})
