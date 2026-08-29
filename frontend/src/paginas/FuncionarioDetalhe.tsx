import { ArrowRight, Pencil } from 'lucide-react'
import { useCallback, useEffect, useState, type FormEvent, type ReactNode } from 'react'
import { useParams } from 'react-router'
import { podeAdministrarPessoas } from '@/api/autenticacao'
import {
  desligar,
  formatarData,
  formatarSalario,
  listarCargos,
  listarContratos,
  listarDependentes,
  listarVigencias,
  obterFuncionario,
  NORMA_MOTIVO_DESLIGAMENTO,
  registrarAlteracao,
  ROTULO_MOTIVO,
  ROTULO_MOTIVO_DESLIGAMENTO,
  type Cargo,
  type Contrato,
  type Dependente,
  type Funcionario,
  type MotivoDesligamento,
  type MotivoVigencia,
  type Vigencia,
} from '@/api/pessoas'
import { useSessao } from '@/auth/useSessao'
import { EstadoCarregando, EstadoErro } from '@/components/sistema/Estados'
import {
  CabecalhoPagina,
  CabecalhoSecao,
  Campo,
  ListaCampos,
  StatusBadge,
} from '@/components/sistema/Primitivos'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Drawer, DrawerClose, DrawerContent, DrawerTrigger } from '@/components/ui/drawer'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { usePagina } from '@/layout/usePagina'
import { cn } from '@/lib/utils'
import { SecaoDependentes } from './SecaoDependentes'
import { SecaoDecimoTerceiro } from './SecaoDecimoTerceiro'
import { SecaoFerias } from './SecaoFerias'
import { SecaoRescisao } from './SecaoRescisao'

export default function FuncionarioDetalhe() {
  const { id } = useParams<{ id: string }>()
  const { usuario } = useSessao()
  const administra = podeAdministrarPessoas(usuario?.perfil)

  const [funcionario, definirFuncionario] = useState<Funcionario | null>(null)
  const [contratos, definirContratos] = useState<Contrato[]>([])
  const [cargos, definirCargos] = useState<Cargo[]>([])
  const [dependentes, definirDependentes] = useState<Dependente[]>([])
  const [carregando, definirCarregando] = useState(true)
  const [erro, definirErro] = useState<string | null>(null)

  usePagina([
    { texto: 'Pessoas' },
    { texto: 'Funcionários', para: '/funcionarios' },
    { texto: funcionario?.nome ?? 'Carregando' },
  ])

  const carregar = useCallback(async () => {
    if (!id) return

    definirErro(null)

    try {
      const [pessoa, vinculos, catalogo, familia] = await Promise.all([
        obterFuncionario(id),
        listarContratos(id),
        listarCargos(),
        listarDependentes(id),
      ])

      definirFuncionario(pessoa)
      definirContratos(vinculos)
      definirCargos(catalogo)
      definirDependentes(familia)
    } catch (falha) {
      definirErro(
        falha instanceof Error ? falha.message : 'Não foi possível carregar o funcionário.',
      )
    } finally {
      definirCarregando(false)
    }
  }, [id])

  useEffect(() => {
    // O estado só muda DEPOIS do await, quando a resposta da API chega.
    // oxlint-disable-next-line react/set-state-in-effect
    void carregar()
  }, [carregar])

  if (carregando) return <EstadoCarregando rotulo="Carregando funcionário" />

  if (erro || !funcionario) {
    return (
      <EstadoErro
        mensagem={erro ?? 'Funcionário não encontrado.'}
        aoTentarNovamente={() => void carregar()}
      />
    )
  }

  return (
    <>
      <CabecalhoPagina
        titulo={funcionario.nome}
        meta={
          <div className="flex flex-wrap items-center gap-x-4 gap-y-1 pt-0.5 text-[13px] text-muted-foreground">
            <StatusBadge tom={funcionario.ativo ? 'sucesso' : 'neutro'}>
              {funcionario.ativo ? 'Ativo' : 'Inativo'}
            </StatusBadge>
            <span className="tabular">{funcionario.cpfFormatado}</span>
            <span>Nascimento {formatarData(funcionario.dataNascimento)}</span>
          </div>
        }
      />

      {contratos.length === 0 && (
        <Alert>
          <AlertDescription>
            Esta pessoa ainda não tem contrato. Sem contrato não há salário, cargo nem lotação — e a
            folha não teria o que calcular.
          </AlertDescription>
        </Alert>
      )}

      <div className="space-y-9">
        {contratos.map((contrato) => (
          <SecaoContrato
            key={contrato.id}
            contrato={contrato}
            cargos={cargos}
            administra={administra}
            aoMudar={carregar}
          />
        ))}

        {/*
         * Férias pertencem ao CONTRATO, não à pessoa: o período aquisitivo
         * nasce da admissão daquele vínculo. Uma pessoa readmitida recomeça.
         */}
        {contratos.map((contrato) => (
          <SecaoFerias
            key={`ferias-${contrato.id}`}
            idContrato={contrato.id}
            administra={administra}
          />
        ))}

        {/* 13º, como as férias, nasce do CONTRATO: os avos contam meses de vínculo. */}
        {contratos.map((contrato) => (
          <SecaoDecimoTerceiro key={`decimo-${contrato.id}`} idContrato={contrato.id} />
        ))}

        {/* Só faz sentido para contrato encerrado: sem desligamento não há rescisão. */}
        {contratos
          .filter((c) => c.situacao === 'Desligado')
          .map((contrato) => (
            <SecaoRescisao key={`rescisao-${contrato.id}`} idContrato={contrato.id} />
          ))}

        {/*
         * Depois dos contratos: dependente pertence à PESSOA, não ao vínculo.
         * Um filho continua sendo filho se ela for readmitida.
         */}
        <SecaoDependentes
          idFuncionario={funcionario.id}
          dependentes={dependentes}
          administra={administra}
          aoMudar={carregar}
        />
      </div>
    </>
  )
}

