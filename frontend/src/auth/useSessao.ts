import { useContext } from 'react'

// O prefixo 'use' e exigido pelas rules-of-hooks do React; e nome de framework,
// nao de dominio (CLAUDE.md secao 19).
import { SessaoContexto, type Sessao } from './contexto'

export function useSessao(): Sessao {
  const sessao = useContext(SessaoContexto)

  if (!sessao) {
    throw new Error('useSessao precisa estar dentro de <ProvedorSessao>.')
  }

  return sessao
}
