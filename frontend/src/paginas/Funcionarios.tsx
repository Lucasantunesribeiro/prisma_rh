import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router'
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
  criarFuncionario,
  formatarData,
  listarFuncionarios,
  type FiltroFuncionarios,
  type Funcionario,
} from '@/api/pessoas'
import { podeAdministrarPessoas } from '@/api/autenticacao'
import { useSessao } from '@/auth/useSessao'

type Estado =
  | { situacao: 'carregando' }
  | { situacao: 'pronto'; total: number; itens: Funcionario[] }
  | { situacao: 'erro'; mensagem: string }

export default function Funcionarios() {
  const { usuario } = useSessao()
  const administra = podeAdministrarPessoas(usuario?.perfil)

  const [estado, definirEstado] = useState<Estado>({ situacao: 'carregando' })
  const [filtro, definirFiltro] = useState<FiltroFuncionarios>({})

  const carregar = useCallback(async (aplicado: FiltroFuncionarios) => {
    try {
      const pagina = await listarFuncionarios(aplicado)
      definirEstado({ situacao: 'pronto', total: pagina.total, itens: pagina.itens })
    } catch (falha) {
      definirEstado({
        situacao: 'erro',
        mensagem: falha instanceof Error ? falha.message : 'Falha ao carregar funcionários.',
      })
    }
  }, [])

  useEffect(() => {
    // O estado só muda DEPOIS do await, quando a resposta da API chega.
    // oxlint-disable-next-line react/set-state-in-effect
    void carregar(filtro)
  }, [carregar, filtro])

  const temFiltro = Boolean(filtro.nome || filtro.cpf || filtro.ativo !== undefined)

  return (
    <main className="mx-auto w-full max-w-5xl px-6 py-8">
      <header className="mb-6">
        <h1 className="text-xl font-semibold tracking-tight">Funcionários</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Pessoas da sua organização. O vínculo com a empresa fica no contrato.
        </p>
      </header>

      {administra && <FormularioNovoFuncionario aoCriar={() => carregar(filtro)} />}

      <Filtros
        aoAplicar={(novo) => {
          definirEstado({ situacao: 'carregando' })
          definirFiltro(novo)
        }}
      />

      <Card>
        <CardHeader>
          <CardTitle className="text-base">
            {estado.situacao === 'pronto' ? `${estado.total} funcionário(s)` : 'Funcionários'}
          </CardTitle>
        </CardHeader>

        <CardContent aria-live="polite" aria-busy={estado.situacao === 'carregando'}>
          {estado.situacao === 'carregando' && (
            <p className="py-2 text-sm text-muted-foreground">Carregando...</p>
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
                  void carregar(filtro)
                }}
              >
                Tentar novamente
              </Button>
            </div>
          )}

          {estado.situacao === 'pronto' && estado.itens.length === 0 && (
            <p className="py-2 text-sm text-muted-foreground">
              {temFiltro
                ? 'Nenhum funcionário corresponde ao filtro.'
                : administra
                  ? 'Nenhum funcionário cadastrado. Crie o primeiro acima.'
                  : 'Nenhum funcionário cadastrado.'}
            </p>
          )}

          {estado.situacao === 'pronto' && estado.itens.length > 0 && (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Nome</TableHead>
                  <TableHead>CPF</TableHead>
                  <TableHead>Nascimento</TableHead>
                  <TableHead>Situação</TableHead>
                  <TableHead className="text-right">Contratos</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {estado.itens.map((funcionario) => (
                  <TableRow key={funcionario.id}>
                    <TableCell className="font-medium">{funcionario.nome}</TableCell>
                    <TableCell className="font-mono text-xs" title="Documento parcial por privacidade">
                      {funcionario.cpfFormatado}
                    </TableCell>
                    <TableCell>{formatarData(funcionario.dataNascimento)}</TableCell>
                    <TableCell>
                      <Badge variant={funcionario.ativo ? 'default' : 'secondary'}>
                        {funcionario.ativo ? 'ativo' : 'inativo'}
                      </Badge>
                    </TableCell>
                    <TableCell className="text-right">
                      <Button asChild variant="ghost" size="sm">
                        <Link to={`/funcionarios/${funcionario.id}`}>Abrir</Link>
                      </Button>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>
    </main>
  )
}

function Filtros({ aoAplicar }: { aoAplicar: (filtro: FiltroFuncionarios) => void }) {
  const [nome, definirNome] = useState('')
  const [cpf, definirCpf] = useState('')
  const [somenteAtivos, definirSomenteAtivos] = useState(false)

  const aoEnviar = (evento: FormEvent) => {
    evento.preventDefault()
    aoAplicar({
      nome: nome || undefined,
      cpf: cpf || undefined,
      ativo: somenteAtivos ? true : undefined,
    })
  }

  return (
    <Card className="mb-6">
      <CardContent className="pt-6">
        <form onSubmit={aoEnviar} className="grid items-end gap-4 sm:grid-cols-4" noValidate>
          <div className="flex flex-col gap-2">
            <Label htmlFor="filtroNome">Nome</Label>
            <Input id="filtroNome" value={nome} onChange={(e) => definirNome(e.target.value)} />
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="filtroCpf">CPF completo</Label>
            <Input
              id="filtroCpf"
              value={cpf}
              placeholder="000.000.000-00"
              onChange={(e) => definirCpf(e.target.value)}
            />
          </div>

          <div className="flex items-center gap-2 pb-2">
            <input
              id="filtroAtivos"
              type="checkbox"
              className="size-4"
              checked={somenteAtivos}
              onChange={(e) => definirSomenteAtivos(e.target.checked)}
            />
            <Label htmlFor="filtroAtivos">Somente ativos</Label>
          </div>

          <div className="flex gap-2">
            <Button type="submit" size="sm">
              Filtrar
            </Button>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              onClick={() => {
                definirNome('')
                definirCpf('')
                definirSomenteAtivos(false)
                aoAplicar({})
              }}
            >
              Limpar
            </Button>
          </div>
        </form>

        <p className="mt-3 text-xs text-muted-foreground">
          A busca por CPF exige o documento completo e válido — busca parcial viraria uma forma de
          descobrir documentos por tentativa.
        </p>
      </CardContent>
    </Card>
  )
}

function FormularioNovoFuncionario({ aoCriar }: { aoCriar: () => Promise<void> }) {
  const [nome, definirNome] = useState('')
  const [cpf, definirCpf] = useState('')
  const [dataNascimento, definirDataNascimento] = useState('')
  const [erro, definirErro] = useState<string | null>(null)
  const [enviando, definirEnviando] = useState(false)

  const aoEnviar = async (evento: FormEvent) => {
    evento.preventDefault()
    definirErro(null)
    definirEnviando(true)

    try {
      await criarFuncionario({ nome, cpf, dataNascimento })
      definirNome('')
      definirCpf('')
      definirDataNascimento('')
      await aoCriar()
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Falha ao criar funcionário.')
    } finally {
      definirEnviando(false)
    }
  }

  return (
    <Card className="mb-6">
      <CardHeader>
        <CardTitle className="text-base">Novo funcionário</CardTitle>
      </CardHeader>
      <CardContent>
        <form onSubmit={aoEnviar} className="grid gap-4 sm:grid-cols-4" noValidate>
          <div className="flex flex-col gap-2 sm:col-span-2">
            <Label htmlFor="nomeFuncionario">Nome</Label>
            <Input
              id="nomeFuncionario"
              required
              value={nome}
              onChange={(e) => definirNome(e.target.value)}
            />
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="cpfFuncionario">CPF</Label>
            <Input
              id="cpfFuncionario"
              required
              placeholder="000.000.000-00"
              value={cpf}
              onChange={(e) => definirCpf(e.target.value)}
            />
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="nascimento">Nascimento</Label>
            <Input
              id="nascimento"
              type="date"
              required
              value={dataNascimento}
              onChange={(e) => definirDataNascimento(e.target.value)}
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
              {enviando ? 'Criando...' : 'Criar funcionário'}
            </Button>
          </div>
        </form>
      </CardContent>
    </Card>
  )
}