function SecaoContrato({
  contrato,
  cargos,
  administra,
  aoMudar,
}: {
  contrato: Contrato
  cargos: Cargo[]
  administra: boolean
  aoMudar: () => Promise<void>
}) {
  const [vigencias, definirVigencias] = useState<Vigencia[] | null>(null)
  const [erro, definirErro] = useState<string | null>(null)

  const carregarHistorico = useCallback(async () => {
    try {
      definirVigencias(await listarVigencias(contrato.id))
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Não foi possível carregar o histórico.')
    }
  }, [contrato.id])

  useEffect(() => {
    // O estado só muda DEPOIS do await, quando a resposta da API chega.
    // oxlint-disable-next-line react/set-state-in-effect
    void carregarHistorico()
  }, [carregarHistorico])

  const nomeCargo = (idCargo: string) => cargos.find((c) => c.id === idCargo)?.nome ?? '—'
  const ativo = contrato.situacao === 'Ativo'

  return (
    <section>
      <CabecalhoSecao
        titulo={`Matrícula ${contrato.matricula}`}
        descricao={
          contrato.dataDesligamento
            ? `Admissão em ${formatarData(contrato.dataAdmissao)} · desligado em ${formatarData(contrato.dataDesligamento)}` +
              (contrato.motivoDesligamento
                ? ` · ${ROTULO_MOTIVO_DESLIGAMENTO[contrato.motivoDesligamento]}`
                : '')
            : `Admissão em ${formatarData(contrato.dataAdmissao)}`
        }
        acao={
          <div className="flex items-center gap-2">
            <StatusBadge tom={ativo ? 'sucesso' : 'neutro'}>
              {ativo ? 'Ativo' : 'Desligado'}
            </StatusBadge>

            {administra && ativo && contrato.vigenciaAtual && (
              <>
                <RegistrarAlteracao
                  contrato={contrato}
                  cargos={cargos}
                  aoRegistrar={async () => {
                    await carregarHistorico()
                    await aoMudar()
                  }}
                />
                <Desligar
                  contrato={contrato}
                  aoDesligar={async () => {
                    await carregarHistorico()
                    await aoMudar()
                  }}
                />
              </>
            )}
          </div>
        }
      />

      {contrato.vigenciaAtual && (
        <ListaCampos colunas={4}>
          <Campo rotulo="Salário atual">
            <span className="tabular font-medium">
              {formatarSalario(contrato.vigenciaAtual.salario)}
            </span>
          </Campo>
          <Campo rotulo="Cargo">{nomeCargo(contrato.vigenciaAtual.idCargo)}</Campo>
          <Campo rotulo="Jornada">
            <span className="tabular">{contrato.vigenciaAtual.jornadaMensalHoras}h/mês</span>
          </Campo>
          <Campo rotulo="Vigente desde">
            <span className="tabular">{formatarData(contrato.vigenciaAtual.validoDe)}</span>
          </Campo>
        </ListaCampos>
      )}

      <h3 className="mb-3 mt-6 text-[13px] font-medium">Histórico contratual</h3>

      {erro && (
        <Alert variant="destructive" role="alert">
          <AlertDescription>{erro}</AlertDescription>
        </Alert>
      )}

      {!vigencias && !erro && (
        <p className="text-[13px] text-muted-foreground" role="status">
          Carregando histórico...
        </p>
      )}

      {vigencias && <LinhaDoTempo vigencias={vigencias} nomeCargo={nomeCargo} />}
    </section>
  )
}

