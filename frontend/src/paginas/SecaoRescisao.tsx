import { useCallback, useEffect, useState } from 'react'
import {
  apurarRescisao,
  formatarData,
  formatarSalario,
  informarValorBaseFgts,
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
 * A apuração das verbas rescisórias.
 *
 * **Apuração, não folha**: responde "quanto esta rescisão vale e por quê". A
 * folha de rescisão usa exatamente estas verbas e é aberta em Folhas, com o
 * tipo Rescisão — por isso não há botão de confirmar aqui.
 *
 * O **valor base do FGTS** é digitado pelo analista e **gravado** (PUT), não
 * calculado: o saldo real da conta vinculada tem correção e juros que o produto
 * não conhece. Fica registrado com data e observação, porque é um número que
 * entra na multa. O que o sistema apurou aparece ao lado, para comparação —
 * nunca como substituto.
 *
 * Três motivos são **bloqueados** por falta de fonte oficial. Para eles a tela
 * mostra a razão e o contexto, sem nenhum número — o contrário seria pior:
 * um valor com cara de exato sobre uma regra que ninguém confirmou.
 */
export function SecaoRescisao({ idContrato }: { idContrato: string }) {
  const [valorBase, definirValorBase] = useState('')
  const [rescisao, definirRescisao] = useState<Rescisao | null>(null)
  const [carregando, definirCarregando] = useState(true)
  const [salvando, definirSalvando] = useState(false)
  const [erro, definirErro] = useState<string | null>(null)

  const carregar = useCallback(async () => {
    definirErro(null)

    try {
      const apurada = await apurarRescisao(idContrato)
      definirRescisao(apurada)

      // Preenche a caixa com o que está gravado. Só acontece na carga e depois
      // de salvar, nunca no meio da digitação.
      definirValorBase(
        apurada.valorBaseFgts === null ? '' : apurada.valorBaseFgts.informado.toFixed(2),
      )
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Não foi possível apurar a rescisão.')
    } finally {
      definirCarregando(false)
    }
  }, [idContrato])

  useEffect(() => {
    // O estado só muda DEPOIS do await, quando a resposta da API chega.
    // oxlint-disable-next-line react/set-state-in-effect
    void carregar()
  }, [carregar])

  async function salvarValorBase() {
    const valor = Number(valorBase.replace(',', '.'))

    if (!Number.isFinite(valor) || valor < 0) {
      definirErro('Informe o valor base do FGTS como um número não negativo.')
      return
    }

    definirSalvando(true)

    try {
      await informarValorBaseFgts(idContrato, { valor })
      await carregar()
    } catch (falha) {
      definirErro(
        falha instanceof Error ? falha.message : 'Não foi possível gravar o valor base do FGTS.',
      )
    } finally {
      definirSalvando(false)
    }
  }

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

  const projetou = rescisao !== null && rescisao.dataProjetada !== rescisao.dataDesligamento

  return (
    <section>
      <CabecalhoSecao
        titulo="Rescisão"
        descricao="Apuração das verbas. A folha de rescisão é aberta em Folhas, com estas mesmas verbas."
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
            {projetou && (
              <>
                {' '}
                · saída na CTPS{' '}
                <span className="tabular font-medium text-foreground">
                  {formatarData(rescisao.dataProjetada)}
                </span>{' '}
                pela projeção do aviso
              </>
            )}
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
                  disabled={salvando}
                  onClick={() => void salvarValorBase()}
                >
                  {salvando ? 'Salvando...' : 'Salvar'}
                </Button>

                <p className="w-full text-xs text-muted-foreground">
                  Informado por você, como no FGTS Digital, e gravado no contrato. O saldo real da
                  conta vinculada tem correção e juros que este sistema não conhece — ele só sabe os
                  depósitos que apurou:{' '}
                  <span className="tabular font-medium text-foreground">
                    {formatarSalario(rescisao.fgtsConhecidoPeloSistema)}
                  </span>
                  .
                  {rescisao.valorBaseFgts?.informadoEm && (
                    <> Informado em {formatarData(rescisao.valorBaseFgts.informadoEm)}.</>
                  )}
                </p>
              </div>

              {rescisao.valorBaseFgts === null && (
                <Alert>
                  <AlertDescription>
                    Sem o valor base informado, a <strong>multa do FGTS não é calculada</strong> —
                    melhor nenhuma linha do que uma sobre um número que o sistema não tem.
                  </AlertDescription>
                </Alert>
              )}

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
                {rescisao.avosDoAviso > 0 && (
                  <span className="text-muted-foreground">
                    {' '}
                    · + <span className="tabular">{rescisao.avosDoAviso}/12</span> pelo aviso
                  </span>
                )}
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
