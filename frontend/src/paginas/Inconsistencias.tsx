import { MessageSquare, Paperclip, Sparkles, UserRound } from 'lucide-react'
import { useCallback, useEffect, useId, useMemo, useState } from 'react'
import { ROTULO_SEVERIDADE, TOM_SEVERIDADE, type Severidade } from '@/api/analises'
import {
  assistenteDisponivel,
  explicarInconsistencia,
  type Explicacao,
} from '@/api/assistente'
import {
  EXPLICACAO_STATUS,
  ROTULO_ANDAMENTO,
  ROTULO_STATUS,
  TOM_STATUS,
  atribuir,
  comentar,
  listarInconsistencias,
  obterInconsistencia,
  podeTratar,
  registrarEvidencia,
  transitar,
  type Andamento,
  type Inconsistencia,
  type StatusInconsistencia,
} from '@/api/workflow'
import { useSessao } from '@/auth/useSessao'
import { DataTable, type Coluna } from '@/components/sistema/DataTable'
import {
  CabecalhoPagina,
  Campo,
  Dinheiro,
  FiltroSelect,
  ListaCampos,
  StatusBadge,
} from '@/components/sistema/Primitivos'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Drawer, DrawerContent } from '@/components/ui/drawer'
import { Label } from '@/components/ui/label'
import { usePagina } from '@/layout/usePagina'
import { cn } from '@/lib/utils'

const STATUS: StatusInconsistencia[] = [
  'Detectada',
  'EmAnalise',
  'Justificada',
  'Corrigida',
  'Resolvida',
]

const SEVERIDADES: Severidade[] = ['Alta', 'Media', 'Baixa']

/**
 * A caixa de trabalho das inconsistências (Fase 7).
 *
 * A Fase 6 encontra; aqui o achado vira trabalho — alguém assume, escreve o que
 * descobriu e conclui.
 *
 * ## O que esta tela NÃO decide
 *
 * Para onde dá para ir a partir de cada status vem do **servidor**, no campo
 * `proximosStatus`. A tela não repete a máquina de estados: duas cópias
 * divergiriam, e a da tela é a que ninguém testa.
 *
 * ## Texto de outro usuário
 *
 * Comentários e justificativas são renderizados como **texto**. O React escapa
 * por padrão, e não há um único `dangerouslySetInnerHTML` no projeto
 * (`CLAUDE.md §24.9`).
 */
