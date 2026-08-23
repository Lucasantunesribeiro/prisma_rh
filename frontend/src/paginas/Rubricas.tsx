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
import {
  criarRubrica,
  inativarRubrica,
  listarRubricas,
  ROTULO_TIPO_RUBRICA,
  type Rubrica,
  type TipoRubrica,
} from '@/api/folha'
import { podeAdministrar } from '@/api/autenticacao'
import { useSessao } from '@/auth/useSessao'

type Estado =
  | { situacao: 'carregando' }
  | { situacao: 'pronto'; rubricas: Rubrica[] }
  | { situacao: 'erro'; mensagem: string }

export default function Rubricas() {
  const { usuario } = useSessao()
  const administra = podeAdministrar(usuario?.perfil)

  const [estado, definirEstado] = useState<Estado>({ situacao: 'carregando' })

  const carregar = useCallback(async () => {
    try {
      definirEstado({ situacao: 'pronto', rubricas: await listarRubricas() })
    } catch (falha) {
      definirEstado({
        situacao: 'erro',
        mensagem: falha instanceof Error ? falha.message : 'Falha ao carregar rubricas.',
      })
    }
  }, [])

  useEffect(() => {
    // O estado só muda DEPOIS do await, quando a resposta da API chega.
    // oxlint-disable-next-line react/set-state-in-effect
    void carregar()
  }, [carregar])

  const temSalario =
    estado.situacao === 'pronto' &&
    estado.rubricas.some((r) => r.ativa && r.estrategia === 'SalarioBaseProporcional')

  return (
    <main className="mx-auto w-full max-w-4xl px-6 py-8">
      <header className="mb-6">
        <h1 className="text-xl font-semibold tracking-tight">Rubricas</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Os eventos que podem aparecer numa folha. O salário-base é calculado pelo sistema; o
          resto é digitado no lançamento.
        </p>
      </header>

      {estado.situacao === 'pronto' && !temSalario && (
        <Alert className="mb-6">
          <AlertDescription>
            Nenhuma rubrica de salário-base ativa. Sem ela a folha não tem o que calcular.
          </AlertDescription>
        </Alert>
      )}

      {administra && <FormularioNovaRubrica temSalario={temSalario} aoCriar={carregar} />}

      <Card>
        <CardHeader>
          <CardTitle className="text-base">
            {estado.situacao === 'pronto' ? `${estado.rubricas.length} rubrica(s)` : 'Rubricas'}
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

          {estado.situacao === 'pronto' && estado.rubricas.length === 0 && (
            <p className="py-2 text-sm text-muted-foreground">
              Nenhuma rubrica cadastrada.
            </p>
          )}

          {estado.situacao === 'pronto' && estado.rubricas.length > 0 && (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Código</TableHead>
                  <TableHead>Nome</TableHead>
                  <TableHead>Tipo</TableHead>
                  <TableHead>Origem do valor</TableHead>
                  <TableHead>Situação</TableHead>
                  {administra && <TableHead className="text-right">Ação</TableHead>}
                </TableRow>
              </TableHeader>
              <TableBody>
                {estado.rubricas.map((rubrica) => (
                  <TableRow key={rubrica.id}>
                    <TableCell className="font-mono text-xs">{rubrica.codigo}</TableCell>
                    <TableCell className="font-medium">{rubrica.nome}</TableCell>
                    <TableCell>{ROTULO_TIPO_RUBRICA[rubrica.tipo]}</TableCell>
                    <TableCell className="text-sm text-muted-foreground">
                      {rubrica.estrategia === 'SalarioBaseProporcional'
                        ? 'calculado pelo sistema'
                        : 'digitado no lançamento'}
                    </TableCell>
                    <TableCell>
                      <Badge variant={rubrica.ativa ? 'default' : 'secondary'}>
                        {rubrica.ativa ? 'ativa' : 'inativa'}
                      </Badge>
                    </TableCell>
                    {administra && (
                      <TableCell className="text-right">
                        {rubrica.ativa && (
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={() => {
                              void inativarRubrica(rubrica.id).then(carregar)
                            }}
                          >
                            Inativar
                          </Button>
                        )}
                      </TableCell>
                    )}
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

function FormularioNovaRubrica({
  temSalario,
  aoCriar,
}: {
  temSalario: boolean
  aoCriar: () => Promise<void>
}) {
  const [codigo, definirCodigo] = useState('')
  const [nome, definirNome] = useState('')
  const [tipo, definirTipo] = useState<TipoRubrica>('Provento')
  const [salarioBase, definirSalarioBase] = useState(false)
  const [erro, definirErro] = useState<string | null>(null)
  const [enviando, definirEnviando] = useState(false)

  const aoEnviar = async (evento: FormEvent) => {
    evento.preventDefault()
    definirErro(null)
    definirEnviando(true)

    try {
      await criarRubrica({
        codigo,
        nome,
        tipo: salarioBase ? 'Provento' : tipo,
        estrategia: salarioBase ? 'SalarioBaseProporcional' : 'ValorInformado',
      })
      definirCodigo('')
      definirNome('')
      definirSalarioBase(false)
      await aoCriar()
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Falha ao criar rubrica.')
    } finally {
      definirEnviando(false)
    }
  }

  return (
    <Card className="mb-6">
      <CardHeader>
        <CardTitle className="text-base">Nova rubrica</CardTitle>
      </CardHeader>
      <CardContent>
        <form onSubmit={aoEnviar} className="grid items-end gap-4 sm:grid-cols-4" noValidate>
          <div className="flex flex-col gap-2">
            <Label htmlFor="codigoRubrica">Código</Label>
            <Input
              id="codigoRubrica"
              required
              placeholder="VT"
              value={codigo}
              onChange={(e) => definirCodigo(e.target.value)}
            />
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="nomeRubrica">Nome</Label>
            <Input
              id="nomeRubrica"
              required
              value={nome}
              onChange={(e) => definirNome(e.target.value)}
            />
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="tipoRubrica">Tipo</Label>
            <select
              id="tipoRubrica"
              className="h-9 rounded-md border border-input bg-transparent px-3 py-1 text-sm shadow-xs disabled:opacity-50"
              value={salarioBase ? 'Provento' : tipo}
              disabled={salarioBase}
              onChange={(e) => definirTipo(e.target.value as TipoRubrica)}
            >
              <option value="Provento">provento</option>
              <option value="Desconto">desconto</option>
              <option value="Informativo">informativo</option>
            </select>
          </div>

          <div className="flex items-center gap-2 pb-2">
            <input
              id="salarioBase"
              type="checkbox"
              className="size-4"
              checked={salarioBase}
              disabled={temSalario}
              onChange={(e) => definirSalarioBase(e.target.checked)}
            />
            <Label htmlFor="salarioBase">
              Salário-base
              {temSalario && (
                <span className="ml-1 text-xs font-normal text-muted-foreground">(já existe)</span>
              )}
            </Label>
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
              {enviando ? 'Criando...' : 'Criar rubrica'}
            </Button>
          </div>
        </form>
      </CardContent>
    </Card>
  )
}
