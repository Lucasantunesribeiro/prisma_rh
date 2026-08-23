import { useCallback, useEffect, useState, type FormEvent } from 'react'
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
import { criarCargo, listarCargos, type Cargo } from '@/api/pessoas'
import { podeAdministrarPessoas } from '@/api/autenticacao'
import { useSessao } from '@/auth/useSessao'

type Estado =
  | { situacao: 'carregando' }
  | { situacao: 'pronto'; cargos: Cargo[] }
  | { situacao: 'erro'; mensagem: string }

export default function Cargos() {
  const { usuario } = useSessao()
  const administra = podeAdministrarPessoas(usuario?.perfil)

  const [estado, definirEstado] = useState<Estado>({ situacao: 'carregando' })

  const carregar = useCallback(async () => {
    try {
      definirEstado({ situacao: 'pronto', cargos: await listarCargos() })
    } catch (falha) {
      definirEstado({
        situacao: 'erro',
        mensagem: falha instanceof Error ? falha.message : 'Falha ao carregar cargos.',
      })
    }
  }, [])

  useEffect(() => {
    // O estado só muda DEPOIS do await, quando a resposta da API chega.
    // oxlint-disable-next-line react/set-state-in-effect
    void carregar()
  }, [carregar])

  return (
    <main className="mx-auto w-full max-w-3xl px-6 py-8">
      <header className="mb-6">
        <h1 className="text-xl font-semibold tracking-tight">Cargos</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Catálogo usado pelos contratos. Um cargo inativado continua legível no histórico.
        </p>
      </header>

      {administra && <FormularioNovoCargo aoCriar={carregar} />}

      <Card>
        <CardHeader>
          <CardTitle className="text-base">
            {estado.situacao === 'pronto' ? `${estado.cargos.length} cargo(s)` : 'Cargos'}
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

          {estado.situacao === 'pronto' && estado.cargos.length === 0 && (
            <p className="py-2 text-sm text-muted-foreground">
              Nenhum cargo cadastrado.
              {administra
                ? ' Crie o primeiro acima — um contrato precisa de cargo.'
                : ' Peça a um administrador ou analista para cadastrar.'}
            </p>
          )}

          {estado.situacao === 'pronto' && estado.cargos.length > 0 && (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Código</TableHead>
                  <TableHead>Nome</TableHead>
                  <TableHead>Situação</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {estado.cargos.map((cargo) => (
                  <TableRow key={cargo.id}>
                    <TableCell className="font-mono text-xs">{cargo.codigo}</TableCell>
                    <TableCell>{cargo.nome}</TableCell>
                    <TableCell>
                      <Badge variant={cargo.ativo ? 'default' : 'secondary'}>
                        {cargo.ativo ? 'ativo' : 'inativo'}
                      </Badge>
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

function FormularioNovoCargo({ aoCriar }: { aoCriar: () => Promise<void> }) {
  const [codigo, definirCodigo] = useState('')
  const [nome, definirNome] = useState('')
  const [erro, definirErro] = useState<string | null>(null)
  const [enviando, definirEnviando] = useState(false)

  const aoEnviar = async (evento: FormEvent) => {
    evento.preventDefault()
    definirErro(null)
    definirEnviando(true)

    try {
      await criarCargo({ codigo, nome })
      definirCodigo('')
      definirNome('')
      await aoCriar()
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Falha ao criar cargo.')
    } finally {
      definirEnviando(false)
    }
  }

  return (
    <Card className="mb-6">
      <CardHeader>
        <CardTitle className="text-base">Novo cargo</CardTitle>
      </CardHeader>
      <CardContent>
        <form onSubmit={aoEnviar} className="grid gap-4 sm:grid-cols-3" noValidate>
          <div className="flex flex-col gap-2">
            <Label htmlFor="codigoCargo">Código</Label>
            <Input
              id="codigoCargo"
              required
              value={codigo}
              onChange={(e) => definirCodigo(e.target.value)}
            />
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="nomeCargo">Nome</Label>
            <Input
              id="nomeCargo"
              required
              value={nome}
              onChange={(e) => definirNome(e.target.value)}
            />
          </div>

          <div className="flex items-end">
            <Button type="submit" size="sm" disabled={enviando}>
              {enviando ? 'Criando...' : 'Criar cargo'}
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