export default function Inconsistencias() {
  usePagina([{ texto: 'Folha' }, { texto: 'Inconsistências' }])

  const { usuario } = useSessao()
  const trata = podeTratar(usuario?.perfil)

  const [itens, definirItens] = useState<Inconsistencia[]>([])
  const [total, definirTotal] = useState(0)
  const [carregando, definirCarregando] = useState(true)
  const [erro, definirErro] = useState<string | null>(null)

  const [status, definirStatus] = useState<string>('')
  const [severidade, definirSeveridade] = useState<string>('')
  const [aberta, definirAberta] = useState<string | null>(null)

  const carregar = useCallback(async () => {
    definirCarregando(true)
    definirErro(null)

    try {
      const pagina = await listarInconsistencias({
        status: (status || undefined) as StatusInconsistencia | undefined,
        severidade: (severidade || undefined) as Severidade | undefined,
      })

      definirItens(pagina.itens)
      definirTotal(pagina.total)
    } catch (falha) {
      definirErro(
        falha instanceof Error ? falha.message : 'Não foi possível carregar as inconsistências.',
      )
    } finally {
      definirCarregando(false)
    }
  }, [status, severidade])

  useEffect(() => {
    // O estado só muda DEPOIS do await, quando a resposta da API chega.
    // oxlint-disable-next-line react/set-state-in-effect
    void carregar()
  }, [carregar])

  const filtrado = status !== '' || severidade !== ''

  const colunas: Coluna<Inconsistencia>[] = useMemo(
    () => [
      {
        cabecalho: 'Severidade',
        largura: '110px',
        celula: (i) => (
          <StatusBadge tom={TOM_SEVERIDADE[i.severidade]}>
            {ROTULO_SEVERIDADE[i.severidade]}
          </StatusBadge>
        ),
      },
      {
        cabecalho: 'Situação',
        largura: '130px',
        celula: (i) => (
          <StatusBadge tom={TOM_STATUS[i.status]}>{ROTULO_STATUS[i.status]}</StatusBadge>
        ),
      },
      {
        cabecalho: 'Regra',
        celula: (i) => (
          <span className="min-w-0">
            <span className="block font-medium text-foreground">{i.regra}</span>
            <span className="block truncate text-[12px] text-muted-foreground">{i.descricao}</span>
          </span>
        ),
      },
      {
        cabecalho: 'Funcionário',
        largura: '200px',
        secundaria: true,
        celula: (i) =>
          i.matricula ? (
            <span className="min-w-0">
              <span className="tabular block text-[12px] text-muted-foreground">{i.matricula}</span>
              <span className="block truncate">{i.nomeFuncionario}</span>
            </span>
          ) : (
            <span className="text-muted-foreground">—</span>
          ),
      },
      {
        cabecalho: 'Competência',
        largura: '110px',
        secundaria: true,
        celula: (i) => <span className="tabular">{i.competencia || '—'}</span>,
      },
      {
        cabecalho: 'Responsável',
        largura: '150px',
        celula: (i) =>
          i.responsavel ? (
            <span className="truncate">{i.responsavel}</span>
          ) : (
            <span className="text-muted-foreground">Sem responsável</span>
          ),
      },
    ],
    [],
  )

  return (
    <>
      <CabecalhoPagina
        titulo="Inconsistências"
        descricao="O que a conferência encontrou, e em que ponto está o tratamento."
      />

      <DataTable
        rotulo="Inconsistências da organização"
        colunas={colunas}
        itens={itens}
        chave={(i) => i.id}
        carregando={carregando}
        erro={erro}
        aoTentarNovamente={() => void carregar()}
        aoClicarLinha={(i) => definirAberta(i.id)}
        filtrado={filtrado}
        aoLimparFiltros={() => {
          definirStatus('')
          definirSeveridade('')
        }}
        vazio={{
          titulo: 'Nenhuma inconsistência',
          descricao: 'Rode a conferência de uma folha calculada para ver o que as regras encontram.',
        }}
        toolbar={
          <>
            <FiltroSelect
              rotulo="Filtrar por situação"
              valor={status}
              aoMudar={definirStatus}
              opcoes={[
                { valor: '', texto: 'Todas as situações' },
                ...STATUS.map((s) => ({ valor: s, texto: ROTULO_STATUS[s] })),
              ]}
            />
            <FiltroSelect
              rotulo="Filtrar por severidade"
              valor={severidade}
              aoMudar={definirSeveridade}
              opcoes={[
                { valor: '', texto: 'Todas as severidades' },
                ...SEVERIDADES.map((s) => ({ valor: s, texto: ROTULO_SEVERIDADE[s] })),
              ]}
            />
          </>
        }
        rodape={
          <span>
            {total} {total === 1 ? 'inconsistência' : 'inconsistências'}
          </span>
        }
      />

      <PainelTratamento
        id={aberta}
        editavel={trata}
        meuId={usuario?.id ?? null}
        aoFechar={() => definirAberta(null)}
        aoMudar={carregar}
      />
    </>
  )
}

// ------------------------------------------------------------------ painel

function PainelTratamento({
  id,
  editavel,
  meuId,
  aoFechar,
  aoMudar,
}: {
  id: string | null
  editavel: boolean
  meuId: string | null
  aoFechar: () => void
  aoMudar: () => Promise<void>
}) {
  const [item, definirItem] = useState<Inconsistencia | null>(null)
  const [carregando, definirCarregando] = useState(false)
  const [erro, definirErro] = useState<string | null>(null)

  const carregar = useCallback(async (alvo: string, vivo: () => boolean) => {
    definirCarregando(true)
    definirErro(null)

    try {
      const resposta = await obterInconsistencia(alvo)

      if (vivo()) definirItem(resposta)
    } catch (falha) {
      if (vivo()) {
        definirErro(falha instanceof Error ? falha.message : 'Não foi possível abrir.')
      }
    } finally {
      if (vivo()) definirCarregando(false)
    }
  }, [])

  useEffect(() => {
    if (!id) return

    let vivo = true

    // O estado só muda DEPOIS do await, quando a resposta da API chega.
    // oxlint-disable-next-line react/set-state-in-effect
    void carregar(id, () => vivo)

    return () => {
      vivo = false
    }
  }, [id, carregar])

  const aplicar = async (acao: () => Promise<Inconsistencia>) => {
    definirErro(null)

    try {
      definirItem(await acao())
      await aoMudar()
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Não foi possível concluir.')
    }
  }

  return (
    <Drawer open={id !== null} onOpenChange={(aberto) => !aberto && aoFechar()}>
      <DrawerContent titulo="Tratamento da inconsistência" className="max-w-3xl">
        <div aria-live="polite" aria-busy={carregando} className="space-y-5">
          {carregando && <div className="h-32 animate-pulse rounded bg-muted" />}

          {!carregando && erro && (
            <Alert variant="destructive" role="alert">
              <AlertDescription>{erro}</AlertDescription>
            </Alert>
          )}

          {!carregando && item && (
            <>
              <Resumo item={item} />

              {editavel && <Assistente key={item.id} id={item.id} />}

              {editavel && <Acoes item={item} meuId={meuId} aoAplicar={aplicar} />}

              <LinhaDoTempo andamentos={item.andamentos ?? []} />
            </>
          )}
        </div>
      </DrawerContent>
    </Drawer>
  )
}

