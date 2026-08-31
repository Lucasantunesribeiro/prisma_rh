import { useCallback, useEffect, useRef, useState } from 'react'
import { obterTrabalho, type TrabalhoAssincrono } from '@/api/importacoes'

/**
 * Intervalo entre perguntas de status.
 *
 * Três segundos: rápido o bastante para a tela parecer viva, devagar o bastante
 * para uma aba esquecida aberta não virar mil requisições por hora. Uma
 * importação de porte real leva alguns segundos, então quase sempre a resposta
 * chega na primeira ou segunda pergunta.
 */
export const INTERVALO_MS = 3000

/**
 * Teto de tentativas — **cinco minutos**.
 *
 * Sem ele, um trabalho que nunca conclui deixa a aba perguntando para sempre.
 * A `Lambda` tem timeout de 60 s e no máximo 3 tentativas, então cinco minutos
 * cobrem o pior caso com folga; passar disso significa que algo ficou preso, e
 * a tela precisa dizer isso em vez de girar eternamente.
 */
export const MAXIMO_TENTATIVAS = 100

export interface AcompanhamentoTrabalho {
  trabalho: TrabalhoAssincrono | null
  acompanhando: boolean
  erro: string | null
  /** Desistiu de esperar. Diferente de falhar: aqui ninguém sabe o desfecho. */
  desistiu: boolean
}

/**
 * Acompanha um trabalho assíncrono até ele deixar de estar pendente.
 *
 * ## Por que polling, e não WebSocket
 *
 * `CLAUDE.md §36` lista WebSockets como tecnologia fora do escopo inicial, e
 * aqui ela não se justifica: a pergunta é feita por poucos segundos, por uma
 * pessoa que acabou de clicar em algo. Uma conexão persistente para isso
 * custaria infraestrutura e complexidade para economizar meia dúzia de
 * requisições.
 *
 * ## Para sozinho, de três formas
 *
 * 1. quando o trabalho deixa de estar `pendente` — o caso normal;
 * 2. quando estoura o teto de tentativas — o trabalho travou;
 * 3. quando o componente sai da tela — sem isso, sair da página deixaria o
 *    laço rodando contra um componente que não existe mais.
 */
export function useTrabalho(id: string | null): AcompanhamentoTrabalho {
  const [trabalho, definirTrabalho] = useState<TrabalhoAssincrono | null>(null)
  const [erro, definirErro] = useState<string | null>(null)
  const [desistiu, definirDesistiu] = useState(false)
  const [acompanhando, definirAcompanhando] = useState(false)

  // `ref` e não estado: mudar a contagem não deve redesenhar a tela, e um
  // estado aqui reiniciaria o efeito a cada tentativa.
  const tentativas = useRef(0)

  const reiniciar = useCallback(() => {
    tentativas.current = 0
    definirTrabalho(null)
    definirErro(null)
    definirDesistiu(false)
  }, [])

  useEffect(() => {
    // Zerar ao trocar de trabalho e sincronizacao com sistema externo - o
    // estado anterior descreve OUTRO trabalho, e mante-lo faria a tela mostrar
    // o status do anterior enquanto o novo ainda nao respondeu.
    if (!id) {
      // oxlint-disable-next-line react/set-state-in-effect
      reiniciar()
      definirAcompanhando(false)
      return
    }

    // oxlint-disable-next-line react/set-state-in-effect
    reiniciar()
    definirAcompanhando(true)

    let vivo = true
    let temporizador: ReturnType<typeof setTimeout> | undefined

    const perguntar = async () => {
      if (!vivo) return

      try {
        const atual = await obterTrabalho(id)

        if (!vivo) return

        definirTrabalho(atual)

        if (!atual.pendente) {
          definirAcompanhando(false)
          return
        }

        tentativas.current += 1

        if (tentativas.current >= MAXIMO_TENTATIVAS) {
          definirDesistiu(true)
          definirAcompanhando(false)
          return
        }

        temporizador = setTimeout(() => void perguntar(), INTERVALO_MS)
      } catch (falha) {
        if (!vivo) return

        // Falha de rede não encerra o acompanhamento na primeira: a API pode
        // ter piscado. O erro fica visível e a próxima pergunta acontece.
        definirErro(falha instanceof Error ? falha.message : 'Não foi possível consultar o status.')

        tentativas.current += 1

        if (tentativas.current >= MAXIMO_TENTATIVAS) {
          definirDesistiu(true)
          definirAcompanhando(false)
          return
        }

        temporizador = setTimeout(() => void perguntar(), INTERVALO_MS)
      }
    }

    void perguntar()

    return () => {
      // Sair da tela encerra o laço. Sem isto, navegar para outra página
      // deixaria o polling rodando contra um componente desmontado.
      vivo = false
      if (temporizador) clearTimeout(temporizador)
    }
  }, [id, reiniciar])

  return { trabalho, acompanhando, erro, desistiu }
}
