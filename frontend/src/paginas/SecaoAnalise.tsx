import { AlertTriangle, CheckCircle2, ShieldCheck } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import {
  ROTULO_CATEGORIA,
  ROTULO_SEVERIDADE,
  TOM_SEVERIDADE,
  analisarFolha,
  listarExecucoes,
  obterExecucao,
  type ExecucaoAnalise,
  type ResultadoAnalise,
} from '@/api/analises'
import { CabecalhoSecao, Dinheiro, StatusBadge } from '@/components/sistema/Primitivos'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'

/**
 * A conferência da folha (Fase 6).
 *
 * Mostra a última análise e deixa rodar de novo. Cada execução é uma passada
 * nova — não substitui a anterior —, porque comparar duas passadas é o que
 * mostra se a correção funcionou.
 *
 * ## O que ela NÃO faz
 *
 * Não resolve, não justifica e não marca como tratado. Isso é workflow, e
 * workflow é a Fase 7. Aqui o resultado é uma leitura: o que as regras
 * encontraram, e nada mais.
 */
export function SecaoAnalise({
  idFolha,
  situacao,
  podeExecutar,
}: {
  idFolha: string
  situacao: string
  podeExecutar: boolean
}) {
  const [execucao, definirExecucao] = useState<ExecucaoAnalise | null>(null)
  const [carregando, definirCarregando] = useState(true)
  const [erro, definirErro] = useState<string | null>(null)
  const [analisando, definirAnalisando] = useState(false)

  const carregar = useCallback(async () => {
    definirCarregando(true)
    definirErro(null)

    try {
      const pagina = await listarExecucoes(idFolha)
      const ultima = pagina.itens[0]

      // A listagem vem sem os resultados — só o resumo. Buscar o detalhe da
      // última evita trazer o relatório inteiro de todas as passadas.
      definirExecucao(ultima ? await obterExecucao(ultima.id) : null)
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Não foi possível ler a conferência.')
    } finally {
      definirCarregando(false)
    }
  }, [idFolha])

  useEffect(() => {
    // O estado só muda DEPOIS do await, quando a resposta da API chega.
    // oxlint-disable-next-line react/set-state-in-effect
    void carregar()
  }, [carregar])

  const analisar = async () => {
    definirAnalisando(true)
    definirErro(null)

    try {
      definirExecucao(await analisarFolha(idFolha))
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Não foi possível analisar.')
    } finally {
      definirAnalisando(false)
    }
  }

  const rascunho = situacao === 'Rascunho'

  return (
    <section className="mt-8">
      <CabecalhoSecao
        titulo="Conferência"
        descricao="Regras oficiais do sistema procuram inconsistências na folha calculada."
        acao={
          podeExecutar && (
            <Button
              type="button"
              size="sm"
              variant="outline"
              disabled={analisando || rascunho}
              onClick={() => void analisar()}
            >
              <ShieldCheck aria-hidden />
              {analisando ? 'Analisando...' : execucao ? 'Analisar de novo' : 'Analisar'}
            </Button>
          )
        }
      />

      <div aria-live="polite" aria-busy={carregando || analisando}>
        {rascunho && (
          <p className="text-[13px] text-muted-foreground">
            Calcule a folha antes de conferir. Em rascunho não há holerite para analisar.
          </p>
        )}

        {!rascunho && carregando && <div className="h-16 animate-pulse rounded bg-muted" />}

        {!rascunho && !carregando && erro && (
          <Alert variant="destructive" role="alert">
            <AlertDescription>{erro}</AlertDescription>
          </Alert>
        )}

        {!rascunho && !carregando && !erro && !execucao && (
          <p className="text-[13px] text-muted-foreground">
            Esta folha ainda não foi conferida.{' '}
            {podeExecutar
              ? 'Rode a análise para ver o que as regras encontram.'
              : 'Nenhuma análise foi executada até agora.'}
          </p>
        )}

        {!rascunho && !carregando && !erro && execucao && (
          <Relatorio execucao={execucao} />
        )}
      </div>
    </section>
  )
}

function Relatorio({ execucao }: { execucao: ExecucaoAnalise }) {
  const resultados = execucao.resultados ?? []

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center gap-x-5 gap-y-1 text-[12.5px] text-muted-foreground">
        <span>
          {execucao.regrasExecutadas} {execucao.regrasExecutadas === 1 ? 'regra' : 'regras'} em{' '}
          {new Date(execucao.executadaEm).toLocaleString('pt-BR')}
        </span>

        {execucao.totalResultados > 0 && (
          <span className="tabular">
            {execucao.resultadosAltos} alta · {execucao.resultadosMedios} média ·{' '}
            {execucao.resultadosBaixos} baixa
          </span>
        )}
      </div>

      {execucao.desatualizada && (
        <Alert role="status">
          <AlertDescription>
            <AlertTriangle className="mr-1.5 inline size-3.5 align-[-2px]" aria-hidden />A folha foi
            recalculada depois desta conferência — ela fala de uma versão anterior. Analise de novo.
          </AlertDescription>
        </Alert>
      )}

      {resultados.length === 0 ? (
        <p className="flex items-center gap-2 text-[13px] text-sucesso">
          <CheckCircle2 className="size-4" aria-hidden />
          Nenhuma inconsistência encontrada.
        </p>
      ) : (
        <ul className="divide-y divide-border rounded-md border border-border">
          {resultados.map((r) => (
            <Achado key={r.id} resultado={r} />
          ))}
        </ul>
      )}
    </div>
  )
}

function Achado({ resultado }: { resultado: ResultadoAnalise }) {
  return (
    <li className="px-3 py-2.5">
      <div className="flex flex-wrap items-center gap-2">
        <StatusBadge tom={TOM_SEVERIDADE[resultado.severidade]}>
          {ROTULO_SEVERIDADE[resultado.severidade]}
        </StatusBadge>

        <span className="text-[13px] font-medium text-foreground">{resultado.regra}</span>

        <span className="text-[11px] text-muted-foreground">
          {ROTULO_CATEGORIA[resultado.categoria]} · v{resultado.versaoRegra}
        </span>

        {resultado.matricula && (
          <span className="tabular text-[12px] text-muted-foreground">
            {resultado.matricula}
            {resultado.nomeFuncionario ? ` — ${resultado.nomeFuncionario}` : ''}
          </span>
        )}
      </div>

      <p className="mt-1 text-[13px] text-foreground/85">{resultado.descricao}</p>

      {(resultado.valorEsperado !== null || resultado.valorEncontrado !== null) && (
        <div className="mt-1 flex flex-wrap gap-x-6 text-[12.5px]">
          {resultado.valorEsperado !== null && (
            <Numero rotulo="Esperado" valor={resultado.valorEsperado} />
          )}
          {resultado.valorEncontrado !== null && (
            <Numero rotulo="Encontrado" valor={resultado.valorEncontrado} />
          )}
          {resultado.diferenca !== null && (
            <Numero rotulo="Diferença" valor={resultado.diferenca} enfase />
          )}
        </div>
      )}
    </li>
  )
}

function Numero({
  rotulo,
  valor,
  enfase,
}: {
  rotulo: string
  valor: number
  enfase?: boolean
}) {
  return (
    <span className={cn('text-muted-foreground', enfase && 'font-medium text-foreground')}>
      {rotulo}: <Dinheiro valor={valor} />
    </span>
  )
}
