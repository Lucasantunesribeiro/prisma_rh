import { useCallback, useEffect, useState } from 'react'
import { consultarSaude, type RespostaSaude } from '@/api/saude'

export type EstadoStatusSistema =
  | { situacao: 'carregando' }
  | { situacao: 'sucesso'; saude: RespostaSaude }
  | { situacao: 'erro'; mensagem: string }

/**
 * Consulta o health check real da API. Nao existe dado simulado:
 * se a API nao responder, o estado vira 'erro'.
 */
export function useStatusSistema(): {
  estado: EstadoStatusSistema
  recarregar: () => void
} {
  const [estado, definirEstado] = useState<EstadoStatusSistema>({ situacao: 'carregando' })
  const [tentativa, definirTentativa] = useState(0)

  const recarregar = useCallback(() => {
    definirEstado({ situacao: 'carregando' })
    definirTentativa((atual) => atual + 1)
  }, [])

  useEffect(() => {
    const controlador = new AbortController()

    consultarSaude(controlador.signal)
      .then((saude) => definirEstado({ situacao: 'sucesso', saude }))
      .catch((erro: unknown) => {
        if (controlador.signal.aborted) {
          return
        }

        definirEstado({
          situacao: 'erro',
          mensagem: erro instanceof Error ? erro.message : 'Falha desconhecida ao consultar a API.',
        })
      })

    return () => controlador.abort()
  }, [tentativa])

  return { estado, recarregar }
}
