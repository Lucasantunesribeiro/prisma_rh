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
  abrirFolha,
  competenciaPorExtenso,
  listarFolhas,
  normalizarCompetencia,
  podeProcessarFolha,
  ROTULO_SITUACAO_FOLHA,
  type FolhaResumo,
} from '@/api/folha'
import { listarEmpresas, type Empresa } from '@/api/empresas'
import { formatarSalario } from '@/api/pessoas'
import { useSessao } from '@/auth/useSessao'

type Estado =
  | { situacao: 'carregando' }
  | { situacao: 'pronto'; folhas: FolhaResumo[]; empresas: Empresa[] }
  | { situacao: 'erro'; mensagem: string }

export default function Folhas() {
  const { usuario } = useSessao()
  const processa = podeProcessarFolha(usuario?.perfil)

  const [estado, definirEstado] = useState<Estado>({ situacao: 'carregando' })

  const carregar = useCallback(async () => {
    try {
      const [folhas, empresas] = await Promise.all([listarFolhas(), listarEmpresas()])
      definirEstado({ situacao: 'pronto', folhas, empresas: empresas.itens })
    } catch (falha) {
      definirEstado({
        situacao: 'erro',
        mensagem: falha instanceof Error ? falha.message : 'Falha ao carregar folhas.',
      })
    }
  }, [])

  useEffect(() => {
    // O estado só muda DEPOIS do await, quando a resposta da API chega.
    // oxlint-disable-next-line react/set-state-in-effect
    void carregar()
  }, [carregar])

  return (
    <main className="mx-auto w-full max-w-5xl px-6 py-8">
      <header className="mb-6">
        <h1 className="text-xl font-semibold tracking-tight">Folhas de pagamento</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Uma folha por empresa e competência. Depois de fechada, ela não muda mais.
        </p>
      </header>

      {estado.situacao === 'pronto' && processa && (
        <FormularioAbertura empresas={estado.empresas} aoAbrir={carregar} />
      )}

      <Card>
        <CardHeader>
          <CardTitle className="text-base">
            {estado.situacao === 'pronto' ? `${estado.folhas.length} folha(s)` : 'Folhas'}
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
                  void carregar()
                }}
              >
                Tentar novamente
              </Button>
            </div>
          )}

          {estado.situacao === 'pronto' && estado.folhas.length === 0 && (
            <p className="py-2 text-sm text-muted-foreground">
              {processa
                ? 'Nenhuma folha aberta. Abra a primeira acima.'
                : 'Nenhuma folha aberta.'}
            </p>
          )}

          {estado.situacao === 'pronto' && estado.folhas.length > 0 && (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Competência</TableHead>
                  <TableHead>Empresa</TableHead>
                  <TableHead>Situação</TableHead>
                  <TableHead className="text-right">Funcionários</TableHead>
                  <TableHead className="text-right">Líquido</TableHead>
                  <TableHead className="text-right">Abrir</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {estado.folhas.map((folha) => (
                  <TableRow key={folha.id}>
                    <TableCell className="font-medium">
                      {folha.competencia}
                      <span className="ml-2 text-xs font-normal text-muted-foreground">
                        {competenciaPorExtenso(folha.competencia)}
                      </span>
                    </TableCell>
                    <TableCell>{folha.empresa}</TableCell>
                    <TableCell>
                      <Badge variant={folha.situacao === 'Fechada' ? 'secondary' : 'default'}>
                        {ROTULO_SITUACAO_FOLHA[folha.situacao]}
                      </Badge>
                    </TableCell>
                    <TableCell className="text-right">{folha.quantidadeFuncionarios}</TableCell>
                    <TableCell className="text-right font-medium">
                      {formatarSalario(folha.totalLiquido)}
                    </TableCell>
                    <TableCell className="text-right">
                      <Button asChild variant="ghost" size="sm">
                        <Link to={`/folhas/${folha.id}`}>Abrir</Link>
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

function FormularioAbertura({
  empresas,
  aoAbrir,
}: {
  empresas: Empresa[]
  aoAbrir: () => Promise<void>
}) {
  const [idEmpresa, definirIdEmpresa] = useState(empresas[0]?.id ?? '')
  const [competencia, definirCompetencia] = useState('')
  const [erro, definirErro] = useState<string | null>(null)
  const [enviando, definirEnviando] = useState(false)

  const aoEnviar = async (evento: FormEvent) => {
    evento.preventDefault()
    definirErro(null)

    const normalizada = normalizarCompetencia(competencia)

    if (!normalizada) {
      definirErro('Competência inválida. Escreva o mês e o ano, como 08/2026.')
      return
    }

    definirEnviando(true)

    try {
      await abrirFolha(idEmpresa, normalizada)
      definirCompetencia('')
      await aoAbrir()
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Falha ao abrir a folha.')
    } finally {
      definirEnviando(false)
    }
  }

  if (empresas.length === 0) {
    return (
      <Card className="mb-6">
        <CardContent className="pt-6">
          <p className="text-sm text-muted-foreground">
            Cadastre uma empresa antes de abrir folha — a folha é sempre de uma empresa.
          </p>
        </CardContent>
      </Card>
    )
  }

  return (
    <Card className="mb-6">
      <CardHeader>
        <CardTitle className="text-base">Abrir folha</CardTitle>
      </CardHeader>
      <CardContent>
        <form onSubmit={aoEnviar} className="grid items-end gap-4 sm:grid-cols-3" noValidate>
          <div className="flex flex-col gap-2">
            <Label htmlFor="empresaFolha">Empresa</Label>
            <select
              id="empresaFolha"
              className="h-9 rounded-md border border-input bg-transparent px-3 py-1 text-sm shadow-xs"
              value={idEmpresa}
              onChange={(e) => definirIdEmpresa(e.target.value)}
            >
              {empresas.map((empresa) => (
                <option key={empresa.id} value={empresa.id}>
                  {empresa.nomeFantasia ?? empresa.razaoSocial}
                </option>
              ))}
            </select>
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="competenciaFolha">Competência</Label>
            <Input
              id="competenciaFolha"
              required
              placeholder="08/2026"
              value={competencia}
              onChange={(e) => definirCompetencia(e.target.value)}
            />
          </div>

          <div>
            <Button type="submit" size="sm" disabled={enviando}>
              {enviando ? 'Abrindo...' : 'Abrir folha'}
            </Button>
          </div>

          {erro && (
            <div className="sm:col-span-3">
              <Alert variant="destructive" role="alert">
                <AlertDescription>{erro}</AlertDescription>
              </Alert>
            </div>
          )}
        </form>
      </CardContent>
    </Card>
  )
}
