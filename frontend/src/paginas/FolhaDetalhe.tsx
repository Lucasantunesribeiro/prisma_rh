import { Calculator, Lock, Plus, Trash2 } from 'lucide-react'
import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { useParams } from 'react-router'
import {
  calcularFolha,
  competenciaPorExtenso,
  fecharFolha,
  lancar,
  listarRubricas,
  obterFolha,
  obterHolerite,
  podeProcessarFolha,
  removerLancamento,
  ROTULO_BASE,
  ROTULO_SITUACAO_FOLHA,
  type BaseApurada,
  type FolhaDetalhe as Detalhe,
  type Holerite,
  type HoleriteResumo,
  type Lancamento,
  type Rubrica,
  type SituacaoFolha,
} from '@/api/folha'
import { useSessao } from '@/auth/useSessao'
import { DataTable, type Coluna } from '@/components/sistema/DataTable'
import { EstadoCarregando, EstadoErro } from '@/components/sistema/Estados'
import {
  CabecalhoPagina,
  CabecalhoSecao,
  Dinheiro,
  ResumoFinanceiro,
  StatusBadge,
} from '@/components/sistema/Primitivos'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Dialog, DialogClose, DialogContent, DialogTrigger } from '@/components/ui/dialog'
import { Drawer, DrawerClose, DrawerContent, DrawerTrigger } from '@/components/ui/drawer'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { ResumoDaFolha } from '@/paginas/ResumoDaFolha'
import { SecaoAnalise } from '@/paginas/SecaoAnalise'
import { usePagina } from '@/layout/usePagina'
import { cn } from '@/lib/utils'

const TOM_SITUACAO: Record<SituacaoFolha, 'neutro' | 'info' | 'sucesso'> = {
  Rascunho: 'neutro',
  Calculada: 'info',
  Fechada: 'sucesso',
}

