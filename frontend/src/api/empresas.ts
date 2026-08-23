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

export const listarEmpresas = (): Promise<PaginaEmpresas> => obter('/api/empresas')

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
