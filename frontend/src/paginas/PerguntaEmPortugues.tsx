import { Search, Sparkles } from 'lucide-react'
import { useEffect, useId, useState, type FormEvent } from 'react'
import {
  consultarEmPortugues,
  vocabularioConsulta,
  type CampoConsulta,
  type RespostaConsulta,
} from '@/api/assistente'
import { ROTULO_SEVERIDADE, TOM_SEVERIDADE, type Severidade } from '@/api/analises'
import { ROTULO_STATUS, TOM_STATUS, type StatusInconsistencia } from '@/api/workflow'
import { StatusBadge } from '@/components/sistema/Primitivos'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

const MOTIVO: Record<string, string> = {
  NaoConfigurada: 'O assistente não está configurado neste ambiente.',
  LimiteAtingido: 'Limite de perguntas atingido. Tente de novo mais tarde.',
  Recusada: 'O assistente não conseguiu interpretar esta pergunta.',
  Indisponivel: 'O assistente está indisponível no momento.',
}

/**
 * A consulta em linguagem natural (Fase 11C).
 *
 * ## O que a tela mostra, e por quê
 *
 * Mostra **em que a pergunta virou** — "Severidade = Alta e Status ≠ Resolvida"
 * — antes dos resultados. Sem isso, uma interpretação errada devolve uma lista
 * plausível que responde outra coisa, e ninguém percebe.
 *
 * Mostra também o que foi **recusado**. Um filtro barrado em silêncio faria
 * quem pediu um recorte receber outro sem saber.
 *
 * ## O modelo não decide nada aqui
 *
 * Ele propõe campo, comparação e valor. O backend confere contra um vocabulário
 * fechado e monta a consulta em C#, sob o filtro global de organização. **Não
 * existe SQL vindo do modelo** (`CLAUDE.md §37.9`).
 *
 * ## Texto do modelo é texto
 *
 * Os motivos de recusa citam o que o modelo propôs, e são renderizados como
 * conteúdo — nunca como markup (`§24.9`).
 */
export function PerguntaEmPortugues({
  aoAbrir,
}: {
  aoAbrir: (idInconsistencia: string) => void
}) {
  const idCampo = useId()

  const [disponivel, definirDisponivel] = useState<boolean | null>(null)
  const [campos, definirCampos] = useState<CampoConsulta[]>([])
  const [pergunta, definirPergunta] = useState('')
  const [resposta, definirResposta] = useState<RespostaConsulta | null>(null)
  const [perguntando, definirPerguntando] = useState(false)
  const [falha, definirFalha] = useState<string | null>(null)

  useEffect(() => {
    let vivo = true

    vocabularioConsulta()
      .then((v) => {
        if (!vivo) return
        definirDisponivel(v.disponivel)
        definirCampos(v.campos)
      })
      .catch(() => vivo && definirDisponivel(false))

    return () => {
      vivo = false
    }
  }, [])

  if (disponivel !== true) return null

  const enviar = async (evento: FormEvent) => {
    evento.preventDefault()

    if (pergunta.trim().length === 0) return

    definirPerguntando(true)
    definirFalha(null)

    try {
      definirResposta(await consultarEmPortugues(pergunta.trim()))
    } catch {
      definirFalha('Não foi possível falar com o assistente.')
    } finally {
      definirPerguntando(false)
    }
  }

  return (
    <section
      aria-labelledby="pergunta-ia"
      className="rounded-lg border border-border bg-card p-4"
    >
      <h2
        id="pergunta-ia"
        className="flex items-center gap-1.5 text-[15px] font-semibold tracking-tight"
      >
        <Sparkles aria-hidden className="size-4 text-muted-foreground" />
        Perguntar em português
      </h2>

      <form onSubmit={enviar} className="mt-3 flex flex-wrap items-end gap-2">
        <div className="min-w-[16rem] flex-1 space-y-1">
          <Label htmlFor={idCampo} className="text-xs text-muted-foreground">
            Sua pergunta
          </Label>
          <Input
            id={idCampo}
            value={pergunta}
            maxLength={500}
            placeholder="Quais inconsistências críticas ainda estão abertas?"
            onChange={(e) => definirPergunta(e.target.value)}
          />
        </div>

        <Button type="submit" size="sm" disabled={perguntando || pergunta.trim().length === 0}>
          <Search aria-hidden className="size-3.5" />
          {perguntando ? 'Consultando…' : 'Consultar'}
        </Button>
      </form>

      <p className="mt-2 text-[11px] text-muted-foreground">
        Campos disponíveis: {campos.map((c) => c.campo).join(', ')}.
      </p>

      {falha && (
        <Alert variant="destructive" role="alert" className="mt-3">
          <AlertDescription>{falha}</AlertDescription>
        </Alert>
      )}

      {resposta && <Resultado resposta={resposta} aoAbrir={aoAbrir} />}
    </section>
  )
}

function Resultado({
  resposta,
  aoAbrir,
}: {
  resposta: RespostaConsulta
  aoAbrir: (id: string) => void
}) {
  const falhou =
    resposta.situacao !== 'Respondeu' && resposta.situacao !== 'NaoEntendida'

  return (
    <div className="mt-3 space-y-3">
      {falhou && (
        <p className="text-[13px] text-muted-foreground">
          {MOTIVO[resposta.situacao] ?? MOTIVO.Indisponivel}
        </p>
      )}

      {/* ⚠️ O que a aplicação entendeu, antes dos resultados. */}
      {resposta.entendido.length > 0 && (
        <p className="text-[13px] text-foreground">
          Entendi como:{' '}
          <span className="font-medium">{resposta.entendido.join(' e ')}</span>
        </p>
      )}

      {resposta.naoEntendido.length > 0 && (
        <ul className="space-y-0.5 text-[12px] text-muted-foreground">
          {resposta.naoEntendido.map((motivo) => (
            <li key={motivo}>Ignorado: {motivo}</li>
          ))}
        </ul>
      )}

      {resposta.situacao === 'NaoEntendida' && (
        <p className="text-[13px] text-muted-foreground">{resposta.aviso}</p>
      )}

      {resposta.situacao === 'Respondeu' && (
        <>
          <p className="text-[12px] text-muted-foreground">
            {resposta.total} resultado{resposta.total === 1 ? '' : 's'}
            {resposta.truncado && ` — mostrando os ${resposta.itens.length} primeiros`}
          </p>

          {resposta.itens.length === 0 ? (
            <p className="text-[13px] text-muted-foreground">
              Nenhuma inconsistência atende a esse recorte.
            </p>
          ) : (
            <ul className="divide-y divide-border rounded-md border border-border">
              {resposta.itens.map((item) => (
                <li key={item.id}>
                  <button
                    type="button"
                    onClick={() => aoAbrir(item.id)}
                    className="flex w-full flex-wrap items-center gap-2 px-3 py-2 text-left hover:bg-muted/40"
                  >
                    <StatusBadge tom={TOM_SEVERIDADE[item.severidade as Severidade]}>
                      {ROTULO_SEVERIDADE[item.severidade as Severidade]}
                    </StatusBadge>
                    <StatusBadge tom={TOM_STATUS[item.status as StatusInconsistencia]}>
                      {ROTULO_STATUS[item.status as StatusInconsistencia]}
                    </StatusBadge>
                    <span className="text-[13px] text-foreground">{item.regra}</span>
                    <span className="text-[12px] text-muted-foreground">{item.descricao}</span>
                  </button>
                </li>
              ))}
            </ul>
          )}

          <p className="text-[11px] text-muted-foreground">{resposta.aviso}</p>
        </>
      )}
    </div>
  )
}