export default function FolhaDetalhe() {
  const { id } = useParams<{ id: string }>()
  const { usuario } = useSessao()
  const processa = podeProcessarFolha(usuario?.perfil)

  const [detalhe, definirDetalhe] = useState<Detalhe | null>(null)
  const [rubricas, definirRubricas] = useState<Rubrica[]>([])
  const [carregando, definirCarregando] = useState(true)
  const [erro, definirErro] = useState<string | null>(null)
  const [acao, definirAcao] = useState<string | null>(null)
  const [erroAcao, definirErroAcao] = useState<string | null>(null)
  const [aberto, definirAberto] = useState<HoleriteResumo | null>(null)

  usePagina(
    [
      { texto: 'Folha' },
      { texto: 'Folhas', para: '/folhas' },
      { texto: detalhe ? detalhe.folha.competencia : 'Carregando' },
    ],
    detalhe ? competenciaPorExtenso(detalhe.folha.competencia) : null,
  )

  const carregar = useCallback(async () => {
    if (!id) return

    definirErro(null)

    try {
      const [novo, catalogo] = await Promise.all([obterFolha(id), listarRubricas(true)])
      definirDetalhe(novo)
      definirRubricas(catalogo)
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Não foi possível carregar a folha.')
    } finally {
      definirCarregando(false)
    }
  }, [id])

  useEffect(() => {
    // O estado só muda DEPOIS do await, quando a resposta da API chega.
    // oxlint-disable-next-line react/set-state-in-effect
    void carregar()
  }, [carregar])

  const executar = async (nome: string, operacao: () => Promise<unknown>) => {
    definirAcao(nome)
    definirErroAcao(null)

    try {
      await operacao()
      await carregar()
    } catch (falha) {
      definirErroAcao(falha instanceof Error ? falha.message : 'Não foi possível concluir a ação.')
    } finally {
      definirAcao(null)
    }
  }

  if (carregando) return <EstadoCarregando rotulo="Carregando a folha" />

  if (erro || !detalhe || !id) {
    return (
      <EstadoErro
        mensagem={erro ?? 'Folha não encontrada.'}
        aoTentarNovamente={() => void carregar()}
      />
    )
  }

  const { folha } = detalhe
  const fechada = folha.situacao === 'Fechada'
  const editavel = processa && !fechada

  const colunas: Coluna<HoleriteResumo>[] = [
    {
      cabecalho: 'Funcionário',
      largura: '30%',
      celula: (h) => (
        <div className="min-w-0">
          <span className="block truncate font-medium text-foreground">{h.funcionario}</span>
          <span className="tabular block text-xs text-muted-foreground">
            Matrícula {h.matricula}
          </span>
        </div>
      ),
    },
    {
      cabecalho: 'Avos',
      numerica: true,
      largura: '90px',
      celula: (h) => (
        <span className="tabular text-muted-foreground">
          {h.avos}/{h.divisor}
        </span>
      ),
    },
    {
      cabecalho: 'Salário base',
      numerica: true,
      secundaria: true,
      celula: (h) => <Dinheiro valor={h.salarioReferencia} className="text-muted-foreground" />,
    },
    {
      cabecalho: 'Proventos',
      numerica: true,
      celula: (h) => <Dinheiro valor={h.totalProventos} />,
    },
    {
      cabecalho: 'Descontos',
      numerica: true,
      celula: (h) =>
        h.totalDescontos > 0 ? (
          <Dinheiro valor={h.totalDescontos} sinal="desconto" />
        ) : (
          <span className="text-muted-foreground">—</span>
        ),
    },
    {
      cabecalho: 'Líquido',
      numerica: true,
      celula: (h) => <Dinheiro valor={h.liquido} enfase />,
    },
  ]

  return (
    <>
      <CabecalhoPagina
        titulo={`Folha mensal · ${competenciaPorExtenso(folha.competencia)}`}
        descricao={folha.empresa}
        meta={
          <div className="flex flex-wrap items-center gap-2 pt-0.5">
            <StatusBadge tom={TOM_SITUACAO[folha.situacao]}>
              {ROTULO_SITUACAO_FOLHA[folha.situacao]}
            </StatusBadge>
            {folha.versaoCalculo > 0 && (
              <span className="text-xs text-muted-foreground">
                {folha.versaoCalculo}ª versão do cálculo
              </span>
            )}
          </div>
        }
        acao={
          processa &&
          !fechada && (
            <>
              <Button
                size="sm"
                variant="outline"
                disabled={acao !== null}
                onClick={() => void executar('calcular', () => calcularFolha(id))}
              >
                <Calculator aria-hidden />
                {acao === 'calcular'
                  ? 'Calculando...'
                  : folha.versaoCalculo > 0
                    ? 'Recalcular'
                    : 'Calcular'}
              </Button>

              <FecharFolha
                habilitado={acao === null && folha.situacao === 'Calculada'}
                aoConfirmar={() => executar('fechar', () => fecharFolha(id))}
              />
            </>
          )
        }
      />

      {erroAcao && (
        <Alert variant="destructive" role="alert" className="mb-5">
          <AlertDescription>{erroAcao}</AlertDescription>
        </Alert>
      )}

      {/*
       * Faixa tipográfica, e não uma grade de KPI cards: quatro números com
       * divisores dizem o mesmo que quatro caixas com sombra, ocupando um
       * terço do espaço e sem sugerir que cada número é um painel próprio.
       */}
      <ResumoFinanceiro
        className="mb-6"
        itens={[
          { rotulo: 'Funcionários', valor: <span>{folha.quantidadeFuncionarios}</span> },
          { rotulo: 'Proventos', valor: <Dinheiro valor={folha.totalProventos} /> },
          {
            rotulo: 'Descontos',
            valor: <Dinheiro valor={folha.totalDescontos} sinal="desconto" />,
          },
          { rotulo: 'Líquido', valor: <Dinheiro valor={folha.totalLiquido} />, enfase: true },
        ]}
      />

      <DataTable
        rotulo={`Holerites da folha de ${folha.competencia}`}
        colunas={colunas}
        itens={detalhe.funcionarios}
        chave={(h) => h.id}
        aoClicarLinha={(h) => definirAberto(h)}
        vazio={{
          titulo: 'Nenhum funcionário na folha',
          descricao: `Quem teve vínculo em qualquer dia de ${competenciaPorExtenso(
            folha.competencia,
          )} entra ao calcular.`,
        }}
        rodape={
          !fechada && folha.versaoCalculo > 0 ? (
            <span className="text-xs">
              Recalcular refaz o salário e o INSS e mantém os lançamentos digitados. Fechar é
              definitivo: não há reabertura nesta versão.
            </span>
          ) : undefined
        }
      />

      <ResumoDaFolha idFolha={id} />

      <SecaoAnalise idFolha={id} situacao={folha.situacao} podeExecutar={processa} />

      {aberto && (
        <PainelHolerite
          idFolha={id}
          resumo={aberto}
          rubricas={rubricas}
          editavel={editavel}
          competencia={folha.competencia}
          aoFechar={() => definirAberto(null)}
          aoMudar={carregar}
        />
      )}
    </>
  )
}

