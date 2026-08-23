import { URL_BASE_API, definirAccessToken, enviar } from './cliente'

export type Perfil =
  | 'AdministradorPlataforma'
  | 'AdministradorEmpresa'
  | 'AnalistaRh'
  | 'Auditor'
  | 'Visualizador'

export interface UsuarioAutenticado {
  id: string
  idOrganizacao: string
  nome: string
  email: string
  perfil: Perfil
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
  })
  definirAccessToken(null)
}
