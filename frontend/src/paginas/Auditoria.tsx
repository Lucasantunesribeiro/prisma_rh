import { useCallback, useEffect, useState } from 'react'
import { listarAuditoria, type EventoAuditoria } from '@/api/workflow'
import { DataTable, type Coluna } from '@/components/sistema/DataTable'
import { CabecalhoPagina, FiltroSelect } from '@/components/sistema/Primitivos'
import { usePagina } from '@/layout/usePagina'

/**
 * As entidades que a trilha registra.
 *
 * Espelha o `enum` do servidor. Um valor que ele não conheça volta **400**, e
 * não uma lista sem filtro — o filtro é vocabulário fechado dos dois lados.
 */
const ENTIDADES = [
  'Funcionario',
  'ContratoTrabalho',
  'FolhaPagamento',
  'Rubrica',
  'ValorBaseFgtsRescisorio',
  'RegraAnalise',
  'ExecucaoAnalise',
  'ResultadoAnalise',
  'Importacao',
  'ConsultaCnpj',
  'ExplicacaoIa',
] as const

const ROTULO_ENTIDADE: Record<string, string> = {
  Funcionario: 'Funcionário',
  ContratoTrabalho: 'Contrato',
  FolhaPagamento: 'Folha',
  Rubrica: 'Rubrica',
  ValorBaseFgtsRescisorio: 'Valor base do FGTS',
  RegraAnalise: 'Regra de conferência',
  ExecucaoAnalise: 'Análise',
  ResultadoAnalise: 'Inconsistência',
  Importacao: 'Importação',
  ConsultaCnpj: 'Consulta de CNPJ',

  // ⚠️ Faltava, e por isso a tela mostrava o valor cru `ExplicacaoIa`.
  // Rotulo ausente nao quebra nada - so fica feio, que e como este passou.
  ExplicacaoIa: 'Assistente de IA',
}

/**
 * A trilha de auditoria de negócio (Fase 7).
 *
 * ## Só leitura, e isso é a funcionalidade
 *
 * Não há botão de criar, de editar nem de apagar — porque não existe rota para
 * nenhum dos três, para perfil nenhum, inclusive Administrador da Plataforma
 * (`CLAUDE.md §24.17`). Uma trilha que alguém pode editar não é trilha.
 *
 * ## Isto não é o log técnico
 *
 * `CLAUDE.md §26`: o log técnico é rotativo e descartável, e responde "por que a
 * requisição demorou". Esta tela responde "quem alterou o salário dela, quando,
 * e de quanto para quanto" — pergunta que pode aparecer anos depois.
 */
export default function Auditoria() {
  usePagina([{ texto: 'Administração' }, { texto: 'Auditoria' }])

  const [itens, definirItens] = useState<EventoAuditoria[]>([])
  const [total, definirTotal] = useState(0)
  const [carregando, definirCarregando] = useState(true)
  const [erro, definirErro] = useState<string | null>(null)
  const [entidade, definirEntidade] = useState('')

  const carregar = useCallback(async () => {
    definirCarregando(true)
    definirErro(null)

    try {
      const pagina = await listarAuditoria({ entidade: entidade || undefined })

      definirItens(pagina.itens)
      definirTotal(pagina.total)
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Não foi possível carregar a trilha.')
    } finally {
      definirCarregando(false)
    }
  }, [entidade])

  useEffect(() => {
    // O estado só muda DEPOIS do await, quando a resposta da API chega.
    // oxlint-disable-next-line react/set-state-in-effect
    void carregar()
  }, [carregar])

  const colunas: Coluna<EventoAuditoria>[] = [
    {
      cabecalho: 'Quando',
      largura: '170px',
      celula: (e) => (
        <span className="tabular">{new Date(e.ocorridoEm).toLocaleString('pt-BR')}</span>
      ),
    },
    {
      cabecalho: 'Quem',
      largura: '160px',
      celula: (e) => (
        <span className="truncate">{e.usuario ?? <em className="text-muted-foreground">sistema</em>}</span>
      ),
    },
    {
      cabecalho: 'O quê',
      celula: (e) => <span className="text-foreground">{e.descricao}</span>,
    },
    {
      cabecalho: 'Sobre',
      largura: '170px',
      secundaria: true,
      celula: (e) => (
        <span className="text-muted-foreground">{ROTULO_ENTIDADE[e.entidade] ?? e.entidade}</span>
      ),
    },
    {
      cabecalho: 'Contexto',
      largura: '260px',
      secundaria: true,
      celula: (e) =>
        e.contexto ? (
          <span className="tabular block truncate text-[11.5px] text-muted-foreground" title={e.contexto}>
            {e.contexto}
          </span>
        ) : (
          <span className="text-muted-foreground">—</span>
        ),
    },
  ]

  return (
    <>
      <CabecalhoPagina
        titulo="Auditoria"
        descricao="O que aconteceu, quem fez e quando. Registro permanente — ninguém edita, de nenhum perfil."
      />

      <DataTable
        rotulo="Trilha de auditoria da organização"
        colunas={colunas}
        itens={itens}
        chave={(e) => e.id}
        carregando={carregando}
        erro={erro}
        aoTentarNovamente={() => void carregar()}
        filtrado={entidade !== ''}
        aoLimparFiltros={() => definirEntidade('')}
        vazio={{
          titulo: 'Nada registrado ainda',
          descricao: 'Calcular folha, configurar regra ou tratar uma inconsistência gera eventos aqui.',
        }}
        toolbar={
          <FiltroSelect
            rotulo="Filtrar por entidade"
            valor={entidade}
            aoMudar={definirEntidade}
            opcoes={[
              { valor: '', texto: 'Tudo' },
              ...ENTIDADES.map((e) => ({ valor: e, texto: ROTULO_ENTIDADE[e] })),
            ]}
          />
        }
        rodape={
          <span>
            {total} {total === 1 ? 'evento' : 'eventos'}
          </span>
        }
      />
    </>
  )
}
