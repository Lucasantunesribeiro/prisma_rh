import { createContext, use } from 'react'
import type { Empresa } from '@/api/empresas'

/**
 * A empresa em que o usuário está trabalhando.
 *
 * É preferência DE INTERFACE, não conceito novo de domínio. O backend continua
 * derivando a organização do token, e nenhuma consulta passa a confiar neste
 * valor: ele serve para pré-filtrar a lista de folhas e para sugerir a empresa
 * ao abrir uma competência nova.
 *
 * Existe porque o contrário é pior: sem indicação nenhuma, o analista processa
 * folha sem saber de qual empresa está olhando.
 */
export interface ContextoEmpresa {
  empresas: Empresa[]
  empresaAtual: Empresa | null
  selecionar: (id: string) => void
  carregando: boolean
}

export const EmpresaContexto = createContext<ContextoEmpresa | null>(null)

export function useEmpresaAtual(): ContextoEmpresa {
  const contexto = use(EmpresaContexto)

  if (!contexto) {
    throw new Error('useEmpresaAtual precisa estar dentro do ApplicationShell.')
  }

  return contexto
}

/**
 * Contexto que a página publica para o topo do shell.
 *
 * O breadcrumb e a competência vivem aqui porque só a página sabe deles: o
 * shell não tem como adivinhar o nome do funcionário aberto nem a competência
 * da folha carregada. Publicar em vez de adivinhar evita a alternativa comum,
 * que é o shell refazer a requisição só para montar um título.
 */
export interface Trilha {
  texto: string
  para?: string
}

export interface ContextoPagina {
  trilha: Trilha[]
  competencia: string | null
  definir: (dados: { trilha?: Trilha[]; competencia?: string | null }) => void
}

export const PaginaContexto = createContext<ContextoPagina | null>(null)
