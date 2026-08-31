import { useCallback, useEffect, useState } from 'react'
import { ROTULO_SEVERIDADE } from '@/api/analises'
import {
  ROTULO_STATUS,
  obterPainel,
  type ContagemPorRotulo,
  type Painel as PainelDados,
  type StatusInconsistencia,
} from '@/api/workflow'
import { EstadoCarregando, EstadoErro } from '@/components/sistema/Estados'
import { CabecalhoPagina, CabecalhoSecao, ResumoFinanceiro } from '@/components/sistema/Primitivos'
import { useEmpresaAtual } from '@/layout/contexto'
import { usePagina } from '@/layout/usePagina'
import { cn } from '@/lib/utils'

/**
 * O painel operacional (Fase 7).
 *
 * ## Todo número aqui vem do banco
 *
 * O critério de aceite da fase é explícito: "dashboard usa dados reais do
 * sistema". Não há valor semeado, não há exemplo, e nada é calculado no
 * navegador — cada indicador é uma agregação que o servidor fez sobre entidade
 * filtrada.
 *
 * ## Barras em CSS, e não uma biblioteca de gráficos
 *
 * As proporções aqui são de uma dimensão só: quanto de cada severidade, quanto
 * por status. Uma biblioteca de gráficos resolveria isso trazendo algumas
 * centenas de kilobytes e uma dependência a mais para manter — o
 * `CLAUDE.md §24.25` manda não instalar biblioteca para funcionalidade trivial.
 *
 * Cada barra tem o número escrito ao lado: quem não distingue as cores lê o
 * valor do mesmo jeito.
 */
export default function Painel() {
  usePagina([{ texto: 'Folha' }, { texto: 'Painel' }])

  const { empresaAtual } = useEmpresaAtual()

  const [dados, definirDados] = useState<PainelDados | null>(null)
  const [carregando, definirCarregando] = useState(true)
  const [erro, definirErro] = useState<string | null>(null)

  const idEmpresa = empresaAtual?.id

  const carregar = useCallback(async () => {
    definirCarregando(true)
    definirErro(null)

    try {
      definirDados(await obterPainel(idEmpresa))
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Não foi possível carregar o painel.')
    } finally {
      definirCarregando(false)
    }
  }, [idEmpresa])

  useEffect(() => {
    // O estado só muda DEPOIS do await, quando a resposta da API chega.
    // oxlint-disable-next-line react/set-state-in-effect
    void carregar()
  }, [carregar])

  return (
    <>
      <CabecalhoPagina
        titulo="Painel"
        descricao="Onde está o trabalho de conferência, com os números do próprio sistema."
      />

      <div aria-live="polite" aria-busy={carregando}>
        {carregando && <EstadoCarregando />}

        {!carregando && erro && (
          <EstadoErro mensagem={erro} aoTentarNovamente={() => void carregar()} />
        )}

        {!carregando && !erro && dados && <Conteudo dados={dados} />}
      </div>
    </>
  )
}