/**
 * A linha do tempo do contrato.
 *
 * Mostra o que MUDOU em cada vigência, e não só o estado dela: "5.100 → 6.200"
 * conta a história que "6.200" sozinho esconde. É a característica central do
 * Prisma RH — alteração não sobrescreve o passado — e a interface tem que
 * deixar isso evidente sem precisar de legenda.
 *
 * Uma linha por vigência, com divisores discretos. Um card por alteração
 * transformaria um contrato de cinco anos numa pilha de vinte caixas.
 */
function LinhaDoTempo({
  vigencias,
  nomeCargo,
}: {
  vigencias: Vigencia[]
  nomeCargo: (id: string) => string
}) {
  // Mais recente primeiro: é o que se procura ao abrir.
  const ordenadas = [...vigencias].sort((a, b) => b.validoDe.localeCompare(a.validoDe))

  return (
    <ol className="border-l border-border">
      {ordenadas.map((vigencia, indice) => {
        // A anterior no tempo é a próxima da lista, porque está em ordem
        // decrescente. É contra ela que se compara para mostrar a mudança.
        const anterior = ordenadas[indice + 1]
        const atual = vigencia.validoAte === null

        return (
          <li key={vigencia.id} className="relative py-3.5 pl-6">
            <span
              aria-hidden
              className={cn(
                'absolute -left-[4.5px] top-[1.35rem] size-2 rounded-full ring-2 ring-background',
                atual ? 'bg-primary' : 'bg-border',
              )}
            />

            <div className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
              <span className="text-[13px] font-medium">{ROTULO_MOTIVO[vigencia.motivo]}</span>
              <span className="tabular text-xs text-muted-foreground">
                {formatarData(vigencia.validoDe)}
                {vigencia.validoAte
                  ? ` até ${formatarData(vigencia.validoAte)}`
                  : ' — vigência atual'}
              </span>
            </div>

            <dl className="mt-1.5 space-y-1">
              <Mudanca
                rotulo="Salário"
                de={anterior && anterior.salario !== vigencia.salario ? formatarSalario(anterior.salario) : null}
                para={formatarSalario(vigencia.salario)}
                tabular
              />
              <Mudanca
                rotulo="Cargo"
                de={
                  anterior && anterior.idCargo !== vigencia.idCargo
                    ? nomeCargo(anterior.idCargo)
                    : null
                }
                para={nomeCargo(vigencia.idCargo)}
              />
              <Mudanca
                rotulo="Jornada"
                de={
                  anterior && anterior.jornadaMensalHoras !== vigencia.jornadaMensalHoras
                    ? `${anterior.jornadaMensalHoras}h/mês`
                    : null
                }
                para={`${vigencia.jornadaMensalHoras}h/mês`}
                tabular
              />
            </dl>
          </li>
        )
      })}
    </ol>
  )
}

