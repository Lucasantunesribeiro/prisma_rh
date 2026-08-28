import { CalendarPlus, Trash2 } from 'lucide-react'
import { useCallback, useEffect, useState, type FormEvent } from 'react'
import {
  cancelarConcessao,
  concederFerias,
  formatarData,
  listarPeriodosFerias,
  ROTULO_SITUACAO_CONCESSAO,
  ROTULO_SITUACAO_PERIODO,
  type FeriasDoContrato,
  type PeriodoAquisitivo,
} from '@/api/pessoas'
import { CabecalhoSecao, StatusBadge } from '@/components/sistema/Primitivos'
import { EstadoCarregando } from '@/components/sistema/Estados'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Drawer, DrawerClose, DrawerContent, DrawerTrigger } from '@/components/ui/drawer'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

/**
 * Férias do contrato: o direito e a programação.
 *
 * O período aquisitivo é derivado da admissão e do calendário; a **concessão**
 * é o que tem estado. A tela junta os dois porque é assim que o analista
 * pensa: "quantos dias esta pessoa tem, e quando ela vai tirar?".
 *
 * Dois riscos precisam saltar aos olhos: um período que passou do prazo paga
 * **em dobro**, e um período sem saldo não aceita mais programação.
 *
 * Ainda não há pagamento. Programar não gera folha.
 */
export function SecaoFerias({
  idContrato,
  administra,
}: {
  idContrato: string
  administra: boolean
}) {
  const [ferias, definirFerias] = useState<FeriasDoContrato | null>(null)
  const [carregando, definirCarregando] = useState(true)
  const [erro, definirErro] = useState<string | null>(null)

  const carregar = useCallback(async () => {
    definirErro(null)

    try {
      definirFerias(await listarPeriodosFerias(idContrato))
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Não foi possível carregar as férias.')
    } finally {
      definirCarregando(false)
    }
  }, [idContrato])

  useEffect(() => {
    // O estado só muda DEPOIS do await, quando a resposta da API chega.
    // oxlint-disable-next-line react/set-state-in-effect
    void carregar()
  }, [carregar])

  const cancelar = async (id: string) => {
    definirErro(null)

    try {
      await cancelarConcessao(idContrato, id)
      await carregar()
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Não foi possível cancelar.')
    }
  }

  return (
    <section>
      <CabecalhoSecao
        titulo="Férias"
        descricao="Cada 12 meses trabalhados dão direito a 30 dias. Programar não paga: o cálculo vem depois."
      />

      {erro && (
        <Alert variant="destructive" role="alert" className="mb-3">
          <AlertDescription>{erro}</AlertDescription>
        </Alert>
      )}

      {carregando && <EstadoCarregando rotulo="Carregando férias" />}

      {!carregando && ferias && (
        <>
          {ferias.periodosVencidos > 0 && (
            <Alert variant="destructive" className="mb-3">
              <AlertDescription>
                {ferias.periodosVencidos === 1
                  ? 'Um período passou do prazo de concessão'
                  : `${ferias.periodosVencidos} períodos passaram do prazo de concessão`}
                . A remuneração desses dias é devida <strong>em dobro</strong> (CLT art. 137).
              </AlertDescription>
            </Alert>
          )}

          {ferias.saldoTotal > 0 && (
            <p className="mb-3 text-[13px] text-muted-foreground">
              <span className="tabular font-medium text-foreground">
                {ferias.saldoTotal} dias
              </span>{' '}
              disponíveis, de {ferias.diasAdquiridos} adquiridos.
            </p>
          )}

          <div className="space-y-3">
            {ferias.periodos.map((periodo) => (
              <CartaoPeriodo
                key={periodo.numero}
                idContrato={idContrato}
                periodo={periodo}
                administra={administra}
                aoMudar={carregar}
                aoCancelar={cancelar}
              />
            ))}
          </div>

          {ferias.periodos.length === 0 && (
            <p className="rounded-md border border-border p-4 text-[13px] text-muted-foreground">
              Nenhum período ainda. O primeiro começa na data de admissão.
            </p>
          )}
        </>
      )}
    </section>
  )
}

