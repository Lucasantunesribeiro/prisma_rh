/**
 * Contrato do endpoint GET /health exposto pela API do Prisma RH.
 * A API responde 200 quando tudo esta saudavel e 503 quando alguma verificacao
 * falha; nos dois casos o corpo traz o mesmo JSON.
 */
export type StatusSaude = 'saudavel' | 'degradado' | 'indisponivel'

export interface VerificacaoSaude {
  nome: string
  status: StatusSaude
  descricao: string | null
}

export interface RespostaSaude {
  status: StatusSaude
  verificacoes: VerificacaoSaude[]
}

/** Nome da verificacao de banco devolvida pela API (ConfiguracaoInfraestrutura.NomeVerificacaoBanco). */
export const VERIFICACAO_BANCO = 'banco-de-dados'

export const URL_BASE_API = import.meta.env.VITE_API_URL ?? 'http://localhost:5080'

export async function consultarSaude(sinal?: AbortSignal): Promise<RespostaSaude> {
  const resposta = await fetch(`${URL_BASE_API}/health`, {
    signal: sinal,
    headers: { Accept: 'application/json' },
  })

  return (await resposta.json()) as RespostaSaude
}
