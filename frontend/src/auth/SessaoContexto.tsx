import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { obter, registrarPerdaDeSessao, renovarSessao } from '@/api/cliente'
import { entrar as entrarNaApi, sair as sairDaApi, type UsuarioAutenticado } from '@/api/autenticacao'
import { SessaoContexto, type Sessao } from './contexto'

export function ProvedorSessao({ children }: { children: ReactNode }) {
  const [usuario, definirUsuario] = useState<UsuarioAutenticado | null>(null)
  const [carregando, definirCarregando] = useState(true)

  // Ao abrir a aplicacao, tenta restaurar a sessao pelo cookie httpOnly. Sem
  // isto, recarregar a pagina jogaria o usuario para o login mesmo com sessao
  // valida - o access token so existe em memoria e some no F5.
  useEffect(() => {
    let ativo = true

    const restaurar = async () => {
      try {
        const renovou = await renovarSessao()

        if (renovou && ativo) {
          definirUsuario(await obter<UsuarioAutenticado>('/api/autenticacao/eu'))
        }
      } catch {
        // Sem sessao valida: segue deslogado, que e o estado correto.
      } finally {
        if (ativo) {
          definirCarregando(false)
        }
      }
    }

    void restaurar()

    return () => {
      ativo = false
    }
  }, [])

  // O cliente HTTP avisa quando a renovacao falhou no meio de uma chamada.
  useEffect(() => {
    registrarPerdaDeSessao(() => definirUsuario(null))
    return () => registrarPerdaDeSessao(null)
  }, [])

  const entrar = useCallback(async (email: string, senha: string) => {
    definirUsuario(await entrarNaApi(email, senha))
  }, [])

  const sair = useCallback(async () => {
    await sairDaApi()
    definirUsuario(null)
  }, [])

  const valor = useMemo<Sessao>(
    () => ({ usuario, carregando, entrar, sair }),
    [usuario, carregando, entrar, sair],
  )

  return <SessaoContexto.Provider value={valor}>{children}</SessaoContexto.Provider>
}