function Conteudo({ dados }: { dados: PainelDados }) {
  const semNada =
    dados.folhasCalculadas === 0 && dados.inconsistenciasTotais === 0

  if (semNada) {
    return (
      <p className="text-[13px] text-muted-foreground">
        Ainda não há folha calculada nesta empresa. Calcule uma folha e rode a conferência para o
        painel ter o que mostrar.
      </p>
    )
  }

  return (
    <div className="space-y-8">
      <ResumoFinanceiro
        itens={[
          {
            rotulo: 'Folhas calculadas',
            valor: <span className="tabular">{dados.folhasCalculadas}</span>,
          },
          {
            rotulo: 'Fechadas',
            valor: <span className="tabular">{dados.folhasFechadas}</span>,
          },
          {
            rotulo: 'Pendentes',
            valor: (
              <span
                className={cn(
                  'tabular',
                  dados.inconsistenciasPendentes > 0 && 'text-critico',
                )}
              >
                {dados.inconsistenciasPendentes}
              </span>
            ),
          },
          {
            rotulo: 'Conformidade',
            valor: (
              <span className="tabular">
                {dados.percentualConformidade === null
                  ? '—'
                  : `${dados.percentualConformidade.toLocaleString('pt-BR')}%`}
              </span>
            ),
            enfase: true,
          },
        ]}
      />

      {dados.percentualConformidade === null && dados.inconsistenciasTotais === 0 && (
        <p className="-mt-6 text-[12px] text-muted-foreground">
          Sem inconsistência não há conformidade a medir — 100% aqui seria uma afirmação que o
          sistema não tem como sustentar.
        </p>
      )}

      <div className="grid gap-8 lg:grid-cols-2">
        <Barras
          titulo="Por severidade"
          descricao="Quanto de cada gravidade a conferência apontou."
          itens={dados.porSeveridade}
          rotular={(r) => ROTULO_SEVERIDADE[r as keyof typeof ROTULO_SEVERIDADE] ?? r}
        />

        <Barras
          titulo="Por situação"
          descricao="Em que ponto do tratamento cada uma está."
          itens={dados.porStatus}
          rotular={(r) => ROTULO_STATUS[r as StatusInconsistencia] ?? r}
        />
      </div>

      <Barras
        titulo="Regras com maior incidência"
        descricao="Qual conferência acusa mais — e onde vale investir em prevenção."
        itens={dados.porRegra}
      />

      <section>
        <CabecalhoSecao
          titulo="Pendências por responsável"
          descricao="Quem tem trabalho na mão agora. Só o que ainda não foi resolvido."
        />

        {dados.porResponsavel.length === 0 ? (
          <p className="text-[13px] text-muted-foreground">Nenhuma pendência.</p>
        ) : (
          <ul className="divide-y divide-border rounded-md border border-border">
            {dados.porResponsavel.map((p) => (
              <li
                key={p.idResponsavel ?? 'sem'}
                className="flex items-center justify-between px-3 py-2 text-[13px]"
              >
                <span className={cn(!p.idResponsavel && 'text-muted-foreground')}>
                  {p.responsavel}
                </span>
                <span className="tabular font-medium">{p.quantidade}</span>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section>
        <CabecalhoSecao
          titulo="Evolução por competência"
          descricao="Folhas processadas, inconsistências encontradas e quantas foram encerradas."
        />

        {dados.evolucao.length === 0 ? (
          <p className="text-[13px] text-muted-foreground">Sem competências processadas.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full border-collapse text-[13px]">
              <caption className="sr-only">Evolução por competência</caption>
              <thead>
                <tr className="border-b border-border text-xs text-muted-foreground">
                  <th scope="col" className="py-2 text-left font-medium">
                    Competência
                  </th>
                  <th scope="col" className="w-28 py-2 text-right font-medium">
                    Folhas
                  </th>
                  <th scope="col" className="w-36 py-2 text-right font-medium">
                    Inconsistências
                  </th>
                  <th scope="col" className="w-28 py-2 text-right font-medium">
                    Resolvidas
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {dados.evolucao.map((e) => (
                  <tr key={e.competencia}>
                    <td className="tabular py-2">{e.competencia}</td>
                    <td className="tabular py-2 text-right">{e.folhas}</td>
                    <td className="tabular py-2 text-right">{e.inconsistencias}</td>
                    <td className="tabular py-2 text-right text-sucesso">{e.resolvidas}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  )
}

function Barras({
  titulo,
  descricao,
  itens,
  rotular,
}: {
  titulo: string
  descricao: string
  itens: ContagemPorRotulo[]
  rotular?: (rotulo: string) => string
}) {
  const maior = itens.reduce((m, i) => Math.max(m, i.quantidade), 0)

  return (
    <section>
      <CabecalhoSecao titulo={titulo} descricao={descricao} />

      {itens.length === 0 ? (
        <p className="text-[13px] text-muted-foreground">Nada a mostrar.</p>
      ) : (
        <ul className="space-y-1.5">
          {itens.map((i) => (
            <li key={i.rotulo} className="flex items-center gap-3 text-[13px]">
              <span className="w-44 shrink-0 truncate" title={i.rotulo}>
                {rotular ? rotular(i.rotulo) : i.rotulo}
              </span>

              {/* A barra é decorativa: o número ao lado diz tudo sozinho. */}
              <span aria-hidden className="h-2 min-w-0 flex-1 rounded-full bg-muted">
                <span
                  className="block h-2 rounded-full bg-primary"
                  style={{ width: maior === 0 ? '0%' : `${(i.quantidade / maior) * 100}%` }}
                />
              </span>

              <span className="tabular w-10 shrink-0 text-right font-medium">{i.quantidade}</span>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
