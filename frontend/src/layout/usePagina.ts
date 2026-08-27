import { use, useEffect } from 'react'
import { PaginaContexto, type Trilha } from './contexto'

/**
 * Publica breadcrumb e competência no shell.
 *
 * As dependências são as partes do conteúdo, e não o array `trilha`: um array
 * literal é recriado a cada render, e usá-lo como dependência colocaria o
 * efeito num laço infinito de atualização de estado.
 */
export function usePagina(trilha: Trilha[], competencia?: string | null) {
  const contexto = use(PaginaContexto)
  const definir = contexto?.definir
  const assinatura = trilha.map((t) => `${t.texto}|${t.para ?? ''}`).join('>')

  useEffect(() => {
    definir?.({
      trilha: assinatura
        .split('>')
        .filter(Boolean)
        .map((parte) => {
          const [texto, para] = parte.split('|')
          return para ? { texto, para } : { texto }
        }),
      competencia: competencia ?? null,
    })
  }, [definir, assinatura, competencia])
}
