import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import {
  formatarData,
  formatarSalario,
  listarCargos,
  listarContratos,
  listarVigencias,
  registrarAlteracao,
  ROTULO_MOTIVO,
  type Cargo,
  type Contrato,
  type Vigencia,
} from '@/api/pessoas'
import { obterFuncionario, type Funcionario } from '@/api/pessoas'
import { podeAdministrarPessoas } from '@/api/autenticacao'
import { useSessao } from '@/auth/useSessao'

interface Carregado {
  funcionario: Funcionario
  contratos: Contrato[]
  cargos: Cargo[]
}

type Estado =
  | { situacao: 'carregando' }
  | { situacao: 'pronto'; dados: Carregado }
  | { situacao: 'erro'; mensagem: string }

export default function FuncionarioDetalhe() {
  const { id } = useParams<{ id: string }>()
  const { usuario } = useSessao()
  const administra = podeAdministrarPessoas(usuario?.perfil)

  const [estado, definirEstado] = useState<Estado>({ situacao: 'carregando' })

  const carregar = useCallback(async () => {
    if (!id) return

    try {
      const [funcionario, contratos, cargos] = await Promise.all([
        obterFuncionario(id),
        listarContratos(id),
        listarCargos(),
      ])
      definirEstado({ situacao: 'pronto', dados: { funcionario, contratos, cargos } })
    } catch (falha) {
      definirEstado({
        situacao: 'erro',
        mensagem: falha instanceof Error ? falha.message : 'Falha ao carregar o funcionário.',
      })
    }
  }, [id])

  useEffect(() => {
    // O estado só muda DEPOIS do await, quando a resposta da API chega.
    // oxlint-disable-next-line react/set-state-in-effect
    void carregar()
  }, [carregar])

  return (
    <main className="mx-auto w-full max-w-4xl px-6 py-8">
      <Button asChild variant="ghost" size="sm" className="mb-4 -ml-2">
        <Link to="/funcionarios">← Funcionários</Link>
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

        {estado.situacao === 'pronto' && (
          <Conteudo dados={estado.dados} administra={administra} aoMudar={carregar} />
        )}
      </div>
    </main>
  )
}

function Conteudo({
  dados,
  administra,
  aoMudar,
}: {
  dados: Carregado
  administra: boolean
  aoMudar: () => Promise<void>
}) {
  const { funcionario, contratos, cargos } = dados

  return (
    <>
      <header className="mb-6">
        <h1 className="text-xl font-semibold tracking-tight">{funcionario.nome}</h1>
        <p className="mt-1 font-mono text-sm text-muted-foreground">
          {funcionario.cpfFormatado} · nascimento {formatarData(funcionario.dataNascimento)}
        </p>
      </header>

      {contratos.length === 0 && (
        <Card>
          <CardContent className="pt-6">
            <p className="text-sm text-muted-foreground">
              Esta pessoa ainda não tem contrato. Sem contrato não há salário, cargo nem lotação —
              e a folha não teria o que calcular.
            </p>
          </CardContent>
        </Card>
      )}

      {contratos.map((contrato) => (
        <CartaoContrato
          key={contrato.id}
          contrato={contrato}
          cargos={cargos}
          administra={administra}
          aoMudar={aoMudar}
        />
      ))}
    </>
  )
}

