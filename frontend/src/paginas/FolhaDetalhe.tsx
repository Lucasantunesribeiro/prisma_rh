import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
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
  ROTULO_SITUACAO_FOLHA,
  type FolhaDetalhe as Detalhe,
  type Holerite,
  type Rubrica,
} from '@/api/folha'
import { formatarSalario } from '@/api/pessoas'
import { useSessao } from '@/auth/useSessao'

type Estado =
  | { situacao: 'carregando' }
  | { situacao: 'pronto'; detalhe: Detalhe; rubricas: Rubrica[] }
  | { situacao: 'erro'; mensagem: string }

export default function FolhaDetalhe() {
  const { id } = useParams<{ id: string }>()
  const { usuario } = useSessao()
  const processa = podeProcessarFolha(usuario?.perfil)

  const [estado, definirEstado] = useState<Estado>({ situacao: 'carregando' })
  const [aberto, definirAberto] = useState<string | null>(null)
  const [acao, definirAcao] = useState<string | null>(null)
  const [erroAcao, definirErroAcao] = useState<string | null>(null)

  const carregar = useCallback(async () => {
    if (!id) return

    try {
      const [detalhe, rubricas] = await Promise.all([obterFolha(id), listarRubricas(true)])
      definirEstado({ situacao: 'pronto', detalhe, rubricas })
    } catch (falha) {
      definirEstado({
        situacao: 'erro',
        mensagem: falha instanceof Error ? falha.message : 'Falha ao carregar a folha.',
      })
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
      definirErroAcao(falha instanceof Error ? falha.message : 'Falha na operação.')
    } finally {
      definirAcao(null)
    }
  }

  return (
    <main className="mx-auto w-full max-w-5xl px-6 py-8">
      <Button asChild variant="ghost" size="sm" className="mb-4 -ml-2">
        <Link to="/folhas">← Folhas</Link>
      </Button>

      <div aria-live="polite" aria-busy={estado.situacao === 'carregando'}>
        {estado.situacao === 'carregando' && (
          <p className="text-sm text-muted-foreground">Carregando...</p>
        )}

        {estado.situacao === 'erro' && (
          <div>
            <Alert variant="destructive" role="alert">
              <AlertDescription>{estado.mensagem}</AlertDescription>
            </Alert>
            <Button
              className="mt-4"
              variant="outline"
              size="sm"
              onClick={() => {
                definirEstado({ situacao: 'carregando' })
                void carregar()
              }}
            >
              Tentar novamente
            </Button>
          </div>
        )}

        {estado.situacao === 'pronto' && id && (
          <>
            <Cabecalho
              detalhe={estado.detalhe}
              processa={processa}
              acao={acao}
              aoCalcular={() => executar('calcular', () => calcularFolha(id))}
              aoFechar={() => executar('fechar', () => fecharFolha(id))}
            />

            {erroAcao && (
              <Alert variant="destructive" role="alert" className="mb-6">
                <AlertDescription>{erroAcao}</AlertDescription>
              </Alert>
            )}

            <Card>
              <CardHeader>
                <CardTitle className="text-base">
                  {estado.detalhe.funcionarios.length} funcionário(s) na folha
                </CardTitle>
              </CardHeader>
              <CardContent>
                {estado.detalhe.funcionarios.length === 0 ? (
                  <p className="py-2 text-sm text-muted-foreground">
                    Nenhum funcionário ainda. Quem teve vínculo em qualquer dia de{' '}
                    {competenciaPorExtenso(estado.detalhe.folha.competencia)} entra ao calcular.
                  </p>
                ) : (
                  <Table>
                    <TableHeader>
                      <TableRow>
                        <TableHead>Funcionário</TableHead>
                        <TableHead>Matrícula</TableHead>
                        <TableHead className="text-right">Avos</TableHead>
                        <TableHead className="text-right">Proventos</TableHead>
                        <TableHead className="text-right">Descontos</TableHead>
                        <TableHead className="text-right">Líquido</TableHead>
                        <TableHead className="text-right">Memória</TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {estado.detalhe.funcionarios.map((holerite) => (
                        <TableRow key={holerite.id}>
                          <TableCell className="font-medium">{holerite.funcionario}</TableCell>
                          <TableCell className="font-mono text-xs">{holerite.matricula}</TableCell>
                          <TableCell className="text-right">
                            {holerite.avos}/{holerite.divisor}
                          </TableCell>
                          <TableCell className="text-right">
                            {formatarSalario(holerite.totalProventos)}
                          </TableCell>
                          <TableCell className="text-right">
                            {holerite.totalDescontos > 0
                              ? `− ${formatarSalario(holerite.totalDescontos)}`
                              : '—'}
                          </TableCell>
                          <TableCell className="text-right font-medium">
                            {formatarSalario(holerite.liquido)}
                          </TableCell>
                          <TableCell className="text-right">
                            <Button
                              variant="ghost"
                              size="sm"
                              onClick={() =>
                                definirAberto(aberto === holerite.id ? null : holerite.id)
                              }
                            >
                              {aberto === holerite.id ? 'Fechar' : 'Ver'}
                            </Button>
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                )}
              </CardContent>
            </Card>

            {aberto && (
              <PainelHolerite
                idFolha={id}
                idHolerite={aberto}
                rubricas={estado.rubricas}
                editavel={processa && estado.detalhe.folha.situacao !== 'Fechada'}
                aoMudar={carregar}
              />
            )}
          </>
        )}
      </div>
    </main>
  )
}

function Cabecalho({
  detalhe,
  processa,
  acao,
  aoCalcular,
  aoFechar,
}: {
  detalhe: Detalhe
  processa: boolean
  acao: string | null
  aoCalcular: () => Promise<void>
  aoFechar: () => Promise<void>
}) {
  const { folha } = detalhe
  const fechada = folha.situacao === 'Fechada'

  return (
    <header className="mb-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">
            {folha.empresa} · {folha.competencia}
          </h1>
          <p className="mt-1 text-sm text-muted-foreground">
            {competenciaPorExtenso(folha.competencia)}
            {folha.versaoCalculo > 0 && ` · calculada ${folha.versaoCalculo}x`}
          </p>
        </div>

        <div className="flex items-center gap-2">
          <Badge variant={fechada ? 'secondary' : 'default'}>
            {ROTULO_SITUACAO_FOLHA[folha.situacao]}
          </Badge>

          {processa && !fechada && (
            <>
              <Button size="sm" disabled={acao !== null} onClick={() => void aoCalcular()}>
                {acao === 'calcular'
                  ? 'Calculando...'
                  : folha.versaoCalculo > 0
                    ? 'Recalcular'
                    : 'Calcular'}
              </Button>
              <Button
                size="sm"
                variant="outline"
                disabled={acao !== null || folha.situacao !== 'Calculada'}
                onClick={() => void aoFechar()}
              >
                {acao === 'fechar' ? 'Fechando...' : 'Fechar folha'}
              </Button>
            </>
          )}
        </div>
      </div>

      <div className="mt-4 grid gap-3 sm:grid-cols-3">
        <Totalizador rotulo="Proventos" valor={folha.totalProventos} />
        <Totalizador rotulo="Descontos" valor={folha.totalDescontos} />
        <Totalizador rotulo="Líquido" valor={folha.totalLiquido} destaque />
      </div>

      {!fechada && folha.versaoCalculo > 0 && (
        <p className="mt-3 text-xs text-muted-foreground">
          Recalcular refaz o salário e mantém os lançamentos digitados. Fechar é definitivo:
          não há reabertura nesta versão.
        </p>
      )}
    </header>
  )
}

function Totalizador({
  rotulo,
  valor,
  destaque = false,
}: {
  rotulo: string
  valor: number
  destaque?: boolean
}) {
  return (
    <Card>
      <CardContent className="pt-6">
        <p className="text-xs text-muted-foreground">{rotulo}</p>
        <p className={`mt-1 ${destaque ? 'text-lg font-semibold' : 'text-base font-medium'}`}>
          {formatarSalario(valor)}
        </p>
      </CardContent>
    </Card>
  )
}

function PainelHolerite({
  idFolha,
  idHolerite,
  rubricas,
  editavel,
  aoMudar,
}: {
  idFolha: string
  idHolerite: string
  rubricas: Rubrica[]
  editavel: boolean
  aoMudar: () => Promise<void>
}) {
  const [holerite, definirHolerite] = useState<Holerite | null>(null)
  const [erro, definirErro] = useState<string | null>(null)

  const carregar = useCallback(async () => {
    definirErro(null)

    try {
      definirHolerite(await obterHolerite(idFolha, idHolerite))
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Falha ao carregar o holerite.')
    }
  }, [idFolha, idHolerite])

  useEffect(() => {
    // O estado só muda DEPOIS do await, quando a resposta da API chega.
    // oxlint-disable-next-line react/set-state-in-effect
    void carregar()
  }, [carregar])

  const apagar = async (idLancamento: string) => {
    try {
      await removerLancamento(idFolha, idHolerite, idLancamento)
      await carregar()
      await aoMudar()
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Falha ao remover o lançamento.')
    }
  }

  if (erro) {
    return (
      <Alert variant="destructive" role="alert" className="mt-6">
        <AlertDescription>{erro}</AlertDescription>
      </Alert>
    )
  }

  if (!holerite) {
    return <p className="mt-6 text-sm text-muted-foreground">Carregando holerite...</p>
  }

  const manuais = rubricas.filter((r) => r.estrategia === 'ValorInformado')

  return (
    <Card className="mt-6">
      <CardHeader>
        <CardTitle className="text-base">
          {holerite.resumo.funcionario} · {holerite.competencia}
        </CardTitle>
        <p className="mt-1 text-sm text-muted-foreground">
          Matrícula {holerite.resumo.matricula} · {holerite.resumo.avos}/{holerite.resumo.divisor}{' '}
          avos · salário de referência {formatarSalario(holerite.resumo.salarioReferencia)}
        </p>
      </CardHeader>

      <CardContent>
        {editavel && manuais.length > 0 && (
          <FormularioLancamento
            idFolha={idFolha}
            idHolerite={idHolerite}
            rubricas={manuais}
            aoLancar={async () => {
              await carregar()
              await aoMudar()
            }}
          />
        )}

        <h3 className="mb-3 text-sm font-medium">Lançamentos e memória de cálculo</h3>

        {holerite.lancamentos.length === 0 && (
          <p className="text-sm text-muted-foreground">
            Nenhum lançamento. Calcule a folha para gerar o salário.
          </p>
        )}

        <ul className="divide-y divide-border">
          {holerite.lancamentos.map((lancamento) => (
            <li key={lancamento.id} className="py-3">
              <div className="flex flex-wrap items-baseline justify-between gap-2">
                <span className="text-sm">
                  <span className="font-mono text-xs text-muted-foreground">
                    {lancamento.codigoRubrica}
                  </span>{' '}
                  <span className="font-medium">{lancamento.nomeRubrica}</span>
                  {lancamento.referencia && (
                    <span className="ml-2 text-xs text-muted-foreground">
                      {lancamento.referencia}
                    </span>
                  )}
                  {lancamento.origem === 'Manual' && (
                    <Badge variant="secondary" className="ml-2">
                      manual
                    </Badge>
                  )}
                </span>

                <span className="flex items-center gap-3">
                  <span
                    className={`text-sm font-medium ${
                      lancamento.tipo === 'Desconto' ? 'text-muted-foreground' : ''
                    }`}
                  >
                    {lancamento.tipo === 'Desconto' ? '− ' : ''}
                    {formatarSalario(lancamento.valor)}
                  </span>

                  {editavel && lancamento.origem === 'Manual' && (
                    <Button variant="ghost" size="sm" onClick={() => void apagar(lancamento.id)}>
                      Remover
                    </Button>
                  )}
                </span>
              </div>

              {/* A memória: como aquele número apareceu. */}
              <ol className="mt-2 space-y-0.5 border-l border-border pl-3">
                {lancamento.memoria.map((linha) => (
                  <li
                    key={linha.ordem}
                    className="flex flex-wrap justify-between gap-2 text-xs text-muted-foreground"
                  >
                    <span>
                      {linha.descricao}
                      <span className="ml-2 font-mono">{linha.expressao}</span>
                    </span>
                    <span className="font-mono">{formatarSalario(linha.valor)}</span>
                  </li>
                ))}
              </ol>
            </li>
          ))}
        </ul>

        <div className="mt-4 flex justify-end gap-6 border-t border-border pt-4 text-sm">
          <span className="text-muted-foreground">
            Proventos {formatarSalario(holerite.resumo.totalProventos)}
          </span>
          <span className="text-muted-foreground">
            Descontos {formatarSalario(holerite.resumo.totalDescontos)}
          </span>
          <span className="font-semibold">Líquido {formatarSalario(holerite.resumo.liquido)}</span>
        </div>
      </CardContent>
    </Card>
  )
}

function FormularioLancamento({
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
        valor: Number(valor),
        referencia: referencia.trim() || null,
      })
      definirValor('')
      definirReferencia('')
      await aoLancar()
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Falha ao lançar.')
    } finally {
      definirEnviando(false)
    }
  }

  return (
    <form onSubmit={aoEnviar} className="mb-6 grid items-end gap-4 sm:grid-cols-4" noValidate>
      <div className="sm:col-span-4">
        <h3 className="text-sm font-medium">Lançamento manual</h3>
        <p className="mt-0.5 text-xs text-muted-foreground">
          O valor é sempre positivo — quem define se soma ou subtrai é o tipo da rubrica.
        </p>
      </div>

      <div className="flex flex-col gap-2">
        <Label htmlFor={`rubrica-${idHolerite}`}>Rubrica</Label>
        <select
          id={`rubrica-${idHolerite}`}
          className="h-9 rounded-md border border-input bg-transparent px-3 py-1 text-sm shadow-xs"
          value={idRubrica}
          onChange={(e) => definirIdRubrica(e.target.value)}
        >
          {rubricas.map((rubrica) => (
            <option key={rubrica.id} value={rubrica.id}>
              {rubrica.codigo} — {rubrica.nome}
            </option>
          ))}
        </select>
      </div>

      <div className="flex flex-col gap-2">
        <Label htmlFor={`valor-${idHolerite}`}>Valor</Label>
        <Input
          id={`valor-${idHolerite}`}
          type="number"
          step="0.01"
          min="0"
          required
          value={valor}
          onChange={(e) => definirValor(e.target.value)}
        />
      </div>

      <div className="flex flex-col gap-2">
        <Label htmlFor={`ref-${idHolerite}`}>Referência</Label>
        <Input
          id={`ref-${idHolerite}`}
          placeholder="opcional"
          value={referencia}
          onChange={(e) => definirReferencia(e.target.value)}
        />
      </div>

      <div>
        <Button type="submit" size="sm" disabled={enviando}>
          {enviando ? 'Lançando...' : 'Lançar'}
        </Button>
      </div>

      {erro && (
        <div className="sm:col-span-4">
          <Alert variant="destructive" role="alert">
            <AlertDescription>{erro}</AlertDescription>
          </Alert>
        </div>
      )}
    </form>
  )
}