const MOTIVO: Record<Explicacao['situacao'], string> = {
  Respondeu: '',
  NaoConfigurada: 'O assistente não está configurado neste ambiente.',
  LimiteAtingido: 'Limite de explicações atingido. Tente de novo mais tarde.',
  Indisponivel: 'O assistente está indisponível no momento.',
  Recusada: 'O assistente não conseguiu explicar esta inconsistência.',
}

/**
 * O assistente de inconsistências (Fase 11).
 *
 * ## Ele explica, e não calcula
 *
 * `CLAUDE.md §37.3`: **se o valor entra numa conta, num holerite ou numa
 * obrigação, ele veio do C#.** O que aparece aqui é frase sobre números que o
 * motor determinístico já produziu — e nada nesta caixa alimenta cálculo algum.
 *
 * ## Por que o rótulo é obrigatório, e não decoração
 *
 * Sem ele, um texto de máquina fica visualmente indistinguível de apuração do
 * sistema, e quem lê meses depois não tem como saber a diferença. O aviso vem
 * do próprio backend, junto da resposta.
 *
 * ## Por que só carrega quando alguém pede
 *
 * Cada chamada custa token. Gerar ao abrir a gaveta pagaria por explicação que
 * ninguém leu.
 *
 * ## Texto do modelo é texto
 *
 * Renderizado como conteúdo, nunca como markup — mesma regra do comentário de
 * outro usuário (`§24.9`). Não há `dangerouslySetInnerHTML` no projeto.
 */
function Assistente({ id }: { id: string }) {
  const [disponivel, definirDisponivel] = useState<boolean | null>(null)
  const [explicacao, definirExplicacao] = useState<Explicacao | null>(null)
  const [pedindo, definirPedindo] = useState(false)
  const [falha, definirFalha] = useState<string | null>(null)

  // ⚠️ O `key={item.id}` na chamada remonta este componente quando a gaveta
  // troca de inconsistência. É de propósito: zerar o estado dentro do efeito
  // dispararia uma segunda renderização, e — pior — deixaria a explicação da
  // inconsistência anterior visível durante ela.
  useEffect(() => {
    let vivo = true

    assistenteDisponivel()
      .then((r) => vivo && definirDisponivel(r))
      .catch(() => vivo && definirDisponivel(false))

    return () => {
      vivo = false
    }
  }, [id])

  // Sem IA configurada a caixa não existe. O produto funciona igual.
  if (disponivel !== true) return null

  const pedir = async () => {
    definirPedindo(true)
    definirFalha(null)

    try {
      definirExplicacao(await explicarInconsistencia(id))
    } catch {
      definirFalha('Não foi possível falar com o assistente.')
    } finally {
      definirPedindo(false)
    }
  }

  return (
    <section aria-labelledby={`ia-${id}`} className="border-t border-border pt-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3
          id={`ia-${id}`}
          className="flex items-center gap-1.5 text-[13px] font-semibold text-foreground"
        >
          <Sparkles aria-hidden className="size-3.5 text-muted-foreground" />
          Assistente
        </h3>

        {!explicacao && (
          <Button size="sm" variant="outline" onClick={pedir} disabled={pedindo}>
            {pedindo ? 'Explicando…' : 'Explicar em linguagem simples'}
          </Button>
        )}
      </div>

      {falha && (
        <Alert variant="destructive" role="alert" className="mt-2">
          <AlertDescription>{falha}</AlertDescription>
        </Alert>
      )}

      {explicacao && explicacao.situacao !== 'Respondeu' && (
        <p className="mt-2 text-[13px] text-muted-foreground">{MOTIVO[explicacao.situacao]}</p>
      )}

      {explicacao?.geradoPorIa && (
        <div className="mt-2 rounded-md border border-border bg-muted/30 px-3 py-2">
          <p className="whitespace-pre-wrap text-[13px] text-foreground">{explicacao.texto}</p>

          {/* ⚠️ O rótulo é requisito, não enfeite (`CLAUDE.md §37.3`). */}
          <p className="mt-2 text-[11px] text-muted-foreground">{explicacao.aviso}</p>
        </div>
      )}
    </section>
  )
}