function CartaoContrato({
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
      definirErro(falha instanceof Error ? falha.message : 'Falha ao carregar o histórico.')
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
    <Card className="mt-6">
      <CardHeader>
        <div className="flex items-start justify-between gap-4">
          <div>
            <CardTitle className="text-base">Matrícula {contrato.matricula}</CardTitle>
            <p className="mt-1 text-sm text-muted-foreground">
              Admissão em {formatarData(contrato.dataAdmissao)}
              {contrato.dataDesligamento &&
                ` · desligado em ${formatarData(contrato.dataDesligamento)}`}
            </p>
          </div>
          <Badge variant={ativo ? 'default' : 'secondary'}>
            {ativo ? 'ativo' : 'desligado'}
          </Badge>
        </div>
      </CardHeader>

      <CardContent>
        {administra && ativo && contrato.vigenciaAtual && (
          <FormularioAlteracao
            contrato={contrato}
            cargos={cargos}
            aoRegistrar={async () => {
              await carregarHistorico()
              await aoMudar()
            }}
          />
        )}

        <h3 className="mb-3 text-sm font-medium">Histórico contratual</h3>

        {erro && (
          <Alert variant="destructive" role="alert">
            <AlertDescription>{erro}</AlertDescription>
          </Alert>
        )}

        {!vigencias && !erro && <p className="text-sm text-muted-foreground">Carregando...</p>}

        {vigencias && (
          <ol className="border-l border-border">
            {vigencias.map((vigencia) => (
              <li key={vigencia.id} className="relative py-3 pl-6">
                <span
                  className={`absolute -left-[5px] top-5 size-2.5 rounded-full ${
                    vigencia.validoAte === null ? 'bg-foreground' : 'bg-border'
                  }`}
                  aria-hidden="true"
                />
                <div className="flex flex-wrap items-baseline justify-between gap-2">
                  <span className="text-sm font-medium">
                    {formatarSalario(vigencia.salario)}
                    <span className="ml-2 font-normal text-muted-foreground">
                      {nomeCargo(vigencia.idCargo)} · {vigencia.jornadaMensalHoras}h/mês
                    </span>
                  </span>
                  <Badge variant={vigencia.validoAte === null ? 'default' : 'secondary'}>
                    {ROTULO_MOTIVO[vigencia.motivo]}
                  </Badge>
                </div>
                <p className="mt-0.5 text-xs text-muted-foreground">
                  {formatarData(vigencia.validoDe)}
                  {vigencia.validoAte
                    ? ` até ${formatarData(vigencia.validoAte)}`
                    : ' — vigência atual'}
                </p>
              </li>
            ))}
          </ol>
        )}
      </CardContent>
    </Card>
  )
}

function FormularioAlteracao({
  contrato,
  cargos,
  aoRegistrar,
}: {
  contrato: Contrato
  cargos: Cargo[]
  aoRegistrar: () => Promise<void>
}) {
  const atual = contrato.vigenciaAtual!

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
    const motivo =
      Number(salario) !== atual.salario
        ? 'AlteracaoSalarial'
        : idCargo !== atual.idCargo
          ? 'MudancaCargo'
          : 'AlteracaoJornada'

    try {
      await registrarAlteracao(contrato.id, {
        validoDe,
        salario: Number(salario),
        idCargo,
        idEstabelecimento: atual.idEstabelecimento,
        jornadaMensalHoras: Number(jornada),
        motivo,
      })
      definirValidoDe('')
      await aoRegistrar()
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Falha ao registrar a alteração.')
    } finally {
      definirEnviando(false)
    }
  }

  return (
    <form onSubmit={aoEnviar} className="mb-6 grid gap-4 sm:grid-cols-4" noValidate>
      <div className="sm:col-span-4">
        <h3 className="text-sm font-medium">Registrar alteração</h3>
        <p className="mt-0.5 text-xs text-muted-foreground">
          A vigência atual será fechada na véspera da data informada. Nada é sobrescrito.
        </p>
      </div>

      <div className="flex flex-col gap-2">
        <Label htmlFor={`de-${contrato.id}`}>A partir de</Label>
        <Input
          id={`de-${contrato.id}`}
          type="date"
          required
          value={validoDe}
          onChange={(e) => definirValidoDe(e.target.value)}
        />
      </div>

      <div className="flex flex-col gap-2">
        <Label htmlFor={`salario-${contrato.id}`}>Salário</Label>
        <Input
          id={`salario-${contrato.id}`}
          type="number"
          step="0.01"
          min="0.01"
          required
          value={salario}
          onChange={(e) => definirSalario(e.target.value)}
        />
      </div>

      <div className="flex flex-col gap-2">
        <Label htmlFor={`cargo-${contrato.id}`}>Cargo</Label>
        <select
          id={`cargo-${contrato.id}`}
          className="h-9 rounded-md border border-input bg-transparent px-3 py-1 text-sm shadow-xs"
          value={idCargo}
          onChange={(e) => definirIdCargo(e.target.value)}
        >
          {cargos.map((cargo) => (
            <option key={cargo.id} value={cargo.id}>
              {cargo.nome}
            </option>
          ))}
        </select>
      </div>

      <div className="flex flex-col gap-2">
        <Label htmlFor={`jornada-${contrato.id}`}>Jornada (h/mês)</Label>
        <Input
          id={`jornada-${contrato.id}`}
          type="number"
          min="1"
          max="400"
          required
          value={jornada}
          onChange={(e) => definirJornada(e.target.value)}
        />
      </div>

      {erro && (
        <div className="sm:col-span-4">
          <Alert variant="destructive" role="alert">
            <AlertDescription>{erro}</AlertDescription>
          </Alert>
        </div>
      )}

      <div className="sm:col-span-4">
        <Button type="submit" size="sm" disabled={enviando}>
          {enviando ? 'Registrando...' : 'Registrar alteração'}
        </Button>
      </div>
    </form>
  )
}
