import {
  URL_BASE_API,
  cabecalhosCsrf,
  definirAccessToken,
  enviar,
  guardarTokenCsrf,
} from './cliente'

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
   * ⚠️ Foram OPCIONAIS ate 02/09/2026, e nao sao mais.
   *
   * `POST /entrar` devolvia o usuario completo, mas `GET /eu` - usado ao
   * restaurar a sessao num F5 - devolvia so id, organizacao e perfil. Depois de
   * recarregar, o nome sumia da barra lateral.
   *
   * O tipo estava certo em admitir a ausencia; o **contrato da API** e que
   * estava errado, com duas respostas diferentes para a mesma pergunta. `eu`
   * passou a devolver os mesmos campos, e ha teste comparando as duas respostas
   * campo a campo.
   */
  nome: string
  email: string
}

interface SessaoResposta {
  accessToken: string
  expiraEm: string
  usuario: UsuarioAutenticado

  /** O par do *double submit*. Ver `guardarTokenCsrf` em `cliente.ts`. */
  tokenCsrf: string
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

  // ⚠️ O par do double submit chega no CORPO, e nao pelo cookie.
  //
  // Em producao a tela esta na Vercel e a API na Lambda - dominios diferentes -
  // e `document.cookie` e por origem, entao a tela nunca enxergou aquele
  // cookie. Ver `CHAVE_CSRF` em `cliente.ts`.
  guardarTokenCsrf(sessao.tokenCsrf)

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
  // Sair apaga os DOIS lados: o access token da memoria e o par do double
  // submit desta origem. Deixar o token para tras faria a proxima visita achar
  // que ha sessao a restaurar e bater numa porta ja fechada.
  guardarTokenCsrf(null)
  definirAccessToken(null)
}