function FecharFolha({
  habilitado,
  aoConfirmar,
}: {
  habilitado: boolean
  aoConfirmar: () => Promise<void>
}) {
  const [aberto, definirAberto] = useState(false)

  return (
    <Dialog open={aberto} onOpenChange={definirAberto}>
      <DialogTrigger asChild>
        <Button size="sm" disabled={!habilitado}>
          <Lock aria-hidden />
          Fechar folha
        </Button>
      </DialogTrigger>

      {/* Confirmação porque a ação é irreversível — não há reabertura. */}
      <DialogContent
        titulo="Fechar a folha?"
        descricao="Depois de fechada, a folha não aceita cálculo, lançamento nem reabertura. É um fato histórico."
      >
        <DialogClose asChild>
          <Button variant="outline" size="sm">
            Cancelar
          </Button>
        </DialogClose>
        <Button
          size="sm"
          onClick={() => {
            definirAberto(false)
            void aoConfirmar()
          }}
        >
          Fechar folha
        </Button>
      </DialogContent>
    </Dialog>
  )
}

// ---------------------------------------------------------------- holerite

function PainelHolerite({
  idFolha,
  resumo,
  rubricas,
  editavel,
  competencia,
  aoFechar,
  aoMudar,
}: {
  idFolha: string
  resumo: HoleriteResumo
  rubricas: Rubrica[]
  editavel: boolean
  competencia: string
  aoFechar: () => void
  aoMudar: () => Promise<void>
}) {
  const [holerite, definirHolerite] = useState<Holerite | null>(null)
  const [erro, definirErro] = useState<string | null>(null)
  const [memoria, definirMemoria] = useState<Lancamento | null>(null)

  const informativos = holerite?.lancamentos.filter((l) => l.tipo === 'Informativo') ?? []
  const temInformativo = informativos.length > 0

  /*
   * Somado aqui e não no backend porque não é um total do documento: o
   * holerite tem proventos, descontos e líquido, e o informativo fica de fora
   * dos três de propósito. O arredondamento explícito existe porque somar
   * números decimais em JavaScript produz 240.00000000000003.
   */
  const totalInformativo = Math.round(informativos.reduce((s, l) => s + l.valor, 0) * 100) / 100

  const carregar = useCallback(async () => {
    definirErro(null)

    try {
      definirHolerite(await obterHolerite(idFolha, resumo.id))
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Não foi possível carregar o holerite.')
    }
  }, [idFolha, resumo.id])

  useEffect(() => {
    // O estado só muda DEPOIS do await, quando a resposta da API chega.
    // oxlint-disable-next-line react/set-state-in-effect
    void carregar()
  }, [carregar])

  const apagar = async (idLancamento: string) => {
    try {
      await removerLancamento(idFolha, resumo.id, idLancamento)
      await carregar()
      await aoMudar()
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Não foi possível remover o lançamento.')
    }
  }

  const manuais = rubricas.filter((r) => r.estrategia === 'ValorInformado')

  return (
    <Drawer open onOpenChange={(estado) => !estado && aoFechar()}>
      <DrawerContent
        titulo={resumo.funcionario}
        descricao={
          <>
            Folha mensal · {competenciaPorExtenso(competencia)} · Matrícula{' '}
            <span className="tabular">{resumo.matricula}</span>
          </>
        }
        className="max-w-3xl"
      >
        {erro && (
          <Alert variant="destructive" role="alert" className="mb-4">
            <AlertDescription>{erro}</AlertDescription>
          </Alert>
        )}

        {!holerite && !erro && <EstadoCarregando rotulo="Carregando holerite" />}

        {holerite && (
          <div className="space-y-7">
            <section>
              <div className="mb-3 flex items-center justify-between gap-4">
                <h3 className="text-[15px] font-semibold tracking-tight">Lançamentos</h3>
                {editavel && manuais.length > 0 && (
                  <NovoLancamento
                    idFolha={idFolha}
                    idHolerite={resumo.id}
                    rubricas={manuais}
                    aoLancar={async () => {
                      await carregar()
                      await aoMudar()
                    }}
                  />
                )}
              </div>

              {/*
               * O holerite é tratado como documento financeiro: código à
               * esquerda, referência no meio, proventos e descontos em duas
               * colunas alinhadas à direita, totais no rodapé.
               *
               * A coluna de informativos só existe quando há algum. Ela é
               * separada de propósito: FGTS é depósito do empregador, e
               * mostrá-lo entre os proventos sugeriria que o funcionário
               * recebeu aquele valor.
               */}
              <table className="w-full border-collapse text-[13px]">
                <caption className="sr-only">Lançamentos do holerite</caption>
                <thead>
                  <tr className="border-y border-border bg-muted/40 text-xs text-muted-foreground">
                    <th scope="col" className="w-16 px-3 py-2 text-left font-medium">
                      Cód.
                    </th>
                    <th scope="col" className="px-3 py-2 text-left font-medium">
                      Rubrica
                    </th>
                    <th scope="col" className="w-24 px-3 py-2 text-left font-medium">
                      Ref.
                    </th>
                    <th scope="col" className="w-32 px-3 py-2 text-right font-medium">
                      Proventos
                    </th>
                    <th scope="col" className="w-32 px-3 py-2 text-right font-medium">
                      Descontos
                    </th>
                    {temInformativo && (
                      <th scope="col" className="w-32 px-3 py-2 text-right font-medium">
                        Informativo
                      </th>
                    )}
                    <th scope="col" className="w-8 px-1 py-2">
                      <span className="sr-only">Ações</span>
                    </th>
                  </tr>
                </thead>

                <tbody className="divide-y divide-border">
                  {holerite.lancamentos.map((l) => {
                    const calculado = l.origem === 'Calculado'

                    return (
                      <tr key={l.id} className="hover:bg-muted/30">
                        <td className="tabular px-3 py-2 align-middle text-muted-foreground">
                          {l.codigoRubrica}
                        </td>
                        <td className="px-3 py-2 align-middle">
                          {calculado ? (
                            <button
                              type="button"
                              onClick={() => definirMemoria(l)}
                              className="text-left underline decoration-dotted underline-offset-4 hover:text-primary"
                            >
                              {l.nomeRubrica}
                            </button>
                          ) : (
                            <span>
                              {l.nomeRubrica}
                              <span className="ml-2 text-xs text-muted-foreground">manual</span>
                            </span>
                          )}
                        </td>
                        <td className="tabular px-3 py-2 align-middle text-xs text-muted-foreground">
                          {l.referencia ?? ''}
                        </td>
                        <td className="px-3 py-2 text-right align-middle">
                          {l.tipo === 'Provento' ? <Dinheiro valor={l.valor} /> : ''}
                        </td>
                        <td className="px-3 py-2 text-right align-middle">
                          {l.tipo === 'Desconto' ? <Dinheiro valor={l.valor} /> : ''}
                        </td>
                        {temInformativo && (
                          <td className="px-3 py-2 text-right align-middle text-muted-foreground">
                            {l.tipo === 'Informativo' ? <Dinheiro valor={l.valor} /> : ''}
                          </td>
                        )}
                        <td className="px-1 py-2 align-middle">
                          {editavel && l.origem === 'Manual' && (
                            <Button
                              variant="ghost"
                              size="sm"
                              aria-label={`Remover ${l.nomeRubrica}`}
                              className="size-7 p-0"
                              onClick={() => void apagar(l.id)}
                            >
                              <Trash2 className="size-3.5" aria-hidden />
                            </Button>
                          )}
                        </td>
                      </tr>
                    )
                  })}
                </tbody>

                <tfoot className="border-t-2 border-border">
                  <tr>
                    <td colSpan={3} className="px-3 py-2 text-right text-muted-foreground">
                      Totais
                    </td>
                    <td className="px-3 py-2 text-right">
                      <Dinheiro valor={holerite.resumo.totalProventos} />
                    </td>
                    <td className="px-3 py-2 text-right">
                      <Dinheiro valor={holerite.resumo.totalDescontos} />
                    </td>
                    {temInformativo && (
                      <td className="px-3 py-2 text-right text-muted-foreground">
                        <Dinheiro valor={totalInformativo} />
                      </td>
                    )}
                    <td />
                  </tr>
                  <tr>
                    <td colSpan={4} className="px-3 pb-1 pt-2 text-right font-medium">
                      Líquido
                    </td>
                    <td className="px-3 pb-1 pt-2 text-right text-[15px]">
                      <Dinheiro valor={holerite.resumo.liquido} enfase />
                    </td>
                    {temInformativo && <td />}
                    <td />
                  </tr>
                </tfoot>
              </table>

              <p className="mt-2 text-xs text-muted-foreground">
                Rubricas sublinhadas são calculadas pelo sistema. Clique para ver a memória de
                cálculo.
                {temInformativo && (
                  <> Valores informativos, como o FGTS, são obrigação do empregador: não entram
                  no líquido nem saem do salário.</>
                )}
              </p>
            </section>

            <BasesDeCalculo bases={holerite.bases} />
          </div>
        )}

        {memoria && <DrawerMemoria lancamento={memoria} aoFechar={() => definirMemoria(null)} />}
      </DrawerContent>
    </Drawer>
  )
}

