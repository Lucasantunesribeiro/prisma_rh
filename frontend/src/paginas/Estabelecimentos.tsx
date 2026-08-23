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
  criarEstabelecimento,
  listarEstabelecimentos,
  type Empresa,
  type Estabelecimento,
} from '@/api/empresas'

type Estado =
  | { situacao: 'carregando' }
  | { situacao: 'pronto'; itens: Estabelecimento[] }
  | { situacao: 'erro'; mensagem: string }

export function Estabelecimentos({
  empresa,
  podeAdministrar,
}: {
  empresa: Empresa
  podeAdministrar: boolean
}) {
  const [estado, definirEstado] = useState<Estado>({ situacao: 'carregando' })

  const carregar = useCallback(async () => {
    try {
      definirEstado({ situacao: 'pronto', itens: await listarEstabelecimentos(empresa.id) })
    } catch (falha) {
      definirEstado({
        situacao: 'erro',
        mensagem: falha instanceof Error ? falha.message : 'Falha ao carregar estabelecimentos.',
      })
    }
  }, [empresa.id])

  useEffect(() => {
    // O estado so muda DEPOIS do await, quando a resposta da API chega. A regra
    // nao distingue isso de um setState sincrono, e buscar dados no effect e o
    // padrao do React sem biblioteca de dados - que a Fase 1 nao justifica.
    // oxlint-disable-next-line react/set-state-in-effect
    void carregar()
  }, [carregar])

  return (
    <Card className="mt-6">
      <CardHeader>
        <CardTitle className="text-base">Estabelecimentos de {empresa.razaoSocial}</CardTitle>
      </CardHeader>

      <CardContent aria-live="polite" aria-busy={estado.situacao === 'carregando'}>
        {podeAdministrar && <FormularioNovo idEmpresa={empresa.id} aoCriar={carregar} />}

        {estado.situacao === 'carregando' && (
          <p className="py-2 text-sm text-muted-foreground">Carregando...</p>
        )}

        {estado.situacao === 'erro' && (
          <div>
            <Alert variant="destructive" role="alert">
              <AlertDescription>{estado.mensagem}</AlertDescription>
            </Alert>
            <Button className="mt-4" variant="outline" size="sm" onClick={() => {
                  definirEstado({ situacao: 'carregando' })
                  void carregar()
                }}>
              Tentar novamente
            </Button>
          </div>
        )}

        {estado.situacao === 'pronto' && estado.itens.length === 0 && (
          <p className="py-2 text-sm text-muted-foreground">
            Esta empresa ainda não tem estabelecimentos.
          </p>
        )}

        {estado.situacao === 'pronto' && estado.itens.length > 0 && (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Código</TableHead>
                <TableHead>Nome</TableHead>
                <TableHead>Situação</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {estado.itens.map((item) => (
                <TableRow key={item.id}>
                  <TableCell className="font-mono text-xs">{item.codigo}</TableCell>
                  <TableCell>{item.nome}</TableCell>
                  <TableCell>
                    <Badge variant={item.ativo ? 'default' : 'secondary'}>
                      {item.ativo ? 'ativo' : 'inativo'}
                    </Badge>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </CardContent>
    </Card>
  )
}

function FormularioNovo({
  idEmpresa,
  aoCriar,
}: {
  idEmpresa: string
  aoCriar: () => Promise<void>
}) {
  const [codigo, definirCodigo] = useState('')
  const [nome, definirNome] = useState('')
  const [erro, definirErro] = useState<string | null>(null)
  const [enviando, definirEnviando] = useState(false)

  const aoEnviar = async (evento: FormEvent) => {
    evento.preventDefault()
    definirErro(null)
    definirEnviando(true)

    try {
      await criarEstabelecimento(idEmpresa, { codigo, nome })
      definirCodigo('')
      definirNome('')
      await aoCriar()
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Falha ao criar estabelecimento.')
    } finally {
      definirEnviando(false)
    }
  }

  return (
    <form onSubmit={aoEnviar} className="mb-6 grid gap-4 sm:grid-cols-3" noValidate>
      <div className="flex flex-col gap-2">
        <Label htmlFor="codigo">Código</Label>
        <Input
          id="codigo"
          required
          value={codigo}
          onChange={(e) => definirCodigo(e.target.value)}
        />
      </div>

      <div className="flex flex-col gap-2">
        <Label htmlFor="nomeEstabelecimento">Nome</Label>
        <Input
          id="nomeEstabelecimento"
          required
          value={nome}
          onChange={(e) => definirNome(e.target.value)}
        />
      </div>

      <div className="flex items-end">
        <Button type="submit" size="sm" disabled={enviando}>
          {enviando ? 'Criando...' : 'Adicionar'}
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
  )
}
