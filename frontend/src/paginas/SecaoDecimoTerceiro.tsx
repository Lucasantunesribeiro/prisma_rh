import { useCallback, useEffect, useState } from 'react'
import {
  MESES_CURTOS,
  obterAvosDecimoTerceiro,
  type AvosDecimoTerceiro,
} from '@/api/pessoas'
import { CabecalhoSecao } from '@/components/sistema/Primitivos'
import { EstadoCarregando } from '@/components/sistema/Estados'
import { Alert, AlertDescription } from '@/components/ui/alert'

/**
 * Os avos de 13º do contrato no ano.
 *
 * Somente leitura, como os períodos aquisitivos de férias: os avos são
 * derivados da admissão, do desligamento e do calendário. Não há cadastro.
 *
 * O que a tela precisa deixar óbvio é **por que** um mês não contou. Mostrar
 * só "9/12" deixa o analista sem saber se é o mês da admissão, o do
 * desligamento, ou um erro de cadastro — e é justamente essa conferência que
 * ele faz antes de provisionar.
 */
export function SecaoDecimoTerceiro({ idContrato }: { idContrato: string }) {
  const anoAtual = new Date().getFullYear()

  const [ano, definirAno] = useState(anoAtual)
  const [avos, definirAvos] = useState<AvosDecimoTerceiro | null>(null)
  const [carregando, definirCarregando] = useState(true)
  const [erro, definirErro] = useState<string | null>(null)

  const carregar = useCallback(async () => {
    definirErro(null)

    try {
      definirAvos(await obterAvosDecimoTerceiro(idContrato, ano))
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Não foi possível carregar os avos.')
    } finally {
      definirCarregando(false)
    }
  }, [idContrato, ano])

  useEffect(() => {
    // O estado só muda DEPOIS do await, quando a resposta da API chega.
    // oxlint-disable-next-line react/set-state-in-effect
    void carregar()
  }, [carregar])

  return (
    <section>
      <CabecalhoSecao
        titulo="13º salário"
        descricao="Um avo por mês trabalhado. Mês com 15 dias ou mais conta inteiro (Lei 4.090/1962)."
        acao={
          <div className="flex items-center gap-2">
            <label htmlFor={`ano-13-${idContrato}`} className="rotulo-secao">
              Ano
            </label>
            <select
              id={`ano-13-${idContrato}`}
              value={ano}
              onChange={(e) => definirAno(Number(e.target.value))}
              className="tabular h-8 rounded-md border border-input bg-card px-2 text-[13px] shadow-xs"
            >
              {[anoAtual, anoAtual - 1, anoAtual - 2].map((a) => (
                <option key={a} value={a}>
                  {a}
                </option>
              ))}
            </select>
          </div>
        }
      />

      {erro && (
        <Alert variant="destructive" role="alert" className="mb-3">
          <AlertDescription>{erro}</AlertDescription>
        </Alert>
      )}

      {carregando && <EstadoCarregando rotulo="Carregando avos" />}

      {!carregando && avos && (
        <div className="rounded-md border border-border p-3">
          <p className="mb-3 text-[13px]">
            <span className="tabular text-[15px] font-medium">{avos.fracao}</span>{' '}
            <span className="text-muted-foreground">
              {avos.anoCompleto
                ? 'do 13º — ano completo'
                : `do 13º de ${avos.ano} — ${avos.avos === 1 ? '1 mês contou' : `${avos.avos} meses contaram`}`}
            </span>
          </p>

          {/*
           * Os doze meses sempre, e não só os que contam: o valor da tela está
           * em explicar a ausência. Um mês apagado com "só 12 dias" responde a
           * pergunta antes de ela ser feita.
           */}
          <ul className="grid grid-cols-3 gap-1.5 sm:grid-cols-4 lg:grid-cols-6">
            {avos.meses.map((m) => (
              <li
                key={m.mes}
                title={m.motivo}
                className={
                  m.conta
                    ? 'rounded border border-border bg-muted/40 px-2 py-1.5'
                    : 'rounded border border-dashed border-border px-2 py-1.5 opacity-60'
                }
              >
                <span className="text-[13px] font-medium capitalize">
                  {MESES_CURTOS[m.mes - 1]}
                </span>
                <span className="mt-0.5 block text-xs text-muted-foreground">
                  {m.conta ? `${m.diasTrabalhados} dias` : '—'}
                </span>
              </li>
            ))}
          </ul>

          <p className="mt-3 text-xs text-muted-foreground">
            O pagamento do 13º ainda não existe. Esta seção mostra o direito, não o valor.
          </p>
        </div>
      )}
    </section>
  )
}