function CartaoPeriodo({
  idContrato,
  periodo,
  administra,
  aoMudar,
  aoCancelar,
}: {
  idContrato: string
  periodo: PeriodoAquisitivo
  administra: boolean
  aoMudar: () => Promise<void>
  aoCancelar: (id: string) => Promise<void>
}) {
  const emAndamento = periodo.situacao === 'EmAndamento'

  return (
    <div
      className={
        periodo.emDobra
          ? 'rounded-md border border-destructive/40 bg-destructive/5 p-3'
          : 'rounded-md border border-border p-3'
      }
    >
      <div className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-2">
        <div className="flex flex-wrap items-center gap-2">
          <span className="tabular text-[13px] font-medium">
            {formatarData(periodo.inicio)} a {formatarData(periodo.fim)}
          </span>
          <StatusBadge
            tom={
              periodo.emDobra ? 'critico' : periodo.situacao === 'Adquirido' ? 'sucesso' : 'neutro'
            }
          >
            {ROTULO_SITUACAO_PERIODO[periodo.situacao]}
          </StatusBadge>
          {emAndamento && (
            <span className="text-xs text-muted-foreground">
              faltam {periodo.diasParaCompletar} dia{periodo.diasParaCompletar === 1 ? '' : 's'}
            </span>
          )}
        </div>

        <div className="flex flex-wrap items-center gap-3 text-[13px]">
          {!emAndamento && (
            <>
              <span>
                <span className="tabular font-medium">{periodo.saldo}</span>
                <span className="text-muted-foreground"> de {periodo.diasDireito} dias</span>
              </span>
              <span className="text-muted-foreground">
                conceder até {formatarData(periodo.limiteConcessao)}
              </span>
              {administra && periodo.saldo > 0 && (
                <FormularioConcessao
                  idContrato={idContrato}
                  periodo={periodo}
                  aoConceder={aoMudar}
                />
              )}
            </>
          )}
        </div>
      </div>

      {periodo.concessoes.length > 0 && (
        <ul className="mt-3 space-y-1 border-t border-border pt-2">
          {periodo.concessoes.map((c) => (
            <li key={c.id} className="flex flex-wrap items-center gap-2 text-[13px]">
              <StatusBadge tom={c.situacao === 'EmGozo' ? 'info' : 'neutro'}>
                {ROTULO_SITUACAO_CONCESSAO[c.situacao]}
              </StatusBadge>

              {c.dias > 0 ? (
                <span className="tabular">
                  {c.dias} dias · {formatarData(c.inicio)} a {formatarData(c.fim)}
                </span>
              ) : (
                <span className="text-muted-foreground">sem gozo</span>
              )}

              {c.diasAbonoPecuniario > 0 && (
                <span className="text-muted-foreground">
                  + <span className="tabular">{c.diasAbonoPecuniario}</span> vendidos
                </span>
              )}

              {administra && c.podeCancelar && (
                <Button
                  variant="ghost"
                  size="sm"
                  aria-label={`Cancelar férias de ${formatarData(c.inicio)}`}
                  className="ml-auto size-7 p-0"
                  onClick={() => void aoCancelar(c.id)}
                >
                  <Trash2 className="size-3.5" aria-hidden />
                </Button>
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

function FormularioConcessao({
  idContrato,
  periodo,
  aoConceder,
}: {
  idContrato: string
  periodo: PeriodoAquisitivo
  aoConceder: () => Promise<void>
}) {
  const [aberto, definirAberto] = useState(false)
  const [inicio, definirInicio] = useState('')
  const [dias, definirDias] = useState(String(Math.min(periodo.saldo, 30)))
  const [abono, definirAbono] = useState('0')
  const [erro, definirErro] = useState<string | null>(null)
  const [enviando, definirEnviando] = useState(false)

  const aoEnviar = async (evento: FormEvent) => {
    evento.preventDefault()
    definirErro(null)
    definirEnviando(true)

    try {
      await concederFerias(idContrato, {
        inicioPeriodoAquisitivo: periodo.inicio.slice(0, 10),
        inicio,
        dias: Number(dias),
        diasAbonoPecuniario: Number(abono),
      })

      definirInicio('')
      definirAbono('0')
      definirAberto(false)
      await aoConceder()
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Não foi possível programar as férias.')
    } finally {
      definirEnviando(false)
    }
  }

  const prefixo = `concessao-${periodo.numero}`

  return (
    <Drawer open={aberto} onOpenChange={definirAberto}>
      <DrawerTrigger asChild>
        <Button variant="outline" size="sm">
          <CalendarPlus aria-hidden />
          Programar
        </Button>
      </DrawerTrigger>

      <DrawerContent
        titulo="Programar férias"
        descricao={`Período de ${formatarData(periodo.inicio)} a ${formatarData(periodo.fim)} — ${periodo.saldo} dias disponíveis.`}
        className="max-w-lg"
      >
        <form onSubmit={aoEnviar} className="space-y-4" noValidate>
          <div className="space-y-1.5">
            <Label htmlFor={`${prefixo}-inicio`}>Primeiro dia de férias</Label>
            <Input
              id={`${prefixo}-inicio`}
              type="date"
              required
              autoFocus
              value={inicio}
              onChange={(e) => definirInicio(e.target.value)}
            />
            <p className="text-xs text-muted-foreground">
              Só a partir de {formatarData(periodo.inicioConcessao)}, quando o direito nasce.
            </p>
          </div>

          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor={`${prefixo}-dias`}>Dias de gozo</Label>
              <Input
                id={`${prefixo}-dias`}
                required
                inputMode="numeric"
                value={dias}
                onChange={(e) => definirDias(e.target.value)}
                className="tabular"
              />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor={`${prefixo}-abono`}>Dias vendidos</Label>
              <Input
                id={`${prefixo}-abono`}
                inputMode="numeric"
                value={abono}
                onChange={(e) => definirAbono(e.target.value)}
                className="tabular"
              />
              <p className="text-xs text-muted-foreground">
                No máximo {periodo.saldoAbono} — um terço do período (CLT art. 143).
              </p>
            </div>
          </div>

          <p className="rounded-md border border-border bg-muted/30 p-3 text-xs text-muted-foreground">
            As férias podem ser divididas em até três períodos, um deles com pelo menos 14 dias
            corridos e os demais com pelo menos 5 (CLT art. 134, §1º).{' '}
            {periodo.fracoesUsadas > 0 && (
              <>
                Este período já usa{' '}
                <span className="font-medium text-foreground">{periodo.fracoesUsadas}</span> de 3.
              </>
            )}
          </p>

          {erro && (
            <Alert variant="destructive" role="alert">
              <AlertDescription>{erro}</AlertDescription>
            </Alert>
          )}

          <div className="flex justify-end gap-2 border-t border-border pt-4">
            <DrawerClose asChild>
              <Button type="button" variant="outline" size="sm">
                Cancelar
              </Button>
            </DrawerClose>
            <Button type="submit" size="sm" disabled={enviando}>
              {enviando ? 'Programando...' : 'Programar'}
            </Button>
          </div>
        </form>
      </DrawerContent>
    </Drawer>
  )
}
