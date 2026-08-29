import { useCallback, useEffect, useState } from 'react'
import {
  apurarRescisao,
  formatarData,
  formatarSalario,
  ROTULO_MOTIVO_DESLIGAMENTO,
  type Rescisao,
} from '@/api/pessoas'
import { DataTable, type Coluna } from '@/components/sistema/DataTable'
import { CabecalhoSecao, Dinheiro } from '@/components/sistema/Primitivos'
import { EstadoCarregando } from '@/components/sistema/Estados'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

/**
 * A simulação das verbas rescisórias.
 *
 * **Simulação, não folha**: responde "quanto esta rescisão vale e por quê", e
 * não gera holerite. Por isso não há botão de confirmar.
 *
 * O **valor base do FGTS** é digitado pelo analista, e não calculado — o saldo
 * real da conta vinculada tem correção e juros que o produto não conhece. O que
 * o sistema sabe aparece ao lado, para comparação.
 *
 * Três motivos são **bloqueados** por falta de fonte oficial. Para eles a tela
 * mostra a razão e o contexto, sem nenhum número — o contrário seria pior:
 * um valor com cara de exato sobre uma regra que ninguém confirmou.
 */
export function SecaoRescisao({ idContrato }: { idContrato: string }) {
  const [valorBase, definirValorBase] = useState('')
  const [aplicado, definirAplicado] = useState<number | undefined>(undefined)
  const [rescisao, definirRescisao] = useState<Rescisao | null>(null)
  const [carregando, definirCarregando] = useState(true)
  const [erro, definirErro] = useState<string | null>(null)

  const carregar = useCallback(async () => {
    definirErro(null)

    try {
      definirRescisao(await apurarRescisao(idContrato, aplicado))
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Não foi possível apurar a rescisão.')
    } finally {
      definirCarregando(false)
    }
  }, [idContrato, aplicado])

  useEffect(() => {
    // O estado só muda DEPOIS do await, quando a resposta da API chega.
    // oxlint-disable-next-line react/set-state-in-effect
    void carregar()
  }, [carregar])

  const colunas: Coluna<Rescisao['verbas'][number]>[] = [
    {
      cabecalho: 'Cód.',
      largura: '100px',
      celula: (v) => <span className="tabular text-muted-foreground">{v.codigo}</span>,
    },
    { cabecalho: 'Verba', celula: (v) => v.nome },
    {
      cabecalho: 'Ref.',
      largura: '90px',
      celula: (v) => <span className="tabular text-xs text-muted-foreground">{v.referencia}</span>,
    },
    {
      cabecalho: 'Valor',
      numerica: true,
      largura: '130px',
      celula: (v) => <Dinheiro valor={v.valor} />,
    },
  ]

  return (
    <section>
      <CabecalhoSecao
        titulo="Rescisão"
        descricao="Simulação das verbas. Não gera folha — o cálculo definitivo é a etapa seguinte."
      />

      {erro && (
        <Alert variant="destructive" role="alert" className="mb-3">
          <AlertDescription>{erro}</AlertDescription>
        </Alert>
      )}

      {carregando && <EstadoCarregando rotulo="Apurando rescisão" />}

      {!carregando && rescisao && (
        <div className="space-y-3">
          <p className="text-[13px] text-muted-foreground">
            {ROTULO_MOTIVO_DESLIGAMENTO[rescisao.motivo]} em{' '}
            {formatarData(rescisao.dataDesligamento)} · salário de referência{' '}
            <span className="tabular font-medium text-foreground">
              {formatarSalario(rescisao.salarioReferencia)}
            </span>
          </p>

          {!rescisao.suportado && (
            <Alert variant="destructive">
              <AlertDescription>
                <strong>Este motivo não é calculado pelo Prisma RH.</strong>{' '}
                {rescisao.motivoDoBloqueio}
              </AlertDescription>
            </Alert>
          )}

          {rescisao.suportado && (
            <>
              <div className="flex flex-wrap items-end gap-3 rounded-md border border-border bg-muted/30 p-3">
                <div className="space-y-1.5">
                  <Label htmlFor={`base-fgts-${idContrato}`}>
                    Valor base do FGTS para fins rescisórios
                  </Label>
                  <Input
                    id={`base-fgts-${idContrato}`}
                    inputMode="decimal"
                    placeholder="0,00"
                    value={valorBase}
                    onChange={(e) => definirValorBase(e.target.value)}
                    className="tabular w-48"
                  />
                </div>

                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => definirAplicado(Number(valorBase.replace(',', '.')) || undefined)}
                >
                  Aplicar
                </Button>

                <p className="w-full text-xs text-muted-foreground">
                  Informado por você, como no FGTS Digital. O saldo real da conta vinculada tem
                  correção e juros que este sistema não conhece — ele só sabe os depósitos que
                  apurou:{' '}
                  <span className="tabular font-medium text-foreground">
                    {formatarSalario(rescisao.valorBaseFgts?.conhecidoPeloSistema ?? 0)}
                  </span>
                  .
                </p>
              </div>

              {rescisao.valorBaseFgts?.abaixoDoConhecido && (
                <Alert variant="destructive">
                  <AlertDescription>
                    O valor informado está <strong>abaixo</strong> do que o sistema já depositou. A
                    multa sairia menor que a devida — confira o número ou o histórico de
                    competências.
                  </AlertDescription>
                </Alert>
              )}

              <DataTable
                rotulo="Verbas rescisórias"
                colunas={colunas}
                itens={rescisao.verbas}
                chave={(v) => v.codigo}
                vazio={{
                  titulo: 'Nenhuma verba',
                  descricao: 'Este motivo não gera verba alguma nesta configuração.',
                }}
                rodape={
                  <span className="flex items-center justify-between">
                    <span className="text-muted-foreground">Total</span>
                    <span className="tabular text-[15px] font-medium">
                      <Dinheiro valor={rescisao.total} enfase />
                    </span>
                  </span>
                }
              />
            </>
          )}

          <dl className="grid gap-3 text-[13px] sm:grid-cols-3">
            <div className="rounded-md border border-border px-3 py-2">
              <dt className="rotulo-secao">Aviso prévio</dt>
              <dd className="mt-0.5">
                {rescisao.aviso && rescisao.aviso.dias > 0 ? (
                  <>
                    <span className="tabular font-medium">{rescisao.aviso.dias} dias</span>
                    <span className="text-muted-foreground">
                      {' '}
                      · devido{' '}
                      {rescisao.aviso.devedor === 'Empregado'
                        ? 'pelo empregado'
                        : 'pelo empregador'}
                    </span>
                  </>
                ) : (
                  <span className="text-muted-foreground">não se aplica</span>
                )}
              </dd>
            </div>

            <div className="rounded-md border border-border px-3 py-2">
              <dt className="rotulo-secao">Férias</dt>
              <dd className="mt-0.5">
                <span className="tabular font-medium">{rescisao.diasFeriasVencidas} dias</span>
                <span className="text-muted-foreground"> vencidos</span>
                {rescisao.feriasProporcionais && (
                  <span className="text-muted-foreground">
                    {' '}
                    · <span className="tabular">{rescisao.feriasProporcionais.fracao}</span>{' '}
                    proporcionais
                  </span>
                )}
              </dd>
            </div>

            <div className="rounded-md border border-border px-3 py-2">
              <dt className="rotulo-secao">13º proporcional</dt>
              <dd className="mt-0.5">
                <span className="tabular font-medium">{rescisao.fracao13 ?? '0/12'}</span>
                <span className="text-muted-foreground"> · não calculado ainda</span>
              </dd>
            </div>
          </dl>

          <p className="text-xs text-muted-foreground">
            <span className="font-medium">Fonte:</span> {rescisao.fonte}
          </p>
        </div>
      )}
    </section>
  )
}
