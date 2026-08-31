import { enviar, obter, remover } from './cliente'

export interface Empresa {
  id: string
  razaoSocial: string
  nomeFantasia: string | null
  cnpj: string
  cnpjFormatado: string
  ativa: boolean
  criadaEm: string
}

export interface PaginaEmpresas {
  total: number
  pagina: number
  tamanho: number
  itens: Empresa[]
}

export interface Estabelecimento {
  id: string
  idEmpresa: string
  codigo: string
  nome: string
  ativo: boolean
  criadoEm: string
}

/**
 * Como a consulta terminou. Espelha o `enum` do servidor — vocabulário fechado
 * dos dois lados, e não texto livre que a tela tenta adivinhar.
 */
export type SituacaoConsulta = 'Encontrada' | 'NaoEncontrada' | 'Recusada' | 'Indisponivel'

export interface DadosDaReceita {
  razaoSocial: string
  nomeFantasia: string | null
  situacaoCadastral: string
  ativaNaReceita: boolean
}

export interface ConsultaCnpj {
  situacao: SituacaoConsulta
  mensagem: string
  dados: DadosDaReceita | null
  /** Já existe **nesta** organização. Nunca "existe em alguma". */
  jaCadastrada: boolean
}

export const listarEmpresas = (): Promise<PaginaEmpresas> => obter('/api/empresas')

/**
 * Busca razão social e nome fantasia na Receita Federal, pela BrasilAPI.
 *
 * `POST` para uma leitura, de propósito: o CNPJ não entra na URL — que vai para
 * log de acesso e histórico de navegador — e a chamada tem efeito, porque sai
 * da nossa rede e consome cota de um serviço de terceiro.
 *
 * O resultado **não** vira cadastro sozinho: volta para a tela e a pessoa
 * decide. Com a BrasilAPI fora do ar, o formulário manual continua igual.
 */
export const consultarCnpj = (cnpj: string): Promise<ConsultaCnpj> =>
  enviar('/api/integracoes/cnpj/consultas', { cnpj })

export const criarEmpresa = (dados: {
  razaoSocial: string
  cnpj: string
  nomeFantasia?: string | null
}): Promise<Empresa> => enviar('/api/empresas', dados)

export const inativarEmpresa = (id: string): Promise<void> => remover(`/api/empresas/${id}`)

export const listarEstabelecimentos = (idEmpresa: string): Promise<Estabelecimento[]> =>
  obter(`/api/empresas/${idEmpresa}/estabelecimentos`)

export const criarEstabelecimento = (
  idEmpresa: string,
  dados: { codigo: string; nome: string },
): Promise<Estabelecimento> => enviar(`/api/empresas/${idEmpresa}/estabelecimentos`, dados)
