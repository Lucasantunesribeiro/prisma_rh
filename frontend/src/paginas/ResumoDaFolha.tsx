import { Sparkles } from 'lucide-react'
import { useEffect, useState } from 'react'
import {
  assistenteDisponivel,
  resumirFolha,
  type ResumoExecutivo,
} from '@/api/assistente'
import { Dinheiro } from '@/components/sistema/Primitivos'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'

const MOTIVO: Record<string, string> = {
  NaoConfigurada: 'O assistente não está configurado neste ambiente.',
  LimiteAtingido: 'Limite de resumos atingido. Tente de novo mais tarde.',
  Recusada: 'O assistente não conseguiu resumir esta folha.',
  Indisponivel: 'O assistente está indisponível no momento.',
}

/**
 * O resumo executivo da folha (Fase 11B).
 *
 * ## Duas metades, com origens diferentes — e a tela deixa isso claro
 *
 * ```text
 * números  ← consulta determinística no backend
 * prosa    ← modelo de linguagem, rotulada
 * ```
 *
 * `ROADMAP.md` da 11B: *"nunca é a fonte de um número"*. Por isso o retrato
 * numérico aparece **ao lado** do parágrafo, e não dentro dele: se o modelo
 * escrever "sete inconsistências" onde há seis, a divergência fica visível na
 * mesma tela em vez de virar um número que ninguém confere.
 *
 * ## Com a IA fora do ar, os números continuam
 *
 * A API devolve o retrato mesmo quando o provedor falha. A caixa perde o
 * parágrafo e mantém o resumo — a IA é acessório (`CLAUDE.md §1`).
 */
export function ResumoDaFolha({ idFolha }: { idFolha: string }) {
  const [disponivel, definirDisponivel] = useState<boolean | null>(null)
  const [resumo, definirResumo] = useState<ResumoExecutivo | null>(null)
  const [pedindo, definirPedindo] = useState(false)
  const [falha, definirFalha] = useState<string | null>(null)

  useEffect(() => {
    let vivo = true

    assistenteDisponivel()
      .then((r) => vivo && definirDisponivel(r))
      .catch(() => vivo && definirDisponivel(false))

    return () => {
      vivo = false
    }
  }, [idFolha])

  if (disponivel !== true) return null

  const pedir = async () => {
    definirPedindo(true)
    definirFalha(null)

    try {
      definirResumo(await resumirFolha(idFolha))
    } catch {
      definirFalha('Não foi possível falar com o assistente.')
    } finally {
      definirPedindo(false)
    }
  }

  const retrato = resumo?.retrato

  return (
    <section aria-labelledby="resumo-ia" className="rounded-lg border border-border bg-card p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2
          id="resumo-ia"
          className="flex items-center gap-1.5 text-[15px] font-semibold tracking-tight"
        >
          <Sparkles aria-hidden className="size-4 text-muted-foreground" />
          Resumo executivo
        </h2>

        {!resumo && (
          <Button size="sm" variant="outline" onClick={pedir} disabled={pedindo}>
            {pedindo ? 'Resumindo…' : 'Gerar resumo'}
          </Button>
        )}
      </div>

      {falha && (
        <Alert variant="destructive" role="alert" className="mt-3">
          <AlertDescription>{falha}</AlertDescription>
        </Alert>
      )}

      {retrato && (
        <>
          {/* ⚠️ Os números primeiro, e vindos do C#. */}
          <dl className="mt-3 grid grid-cols-2 gap-x-6 gap-y-2 sm:grid-cols-4">
            <Numero rotulo="Holerites" valor={String(retrato.holerites)} />
            <Numero rotulo="Líquido" valor={<Dinheiro valor={retrato.totalLiquido} />} />
            <Numero rotulo="Inconsistências" valor={String(retrato.inconsistencias)} />
            <Numero rotulo="Pendentes" valor={String(retrato.pendentes)} />
          </dl>

          {(retrato.porSeveridade.length > 0 || retrato.competenciaAnterior) && (
            <p className="mt-2 text-[12px] text-muted-foreground">
              {retrato.porSeveridade.map((c) => `${c.rotulo}: ${c.quantidade}`).join(' · ')}
              {retrato.competenciaAnterior && (
                <>
                  {retrato.porSeveridade.length > 0 && ' — '}
                  contra {retrato.competenciaAnterior}
                  {retrato.inconsistenciasAnterior !== null &&
                    `, que teve ${retrato.inconsistenciasAnterior}`}
                </>
              )}
            </p>
          )}

          {resumo.situacao !== 'Respondeu' && (
            <p className="mt-3 text-[13px] text-muted-foreground">
              {MOTIVO[resumo.situacao] ?? MOTIVO.Indisponivel}
            </p>
          )}

          {resumo.geradoPorIa && (
            <div className="mt-3 rounded-md border border-border bg-muted/30 px-3 py-2">
              <p className="whitespace-pre-wrap text-[13px] text-foreground">{resumo.texto}</p>

              {/* ⚠️ O rótulo é requisito, não enfeite (`CLAUDE.md §37.3`). */}
              <p className="mt-2 text-[11px] text-muted-foreground">{resumo.aviso}</p>
            </div>
          )}
        </>
      )}
    </section>
  )
}

function Numero({ rotulo, valor }: { rotulo: string; valor: React.ReactNode }) {
  return (
    <div>
      <dt className="text-[11px] uppercase tracking-wide text-muted-foreground">{rotulo}</dt>
      <dd className="text-[15px] font-semibold tabular-nums text-foreground">{valor}</dd>
    </div>
  )
}