function Resumo({ item }: { item: Inconsistencia }) {
  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center gap-2">
        <StatusBadge tom={TOM_SEVERIDADE[item.severidade]}>
          {ROTULO_SEVERIDADE[item.severidade]}
        </StatusBadge>
        <StatusBadge tom={TOM_STATUS[item.status]}>{ROTULO_STATUS[item.status]}</StatusBadge>
        <span className="text-[13px] font-semibold text-foreground">{item.regra}</span>
        <span className="text-[11px] text-muted-foreground">v{item.versaoRegra}</span>
      </div>

      <p className="text-[13px] text-foreground/85">{item.descricao}</p>

      <p className="text-[12px] text-muted-foreground">{EXPLICACAO_STATUS[item.status]}</p>

      <ListaCampos colunas={3}>
        <Campo rotulo="Funcionário">
          {item.matricula ? `${item.matricula} — ${item.nomeFuncionario}` : '—'}
        </Campo>
        <Campo rotulo="Competência">{item.competencia || '—'}</Campo>
        <Campo rotulo="Responsável">{item.responsavel ?? 'Sem responsável'}</Campo>
        {item.valorEsperado !== null && (
          <Campo rotulo="Esperado">
            <Dinheiro valor={item.valorEsperado} />
          </Campo>
        )}
        {item.valorEncontrado !== null && (
          <Campo rotulo="Encontrado">
            <Dinheiro valor={item.valorEncontrado} />
          </Campo>
        )}
        {item.diferenca !== null && (
          <Campo rotulo="Diferença">
            <Dinheiro valor={item.diferenca} />
          </Campo>
        )}
      </ListaCampos>

      {item.justificativa && (
        <div className="rounded-md border border-border bg-muted/30 px-3 py-2">
          <p className="text-xs text-muted-foreground">Justificativa</p>
          <p className="mt-0.5 whitespace-pre-wrap text-[13px] text-foreground">
            {item.justificativa}
          </p>
        </div>
      )}
    </div>
  )
}