function Mudanca({
  rotulo,
  de,
  para,
  tabular,
}: {
  rotulo: string
  de: string | null
  para: ReactNode
  tabular?: boolean
}) {
  return (
    <div className="flex flex-wrap items-baseline gap-x-2 text-[13px]">
      <dt className="w-16 shrink-0 text-xs text-muted-foreground">{rotulo}</dt>
      <dd className="flex flex-wrap items-baseline gap-1.5">
        {de && (
          <>
            <span className={cn('text-muted-foreground line-through', tabular && 'tabular')}>
              {de}
            </span>
            <ArrowRight className="size-3 shrink-0 text-muted-foreground" aria-label="alterado para" />
          </>
        )}
        <span className={cn(de && 'font-medium', tabular && 'tabular')}>{para}</span>
      </dd>
    </div>
  )
}

/**
 * Encerra o contrato.
 *
 * O **motivo** é obrigatório e não tem valor pré-selecionado: ele decide as
 * verbas rescisórias, e um padrão convidaria a aceitar o que já estava lá.
 * Deixar em branco força a escolha consciente.
 *
 * Ação irreversível — não há reabertura de contrato no produto —, por isso o
 * botão é destrutivo e o texto avisa antes.
 */
function Desligar({
  contrato,
  aoDesligar,
}: {
  contrato: Contrato
  aoDesligar: () => Promise<void>
}) {
  const [aberto, definirAberto] = useState(false)
  const [data, definirData] = useState('')
  const [motivo, definirMotivo] = useState<MotivoDesligamento | ''>('')
  const [erro, definirErro] = useState<string | null>(null)
  const [enviando, definirEnviando] = useState(false)

  const aoEnviar = async (evento: FormEvent) => {
    evento.preventDefault()

    if (motivo === '') {
      definirErro('Escolha o motivo do desligamento.')
      return
    }

    definirErro(null)
    definirEnviando(true)

    try {
      await desligar(contrato.id, data, motivo)
      definirAberto(false)
      await aoDesligar()
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Não foi possível desligar.')
    } finally {
      definirEnviando(false)
    }
  }

  const motivos = Object.keys(ROTULO_MOTIVO_DESLIGAMENTO) as MotivoDesligamento[]

  return (
    <Drawer open={aberto} onOpenChange={definirAberto}>
      <DrawerTrigger asChild>
        <Button variant="outline" size="sm">
          Desligar
        </Button>
      </DrawerTrigger>

      <DrawerContent
        titulo="Desligar contrato"
        descricao="O vínculo é encerrado e a vigência atual é fechada na data informada. Não há reabertura."
        className="max-w-lg"
      >
        <form onSubmit={aoEnviar} className="space-y-4" noValidate>
          <div className="space-y-1.5">
            <Label htmlFor={`desligamento-${contrato.id}`}>Data do desligamento</Label>
            <Input
              id={`desligamento-${contrato.id}`}
              type="date"
              required
              autoFocus
              value={data}
              onChange={(e) => definirData(e.target.value)}
            />
          </div>

          <div className="space-y-1.5">
            <Label htmlFor={`motivo-${contrato.id}`}>Motivo</Label>
            <select
              id={`motivo-${contrato.id}`}
              required
              value={motivo}
              onChange={(e) => definirMotivo(e.target.value as MotivoDesligamento)}
              className="h-9 w-full rounded-md border border-input bg-card px-3 text-[13px] shadow-xs"
            >
              <option value="">Escolha…</option>
              {motivos.map((m) => (
                <option key={m} value={m}>
                  {ROTULO_MOTIVO_DESLIGAMENTO[m]}
                  {NORMA_MOTIVO_DESLIGAMENTO[m] ? ` (${NORMA_MOTIVO_DESLIGAMENTO[m]})` : ''}
                </option>
              ))}
            </select>
            <p className="text-xs text-muted-foreground">
              O motivo decide as verbas rescisórias — quem pede demissão não recebe o mesmo que
              quem é dispensado. O cálculo da rescisão ainda não existe.
            </p>
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
            <Button type="submit" size="sm" variant="destructive" disabled={enviando}>
              {enviando ? 'Desligando...' : 'Desligar'}
            </Button>
          </div>
        </form>
      </DrawerContent>
    </Drawer>
  )
}

