import { enviarArquivo, obter, obterArquivo } from './cliente'

export type FormatoImportacao = 'Csv' | 'Xlsx'
export type StatusImportacao = 'Analisada' | 'Aplicada' | 'Recusada'
export type SituacaoLinha = 'Valida' | 'ComErro'

/** De qual coluna do arquivo sai cada campo. */
export interface MapeamentoFuncionarios {
  nome: string
  cpf: string
  dataNascimento: string
}

export interface LinhaPreview {
  linha: number
  nome: string | null
  /** Já vem mascarado da API: `111.***.**7-35`. O CPF inteiro nunca chega aqui. */
  cpf: string | null
  dataNascimento: string | null
  erros: string[]
}

export interface PreviewImportacao {
  nomeArquivo: string
  formato: FormatoImportacao
  tamanhoBytes: number
  hashSha256: string
  /** O cabeçalho que o servidor leu do arquivo. É a lista de onde o mapeamento escolhe. */
  colunas: string[]
  mapeamento: MapeamentoFuncionarios
  total: number
  validas: number
  comErro: number
  importavel: boolean
  errosDoArquivo: string[]
  linhas: LinhaPreview[]
}

export interface ConfirmacaoImportacao {
  idImportacao: string
  status: StatusImportacao
  formato: FormatoImportacao
  hashSha256: string
  total: number
  validas: number
  comErro: number
  funcionariosCriados: number
  errosDoArquivo: string[]
  linhas: LinhaPreview[]
}

export interface ImportacaoResumo {
  id: string
  nomeOriginalArquivo: string
  formato: FormatoImportacao
  tamanhoBytes: number
  hashSha256: string
  enviadaEm: string
  status: StatusImportacao
  totalLinhas: number
  linhasValidas: number
  linhasComErro: number
}

export interface LinhaRelatorio {
  numeroNoArquivo: number
  situacao: SituacaoLinha
  erros: string[]
}

export interface ImportacaoDetalhe extends ImportacaoResumo {
  linhas: LinhaRelatorio[]
}

export interface PaginaImportacoes {
  total: number
  pagina: number
  itens: ImportacaoResumo[]
}

const BASE = '/api/importacoes'

export const EXTENSOES_ACEITAS = '.csv,.xlsx'

export const ROTULO_FORMATO: Record<FormatoImportacao, string> = {
  Csv: 'CSV',
  Xlsx: 'XLSX',
}

export const ROTULO_STATUS: Record<StatusImportacao, string> = {
  Analisada: 'Analisada',
  Aplicada: 'Aplicada',
  Recusada: 'Recusada',
}

/**
 * Monta o corpo do envio.
 *
 * ⚠️ Vai o ARQUIVO e o MAPEAMENTO, e mais nada.
 *
 * Nenhum total, nenhuma lista de linhas válidas, nenhum id de prévia. O
 * servidor não guarda o arquivo entre a prévia e a confirmação, então ele relê
 * e revalida tudo — e é justamente por isso que nada do que esta tela calculou
 * precisa (nem pode) ser enviado como verdade.
 */
function corpo(arquivo: File, mapeamento?: MapeamentoFuncionarios): FormData {
  const formulario = new FormData()

  formulario.append('arquivo', arquivo, arquivo.name)

  if (mapeamento) {
    formulario.append('colunaNome', mapeamento.nome)
    formulario.append('colunaCpf', mapeamento.cpf)
    formulario.append('colunaDataNascimento', mapeamento.dataNascimento)
  }

  return formulario
}

/** Lê e valida sem gravar nada. */
export async function previewFuncionarios(
  arquivo: File,
  mapeamento?: MapeamentoFuncionarios,
): Promise<PreviewImportacao> {
  return enviarArquivo<PreviewImportacao>(
    `${BASE}/funcionarios/preview`,
    corpo(arquivo, mapeamento),
  )
}

/** Reenvia o arquivo para que o servidor releia, revalide e só então grave. */
export async function confirmarFuncionarios(
  arquivo: File,
  mapeamento?: MapeamentoFuncionarios,
): Promise<ConfirmacaoImportacao> {
  return enviarArquivo<ConfirmacaoImportacao>(
    `${BASE}/funcionarios/confirmar`,
    corpo(arquivo, mapeamento),
  )
}

export async function listarImportacoes(pagina = 1, tamanho = 25): Promise<PaginaImportacoes> {
  return obter<PaginaImportacoes>(`${BASE}?pagina=${pagina}&tamanho=${tamanho}`)
}

export async function obterImportacao(id: string): Promise<ImportacaoDetalhe> {
  return obter<ImportacaoDetalhe>(`${BASE}/${id}`)
}

/**
 * Baixa o modelo e entrega ao navegador.
 *
 * O caminho indireto — buscar por fetch e criar um blob — existe porque o
 * access token vive só em memória: um link direto para a API sairia sem o
 * cabeçalho `Authorization` e voltaria 401.
 *
 * A URL temporária é revogada logo em seguida. Sem isso, cada download deixaria
 * o arquivo inteiro preso na memória da aba até o F5.
 */
export async function baixarModelo(formato: 'csv' | 'xlsx'): Promise<void> {
  const blob = await obterArquivo(`${BASE}/funcionarios/modelo?formato=${formato}`)
  const url = URL.createObjectURL(blob)

  try {
    const ancora = document.createElement('a')

    ancora.href = url
    ancora.download = `modelo-funcionarios.${formato}`
    document.body.appendChild(ancora)
    ancora.click()
    ancora.remove()
  } finally {
    URL.revokeObjectURL(url)
  }
}

// ------------------------------------------------- processamento assincrono (Fase 9)

export type StatusTrabalho = 'Enfileirado' | 'Processando' | 'Concluido' | 'Falhou'

export interface TrabalhoAssincrono {
  id: string
  tipo: string
  status: StatusTrabalho
  /** Ainda vai acontecer alguma coisa? É o que decide se a tela continua perguntando. */
  pendente: boolean
  tentativas: number
  /** A `Importacao` gerada, quando concluiu. */
  idRecurso: string | null
  erro: string | null
  criadoEm: string
  concluidoEm: string | null
}

export const ROTULO_TRABALHO: Record<StatusTrabalho, string> = {
  Enfileirado: 'Na fila',
  Processando: 'Processando',
  Concluido: 'Concluído',
  Falhou: 'Falhou',
}

/**
 * Envia a planilha para processamento em segundo plano.
 *
 * Responde **202** com o trabalho ainda `Enfileirado` — o arquivo foi aceito e
 * guardado, e o processamento acontece fora da requisição. Quem chama passa a
 * acompanhar por `obterTrabalho`.
 *
 * Devolve **507** quando o armazenamento temporário compartilhado está cheio.
 * O limite é do sistema inteiro, e não da organização: outra empresa pode estar
 * ocupando o espaço, e a mensagem diz isso em vez de sugerir falta de permissão.
 */
export const enfileirarImportacao = (arquivo: File): Promise<TrabalhoAssincrono> => {
  const formulario = new FormData()
  formulario.append('arquivo', arquivo)

  return enviarArquivo('/api/importacoes/funcionarios/assincrona', formulario)
}

export const obterTrabalho = (id: string): Promise<TrabalhoAssincrono> =>
  obter(`/api/trabalhos/${id}`)

export const listarTrabalhos = (): Promise<{
  total: number
  pagina: number
  itens: TrabalhoAssincrono[]
}> => obter('/api/trabalhos?tamanho=20')