function Acoes({
  item,
  meuId,
  aoAplicar,
}: {
  item: Inconsistencia
  meuId: string | null
  aoAplicar: (acao: () => Promise<Inconsistencia>) => Promise<void>
}) {
  const idTexto = useId()

  const [destino, definirDestino] = useState<StatusInconsistencia | ''>('')
  const [texto, definirTexto] = useState('')
  const [enviando, definirEnviando] = useState(false)

  // Justificar sem escrever o motivo é só fechar a pendência com outro nome —
  // o backend recusa, e a tela avisa antes de deixar tentar.
  const exigeTexto = destino === 'Justificada'
  const podeEnviar = destino !== '' && (!exigeTexto || texto.trim().length > 0)

  const executar = async (acao: () => Promise<Inconsistencia>) => {
    definirEnviando(true)
    await aoAplicar(acao)
    definirEnviando(false)
  }

  return (
    <div className="space-y-3 border-t border-border pt-4">
      <div className="flex flex-wrap items-end gap-2">
        <div className="space-y-1">
          <Label htmlFor={`${idTexto}-status`} className="text-xs text-muted-foreground">
            Mudar para
          </Label>
          <select
            id={`${idTexto}-status`}
            value={destino}
            onChange={(e) => definirDestino(e.target.value as StatusInconsistencia | '')}
            className={cn(
              'h-8 rounded-md border border-input bg-card px-2 text-[13px] text-foreground shadow-xs',
              'focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2',
            )}
          >
            <option value="">Escolha…</option>
            {/* Vem do servidor: a tela não repete a máquina de estados. */}
            {item.proximosStatus.map((s) => (
              <option key={s} value={s}>
                {ROTULO_STATUS[s]}
              </option>
            ))}
          </select>
        </div>

        {destino !== '' && (
          <p className="pb-1.5 text-[12px] text-muted-foreground">
            {EXPLICACAO_STATUS[destino]}
          </p>
        )}
      </div>

      <div className="space-y-1">
        <Label htmlFor={idTexto} className="text-xs text-muted-foreground">
          {exigeTexto ? 'Motivo (obrigatório para justificar)' : 'Observação, comentário ou evidência'}
        </Label>
        <textarea
          id={idTexto}
          rows={3}
          value={texto}
          onChange={(e) => definirTexto(e.target.value)}
          maxLength={2000}
          className={cn(
            'w-full rounded-md border border-input bg-card px-2.5 py-2 text-[13px] text-foreground shadow-xs',
            'focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2',
          )}
        />
      </div>

      <div className="flex flex-wrap gap-2">
        <Button
          type="button"
          size="sm"
          disabled={!podeEnviar || enviando}
          onClick={() =>
            void executar(async () => {
              const r = await transitar(item.id, destino as StatusInconsistencia, texto || undefined)
              definirDestino('')
              definirTexto('')
              return r
            })
          }
        >
          {enviando ? 'Salvando...' : 'Mudar situação'}
        </Button>

        <Button
          type="button"
          size="sm"
          variant="outline"
          disabled={texto.trim().length === 0 || enviando}
          onClick={() =>
            void executar(async () => {
              const r = await comentar(item.id, texto)
              definirTexto('')
              return r
            })
          }
        >
          <MessageSquare aria-hidden />
          Comentar
        </Button>

        <Button
          type="button"
          size="sm"
          variant="outline"
          disabled={texto.trim().length === 0 || enviando}
          onClick={() =>
            void executar(async () => {
              const r = await registrarEvidencia(item.id, texto)
              definirTexto('')
              return r
            })
          }
        >
          <Paperclip aria-hidden />
          Registrar evidência
        </Button>

        {item.idResponsavel === null ? (
          <Button
            type="button"
            size="sm"
            variant="outline"
            disabled={enviando || meuId === null}
            onClick={() => void executar(() => atribuir(item.id, meuId!))}
          >
            <UserRound aria-hidden />
            Assumir
          </Button>
        ) : (
          <Button
            type="button"
            size="sm"
            variant="outline"
            disabled={enviando}
            onClick={() => void executar(() => atribuir(item.id, null))}
          >
            <UserRound aria-hidden />
            Liberar
          </Button>
        )}
      </div>

      <p className="text-[11.5px] text-muted-foreground">
        Evidência aqui é <strong>texto</strong>: o que foi conferido e onde a prova está. Anexar
        arquivo depende do armazenamento da Fase 9.
      </p>
    </div>
  )
}

function LinhaDoTempo({ andamentos }: { andamentos: Andamento[] }) {
  if (andamentos.length === 0) {
    return (
      <p className="border-t border-border pt-4 text-[13px] text-muted-foreground">
        Nada aconteceu com esta inconsistência ainda.
      </p>
    )
  }

  return (
    <div className="border-t border-border pt-4">
      <p className="mb-2 text-[13px] font-medium text-foreground">Histórico</p>

      <ul className="space-y-3">
        {andamentos.map((a) => (
          <li key={a.id} className="border-l-2 border-border pl-3">
            <p className="flex flex-wrap items-center gap-x-2 text-[12px] text-muted-foreground">
              <span className="font-medium text-foreground">{ROTULO_ANDAMENTO[a.tipo]}</span>
              <span>{a.autor ?? 'sistema'}</span>
              <span className="tabular">{new Date(a.ocorridoEm).toLocaleString('pt-BR')}</span>
            </p>

            {a.tipo === 'Transicao' && a.statusNovo && (
              <p className="mt-0.5 text-[13px]">
                {a.statusAnterior ? ROTULO_STATUS[a.statusAnterior] : '—'} →{' '}
                <span className="font-medium">{ROTULO_STATUS[a.statusNovo]}</span>
              </p>
            )}

            {a.tipo === 'Atribuicao' && (
              <p className="mt-0.5 text-[13px]">
                {a.responsavelAnterior ?? 'sem responsável'} →{' '}
                <span className="font-medium">{a.responsavelNovo ?? 'sem responsável'}</span>
              </p>
            )}

            {/* Texto de outro usuário, renderizado como TEXTO. */}
            {a.texto && (
              <p className="mt-0.5 whitespace-pre-wrap text-[13px] text-foreground/85">{a.texto}</p>
            )}
          </li>
        ))}
      </ul>
    </div>
  )
}
