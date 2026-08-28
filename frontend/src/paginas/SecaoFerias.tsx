import { useCallback, useEffect, useState } from 'react'
import {
  formatarData,
  listarPeriodosFerias,
  ROTULO_SITUACAO_PERIODO,
  type FeriasDoContrato,
  type PeriodoAquisitivo,
} from '@/api/pessoas'
import { DataTable, type Coluna } from '@/components/sistema/DataTable'
import { CabecalhoSecao, StatusBadge } from '@/components/sistema/Primitivos'
import { Alert, AlertDescription } from '@/components/ui/alert'

/**
 * Os períodos aquisitivos de férias do contrato.
 *
 * Somente leitura, e isso é a natureza da etapa: o período aquisitivo é
 * derivado da admissão e do calendário, não um cadastro. Quem tem estado é a
 * **concessão** de férias, que ainda não existe.
 *
 * O que a tela precisa deixar óbvio é o risco: um período que passou do prazo
 * do art. 134 paga **em dobro**. Por isso ele ganha destaque próprio em vez de
 * ser mais uma linha igual às outras.
 */
export function SecaoFerias({ idContrato }: { idContrato: string }) {
  const [ferias, definirFerias] = useState<FeriasDoContrato | null>(null)
  const [carregando, definirCarregando] = useState(true)
  const [erro, definirErro] = useState<string | null>(null)

  const carregar = useCallback(async () => {
    definirCarregando(true)
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

  const colunas: Coluna<PeriodoAquisitivo>[] = [
    {
      cabecalho: 'Nº',
      largura: '56px',
      celula: (p) => <span className="tabular text-muted-foreground">{p.numero}</span>,
    },
    {
      cabecalho: 'Período aquisitivo',
      celula: (p) => (
        <span className="tabular">
          {formatarData(p.inicio)} a {formatarData(p.fim)}
        </span>
      ),
    },
    {
      cabecalho: 'Conceder até',
      numerica: true,
      largura: '140px',
      celula: (p) =>
        p.situacao === 'EmAndamento' ? (
          <span className="text-muted-foreground">—</span>
        ) : (
          <span className={p.emDobra ? 'tabular text-destructive' : 'tabular'}>
            {formatarData(p.limiteConcessao)}
          </span>
        ),
    },
    {
      cabecalho: 'Dias',
      numerica: true,
      largura: '80px',
      celula: (p) =>
        p.situacao === 'EmAndamento' ? (
          <span className="text-muted-foreground">—</span>
        ) : (
          <span className="tabular font-medium">{p.diasDireito}</span>
        ),
    },
    {
      cabecalho: 'Situação',
      largura: '190px',
      celula: (p) => (
        <div className="flex flex-wrap items-center gap-2">
          <StatusBadge
            tom={p.emDobra ? 'critico' : p.situacao === 'Adquirido' ? 'sucesso' : 'neutro'}
          >
            {ROTULO_SITUACAO_PERIODO[p.situacao]}
          </StatusBadge>
          {p.situacao === 'EmAndamento' && (
            <span className="text-xs text-muted-foreground">
              faltam {p.diasParaCompletar} dia{p.diasParaCompletar === 1 ? '' : 's'}
            </span>
          )}
        </div>
      ),
    },
  ]

  return (
    <section>
      <CabecalhoSecao
        titulo="Férias"
        descricao="Períodos aquisitivos derivados da admissão. Cada 12 meses trabalhados dão direito a 30 dias."
      />

      {erro && (
        <Alert variant="destructive" role="alert" className="mb-3">
          <AlertDescription>{erro}</AlertDescription>
        </Alert>
      )}

      {ferias && ferias.periodosVencidos > 0 && (
        <Alert variant="destructive" className="mb-3">
          <AlertDescription>
            {ferias.periodosVencidos === 1
              ? 'Um período passou do prazo de concessão'
              : `${ferias.periodosVencidos} períodos passaram do prazo de concessão`}
            . A remuneração desses dias é devida <strong>em dobro</strong> (CLT art. 137).
          </AlertDescription>
        </Alert>
      )}

      {ferias && ferias.diasAdquiridos > 0 && (
        <p className="mb-3 text-[13px] text-muted-foreground">
          <span className="tabular font-medium text-foreground">{ferias.diasAdquiridos} dias</span>{' '}
          adquiridos e ainda não concedidos.
        </p>
      )}

      <DataTable
        rotulo="Períodos aquisitivos de férias"
        colunas={colunas}
        itens={ferias?.periodos ?? []}
        chave={(p) => `${idContrato}-${p.numero}`}
        carregando={carregando}
        aoTentarNovamente={() => void carregar()}
        vazio={{
          titulo: 'Nenhum período ainda',
          descricao: 'O primeiro período aquisitivo começa na data de admissão.',
        }}
      />

      <p className="mt-2 text-xs text-muted-foreground">
        A concessão de férias — escolher os dias e gerar o pagamento — ainda não existe. Esta tela
        mostra o direito, não o gozo.
      </p>
    </section>
  )
}
