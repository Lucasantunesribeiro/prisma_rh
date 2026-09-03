import { afterEach, describe, expect, it, vi } from 'vitest'
import { definirAccessToken } from './cliente'
import { baixarModelo, confirmarFuncionarios, previewFuncionarios } from './importacoes'

function arquivo(nome = 'funcionarios.csv'): File {
  return new File(['nome;cpf;data de nascimento\n'], nome, { type: 'text/csv' })
}

function respostaJson(corpo: unknown): Response {
  return new Response(JSON.stringify(corpo), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}

/** Devolve o fetch simulado e o corpo que ele recebeu. */
function interceptar(corpo: unknown = {}) {
  const simulado = vi.fn().mockResolvedValue(respostaJson(corpo))

  vi.stubGlobal('fetch', simulado)

  return simulado
}

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
  definirAccessToken(null)
})

describe('api de importações', () => {
  /**
   * ⚠️ O teste que sustenta o modelo de confiança da Fase 5.
   *
   * O servidor não guarda o arquivo entre a prévia e a confirmação, então ele
   * relê e revalida tudo. Para isso funcionar, a tela não pode mandar nenhuma
   * conclusão junto — e este teste prova que ela não manda.
   */
  it('a confirmação envia SÓ o arquivo e o mapeamento, e nenhuma conclusão da tela', async () => {
    const simulado = interceptar({ status: 'Aplicada' })

    await confirmarFuncionarios(arquivo(), {
      nome: 'Nome Completo',
      cpf: 'Documento',
      dataNascimento: 'Nascimento',
    })

    const corpo = simulado.mock.calls[0][1].body as FormData
    const campos = [...corpo.keys()].sort()

    expect(campos).toEqual(['arquivo', 'colunaCpf', 'colunaDataNascimento', 'colunaNome'])

    // Nada de totais, linhas aprovadas, "importável" ou hash: se qualquer um
    // desses viajasse, o cliente estaria opinando sobre a validação.
    expect(campos).not.toContain('importavel')
    expect(campos).not.toContain('validas')
    expect(campos).not.toContain('linhas')
    expect(campos).not.toContain('hashSha256')
    expect(campos).not.toContain('idPreview')
  })

  it('sem mapeamento escolhido, só o arquivo viaja', async () => {
    const simulado = interceptar({})

    await previewFuncionarios(arquivo())

    const corpo = simulado.mock.calls[0][1].body as FormData

    expect([...corpo.keys()]).toEqual(['arquivo'])
  })

  /**
   * FormData define o próprio Content-Type, com a fronteira do multipart.
   * Escrever 'application/json' por cima quebraria o envio de um jeito
   * silencioso — o servidor receberia um corpo que não consegue separar.
   */
  it('o envio de arquivo NÃO força Content-Type de JSON', async () => {
    const simulado = interceptar({})

    await previewFuncionarios(arquivo())

    const cabecalhos = simulado.mock.calls[0][1].headers as Record<string, string>

    expect(cabecalhos['Content-Type']).toBeUndefined()
  })

  it('o envio leva o access token, que vive só em memória', async () => {
    const simulado = interceptar({})

    definirAccessToken('token-de-teste')
    await previewFuncionarios(arquivo())

    const cabecalhos = simulado.mock.calls[0][1].headers as Record<string, string>

    expect(cabecalhos.Authorization).toBe('Bearer token-de-teste')
  })

  it('a prévia devolve o que o servidor disse, sem recalcular nada', async () => {
    interceptar({
      nomeArquivo: 'f.csv',
      formato: 'Csv',
      total: 3,
      validas: 1,
      comErro: 2,
      importavel: false,
      colunas: ['nome', 'cpf', 'data de nascimento'],
      mapeamento: { nome: 'nome', cpf: 'cpf', dataNascimento: 'data de nascimento' },
      errosDoArquivo: [],
      linhas: [],
    })

    const previa = await previewFuncionarios(arquivo())

    expect(previa.importavel).toBe(false)
    expect(previa.comErro).toBe(2)
  })

  it('erro da API vira mensagem legível', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        new Response(JSON.stringify({ detalhe: 'Formatos aceitos: .csv e .xlsx.' }), {
          status: 400,
          headers: { 'Content-Type': 'application/json' },
        }),
      ),
    )

    await expect(previewFuncionarios(arquivo('virus.exe'))).rejects.toThrow(
      'Formatos aceitos: .csv e .xlsx.',
    )
  })

  /**
   * O modelo é baixado por fetch, e não por link direto.
   *
   * O access token vive só em memória: um `<a href>` apontando para a API
   * sairia sem o cabeçalho `Authorization` e voltaria 401.
   */
  it('baixar o modelo passa pelo fetch autenticado e revoga a URL temporária', async () => {
    const simulado = vi.fn().mockResolvedValue(
      new Response('nome;cpf', {
        status: 200,
        headers: { 'Content-Type': 'text/csv' },
      }),
    )

    vi.stubGlobal('fetch', simulado)

    const criar = vi.fn().mockReturnValue('blob:teste')
    const revogar = vi.fn()

    vi.stubGlobal('URL', { ...URL, createObjectURL: criar, revokeObjectURL: revogar })

    // O download dispara `ancora.click()` num link real. O jsdom nao implementa
    // navegacao e imprimiria "Not implemented: navigation to another Document"
    // no log do CI. Interceptar o clique remove o ruido e, de quebra, prova que
    // o download foi realmente acionado — sem mudar o comportamento de producao.
    const clicar = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})

    await baixarModelo('xlsx')

    expect(simulado.mock.calls[0][0]).toContain('/api/importacoes/funcionarios/modelo?formato=xlsx')
    expect(criar).toHaveBeenCalledTimes(1)
    expect(clicar).toHaveBeenCalledTimes(1)

    // Sem revogar, cada download deixaria o arquivo inteiro preso na memória
    // da aba até o F5.
    expect(revogar).toHaveBeenCalledWith('blob:teste')
  })
})
