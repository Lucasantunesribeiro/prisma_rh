import { createContext } from 'react'
import type { UsuarioAutenticado } from '@/api/autenticacao'

export interface Sessao {
  usuario: UsuarioAutenticado | null
  carregando: boolean
  entrar: (email: string, senha: string) => Promise<void>
  sair: () => Promise<void>
}

/**
 * Vive separado do provedor de proposito: um modulo que exporta componente E
 * valor quebra o Fast Refresh do Vite, e a tela deixa de recarregar sozinha
 * durante o desenvolvimento.
 */
export const SessaoContexto = createContext<Sessao | null>(null)
