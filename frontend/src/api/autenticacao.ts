import { URL_BASE_API, cabecalhosCsrf, definirAccessToken, enviar } from './cliente'

export type Perfil =
  | 'AdministradorPlataforma'
  | 'AdministradorEmpresa'
  | 'AnalistaRh'
  | 'Auditor'
  | 'Visualizador'

export interface UsuarioAutenticado {
  id: string
  idOrganizacao: string
  perfil: Perfil

  /**
   * ⚠️ OPCIONAIS PORQUE A API NEM SEMPRE OS ENVIA.
   *
   * POST /api/autenticacao/entrar devolve o usuario completo, mas
   * GET /api/autenticacao/eu - usado ao restaurar a sessao num F5 - devolve
   * apenas id, idOrganizacao e perfil. Depois de recarregar a pagina, nome e
   * e-mail somem.
   *
   * Tipar como obrigatorio era mentira do tipo: o codigo antigo renderizava
   * `undefined` em silencio e ninguem via. Marcar como opcional obriga cada
   * uso a decidir o que mostrar quando falta.
   *
   * A correcao definitiva e no backend, e depende de decisao do responsavel.
   */
  nome?: string
  email?: string
}

interface SessaoResposta {
  accessToken: string
  expiraEm: string
  usuario: UsuarioAutenticado
}

export const ROTULO_PERFIL: Record<Perfil, string> = {
  AdministradorPlataforma: 'Administrador da Plataforma',
  AdministradorEmpresa: 'Administrador da Empresa',
  AnalistaRh: 'Analista de RH',
  Auditor: 'Auditor',
  Visualizador: 'Visualizador',
}

/** Perfis que podem criar, alterar e inativar empresas e estabelecimentos. */
const PERFIS_ADMINISTRADORES: Perfil[] = ['AdministradorPlataforma', 'AdministradorEmpresa']

/** Perfis que mantem cadastro de gente: funcionarios, contratos e cargos. */
const PERFIS_PESSOAS: Perfil[] = [...PERFIS_ADMINISTRADORES, 'AnalistaRh']

/**
 * Usado apenas para adaptar a interface. A autoridade e o backend: esconder o
 * botao nao impede ninguem de chamar a API direto.
 */
export function podeAdministrar(perfil: Perfil | undefined): boolean {
  return perfil !== undefined && PERFIS_ADMINISTRADORES.includes(perfil)
}

/**
 * O Analista de RH mantem cadastros mas NAO administra empresas - por isso a
 * permissao de pessoas e separada da de empresas (CLAUDE.md secao 6).
 */
export function podeAdministrarPessoas(perfil: Perfil | undefined): boolean {
  return perfil !== undefined && PERFIS_PESSOAS.includes(perfil)
}

export async function entrar(email: string, senha: string): Promise<UsuarioAutenticado> {
  const sessao = await enviar<SessaoResposta>('/api/autenticacao/entrar', { email, senha })
  definirAccessToken(sessao.accessToken)
  return sessao.usuario
}

export async function sair(): Promise<void> {
  await fetch(`${URL_BASE_API}/api/autenticacao/sair`, {
    method: 'POST',
    credentials: 'include',
    // `sair` também depende do cookie, e por isso também precisa do token.
    // Sem ele, o servidor recusa com 403 e a sessão ficaria viva no banco.
    headers: cabecalhosCsrf(),
  })
  definirAccessToken(null)
}