function RegistrarAlteracao({
  contrato,
  cargos,
  aoRegistrar,
}: {
  contrato: Contrato
  cargos: Cargo[]
  aoRegistrar: () => Promise<void>
}) {
  const atual = contrato.vigenciaAtual!

  const [aberto, definirAberto] = useState(false)
  const [validoDe, definirValidoDe] = useState('')
  const [salario, definirSalario] = useState(String(atual.salario))
  const [idCargo, definirIdCargo] = useState(atual.idCargo)
  const [jornada, definirJornada] = useState(String(atual.jornadaMensalHoras))
  const [erro, definirErro] = useState<string | null>(null)
  const [enviando, definirEnviando] = useState(false)

  const aoEnviar = async (evento: FormEvent) => {
    evento.preventDefault()
    definirErro(null)
    definirEnviando(true)

    // O motivo é deduzido do que realmente mudou. Pedir para o usuário
    // escolher abriria espaço para um rótulo que não descreve a alteração.
    const valorSalario = Number(salario.replace(',', '.'))

    const motivo: MotivoVigencia =
      valorSalario !== atual.salario
        ? 'AlteracaoSalarial'
        : idCargo !== atual.idCargo
          ? 'MudancaCargo'
          : 'AlteracaoJornada'

    try {
      await registrarAlteracao(contrato.id, {
        validoDe,
        salario: valorSalario,
        idCargo,
        idEstabelecimento: atual.idEstabelecimento,
        jornadaMensalHoras: Number(jornada),
        motivo,
      })

      definirValidoDe('')
      definirAberto(false)
      await aoRegistrar()
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Não foi possível registrar a alteração.')
    } finally {
      definirEnviando(false)
    }
  }

  return (
    <Drawer open={aberto} onOpenChange={definirAberto}>
      <DrawerTrigger asChild>
        <Button variant="outline" size="sm">
          <Pencil aria-hidden />
          Registrar alteração
        </Button>
      </DrawerTrigger>

      <DrawerContent
        titulo="Registrar alteração contratual"
        descricao="A vigência atual é fechada na véspera e uma nova é aberta. O passado permanece consultável."
        className="max-w-lg"
      >
        <form onSubmit={aoEnviar} className="space-y-4" noValidate>
          <div className="space-y-1.5">
            <Label htmlFor="validoDe">Válido a partir de</Label>
            <Input
              id="validoDe"
              type="date"
              required
              autoFocus
              value={validoDe}
              onChange={(e) => definirValidoDe(e.target.value)}
            />
          </div>

          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor="salarioAlteracao">Salário</Label>
              <Input
                id="salarioAlteracao"
                required
                inputMode="decimal"
                value={salario}
                onChange={(e) => definirSalario(e.target.value)}
                className="tabular"
              />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="jornadaAlteracao">Jornada mensal</Label>
              <Input
                id="jornadaAlteracao"
                required
                inputMode="numeric"
                value={jornada}
                onChange={(e) => definirJornada(e.target.value)}
                className="tabular"
              />
            </div>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="cargoAlteracao">Cargo</Label>
            <select
              id="cargoAlteracao"
              value={idCargo}
              onChange={(e) => definirIdCargo(e.target.value)}
              className="h-9 w-full rounded-md border border-input bg-card px-3 text-[13px] shadow-xs"
            >
              {cargos.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.codigo} — {c.nome}
                </option>
              ))}
            </select>
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
              {enviando ? 'Registrando...' : 'Registrar alteração'}
            </Button>
          </div>
        </form>
      </DrawerContent>
    </Drawer>
  )
}