/**
 * A memória num drawer sobre o holerite, e não numa parede permanente ao lado.
 *
 * A conta de uma rubrica é informação de apoio: interessa quando alguém
 * questiona aquele número específico. Mostrá-la sempre, para todas as
 * rubricas, transforma o holerite num muro de dígitos em que o valor que
 * importa deixa de saltar.
 */
function DrawerMemoria({
  lancamento,
  aoFechar,
}: {
  lancamento: Lancamento
  aoFechar: () => void
}) {
  return (
    <Drawer open onOpenChange={(estado) => !estado && aoFechar()}>
      <DrawerContent
        titulo="Memória de cálculo"
        descricao={`${lancamento.codigoRubrica} · ${lancamento.nomeRubrica}`}
        className="max-w-lg"
      >
        <table className="w-full border-collapse text-[13px]">
          <caption className="sr-only">Passos do cálculo de {lancamento.nomeRubrica}</caption>
          <thead>
            <tr className="border-y border-border bg-muted/40 text-xs text-muted-foreground">
              <th scope="col" className="px-3 py-2 text-left font-medium">
                Etapa
              </th>
              <th scope="col" className="px-3 py-2 text-left font-medium">
                Conta
              </th>
              <th scope="col" className="w-28 px-3 py-2 text-right font-medium">
                Valor
              </th>
            </tr>
          </thead>

          <tbody className="divide-y divide-border">
            {lancamento.memoria.map((linha, indice) => {
              const ultima = indice === lancamento.memoria.length - 1

              return (
                <tr key={linha.ordem} className={cn(ultima && 'bg-muted/30')}>
                  <td className={cn('px-3 py-2 align-top', ultima && 'font-medium')}>
                    {linha.descricao}
                  </td>
                  <td className="tabular px-3 py-2 align-top text-xs text-muted-foreground">
                    {linha.expressao}
                  </td>
                  <td className="px-3 py-2 text-right align-top">
                    <Dinheiro valor={linha.valor} enfase={ultima} />
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>

        <p className="mt-4 text-xs text-muted-foreground">
          Valores congelados no momento do cálculo. Alterar a rubrica depois não altera este
          holerite.
        </p>
      </DrawerContent>
    </Drawer>
  )
}

function BasesDeCalculo({ bases }: { bases: BaseApurada[] }) {
  if (!bases || bases.length === 0) return null

  return (
    <section>
      <CabecalhoSecao
        titulo="Bases de cálculo"
        descricao="INSS, FGTS e IRRF não incidem sobre o total: cada um tem sua base."
      />

      <table className="w-full border-collapse text-[13px]">
        <caption className="sr-only">Bases de cálculo apuradas no holerite</caption>
        <thead>
          <tr className="border-b border-border text-xs text-muted-foreground">
            <th scope="col" className="w-24 py-2 text-left font-medium">
              Base
            </th>
            <th scope="col" className="py-2 text-left font-medium">
              Composta por
            </th>
            <th scope="col" className="w-32 py-2 text-right font-medium">
              Valor
            </th>
          </tr>
        </thead>

        <tbody className="divide-y divide-border">
          {bases.map((base) => (
            <tr key={base.base}>
              <td className="py-2 align-middle font-medium">{ROTULO_BASE[base.base]}</td>
              <td className="py-2 align-middle text-xs text-muted-foreground">
                {base.composta.length === 0 ? (
                  'nenhuma rubrica incide'
                ) : (
                  <span className="tabular">{base.composta.join(' + ')}</span>
                )}
              </td>
              <td className="py-2 text-right align-middle">
                <Dinheiro valor={base.valor} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  )
}

function NovoLancamento({
  idFolha,
  idHolerite,
  rubricas,
  aoLancar,
}: {
  idFolha: string
  idHolerite: string
  rubricas: Rubrica[]
  aoLancar: () => Promise<void>
}) {
  const [aberto, definirAberto] = useState(false)
  const [idRubrica, definirIdRubrica] = useState(rubricas[0]?.id ?? '')
  const [valor, definirValor] = useState('')
  const [referencia, definirReferencia] = useState('')
  const [erro, definirErro] = useState<string | null>(null)
  const [enviando, definirEnviando] = useState(false)

  const aoEnviar = async (evento: FormEvent) => {
    evento.preventDefault()
    definirErro(null)
    definirEnviando(true)

    try {
      await lancar(idFolha, idHolerite, {
        idRubrica,
        valor: Number(valor.replace(',', '.')),
        referencia: referencia || null,
      })
      definirValor('')
      definirReferencia('')
      definirAberto(false)
      await aoLancar()
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Não foi possível lançar.')
    } finally {
      definirEnviando(false)
    }
  }

  return (
    <Drawer open={aberto} onOpenChange={definirAberto}>
      <DrawerTrigger asChild>
        <Button variant="outline" size="sm">
          <Plus aria-hidden />
          Lançar
        </Button>
      </DrawerTrigger>

      <DrawerContent
        titulo="Novo lançamento"
        descricao="Provento ou desconto digitado. Ele sobrevive ao recálculo da folha."
        className="max-w-md"
      >
        <form onSubmit={aoEnviar} className="space-y-4" noValidate>
          <div className="space-y-1.5">
            <Label htmlFor="rubricaLancamento">Rubrica</Label>
            <select
              id="rubricaLancamento"
              value={idRubrica}
              onChange={(e) => definirIdRubrica(e.target.value)}
              className="h-9 w-full rounded-md border border-input bg-card px-3 text-[13px] shadow-xs"
            >
              {rubricas.map((r) => (
                <option key={r.id} value={r.id}>
                  {r.codigo} — {r.nome}
                </option>
              ))}
            </select>
          </div>

          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor="valorLancamento">Valor</Label>
              <Input
                id="valorLancamento"
                required
                inputMode="decimal"
                placeholder="0,00"
                value={valor}
                onChange={(e) => definirValor(e.target.value)}
                className="tabular"
              />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="refLancamento">Referência</Label>
              <Input
                id="refLancamento"
                placeholder="22 dias"
                value={referencia}
                onChange={(e) => definirReferencia(e.target.value)}
              />
            </div>
          </div>

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
              {enviando ? 'Lançando...' : 'Lançar'}
            </Button>
          </div>
        </form>
      </DrawerContent>
    </Drawer>
  )
}
