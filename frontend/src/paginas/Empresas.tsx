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
import { criarEmpresa, listarEmpresas, type Empresa } from '@/api/empresas'
import { podeAdministrar } from '@/api/autenticacao'
import { useSessao } from '@/auth/useSessao'
import { Estabelecimentos } from './Estabelecimentos'

type Estado =
  | { situacao: 'carregando' }
  | { situacao: 'pronto'; empresas: Empresa[] }
  | { situacao: 'erro'; mensagem: string }

export default function Empresas() {
  const { usuario } = useSessao()
  const administra = podeAdministrar(usuario?.perfil)

  const [estado, definirEstado] = useState<Estado>({ situacao: 'carregando' })
  const [selecionada, definirSelecionada] = useState<Empresa | null>(null)

  const carregar = useCallback(async () => {
    try {
      const pagina = await listarEmpresas()
      definirEstado({ situacao: 'pronto', empresas: pagina.itens })
    } catch (falha) {
      definirEstado({
        situacao: 'erro',
        mensagem: falha instanceof Error ? falha.message : 'Falha ao carregar empresas.',
      })
    }
  }, [])

  useEffect(() => {
    // O estado so muda DEPOIS do await, quando a resposta da API chega. A regra
    // nao distingue isso de um setState sincrono, e buscar dados no effect e o
    // padrao do React sem biblioteca de dados - que a Fase 1 nao justifica.
    // oxlint-disable-next-line react/set-state-in-effect
    void carregar()
  }, [carregar])

  return (
    <main className="mx-auto w-full max-w-5xl px-6 py-8">
      <header className="mb-6">
        <h1 className="text-xl font-semibold tracking-tight">Empresas</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Empresas administradas pela sua organização.
        </p>
      </header>

      {administra && <FormularioNovaEmpresa aoCriar={carregar} />}

      <Card>
        <CardHeader>
          <CardTitle className="text-base">
            {estado.situacao === 'pronto' ? `${estado.empresas.length} empresa(s)` : 'Empresas'}
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
              <Button className="mt-4" variant="outline" size="sm" onClick={() => {
                  definirEstado({ situacao: 'carregando' })
                  void carregar()
                }}>
                Tentar novamente
              </Button>
            </div>
          )}

          {estado.situacao === 'pronto' && estado.empresas.length === 0 && (
            <p className="py-2 text-sm text-muted-foreground">
              Nenhuma empresa cadastrada ainda.
              {administra
                ? ' Use o formulário acima para criar a primeira.'
                : ' Peça a um administrador para cadastrar.'}
            </p>
          )}

          {estado.situacao === 'pronto' && estado.empresas.length > 0 && (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Razão social</TableHead>
                  <TableHead>CNPJ</TableHead>
                  <TableHead>Situação</TableHead>
                  <TableHead className="text-right">Estabelecimentos</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {estado.empresas.map((empresa) => (
                  <TableRow key={empresa.id}>
                    <TableCell>
                      <span className="font-medium">{empresa.razaoSocial}</span>
                      {empresa.nomeFantasia && (
                        <span className="block text-xs text-muted-foreground">
                          {empresa.nomeFantasia}
                        </span>
                      )}
                    </TableCell>
                    <TableCell className="font-mono text-xs">{empresa.cnpjFormatado}</TableCell>
                    <TableCell>
                      <Badge variant={empresa.ativa ? 'default' : 'secondary'}>
                        {empresa.ativa ? 'ativa' : 'inativa'}
                      </Badge>
                    </TableCell>
                    <TableCell className="text-right">
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={() =>
                          definirSelecionada(selecionada?.id === empresa.id ? null : empresa)
                        }
                      >
                        {selecionada?.id === empresa.id ? 'Fechar' : 'Ver'}
                      </Button>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>

      {selecionada && (
        <Estabelecimentos
          key={selecionada.id}
          empresa={selecionada}
          podeAdministrar={administra}
        />
      )}
    </main>
  )
}

function FormularioNovaEmpresa({ aoCriar }: { aoCriar: () => Promise<void> }) {
  const [razaoSocial, definirRazaoSocial] = useState('')
  const [cnpj, definirCnpj] = useState('')
  const [nomeFantasia, definirNomeFantasia] = useState('')
  const [erro, definirErro] = useState<string | null>(null)
  const [enviando, definirEnviando] = useState(false)

  const aoEnviar = async (evento: FormEvent) => {
    evento.preventDefault()
    definirErro(null)
    definirEnviando(true)

    try {
      await criarEmpresa({ razaoSocial, cnpj, nomeFantasia: nomeFantasia || null })
      definirRazaoSocial('')
      definirCnpj('')
      definirNomeFantasia('')
      await aoCriar()
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Falha ao criar empresa.')
    } finally {
      definirEnviando(false)
    }
  }

  return (
    <Card className="mb-6">
      <CardHeader>
        <CardTitle className="text-base">Nova empresa</CardTitle>
      </CardHeader>
      <CardContent>
        <form onSubmit={aoEnviar} className="grid gap-4 sm:grid-cols-3" noValidate>
          <div className="flex flex-col gap-2">
            <Label htmlFor="razaoSocial">Razão social</Label>
            <Input
              id="razaoSocial"
              required
              value={razaoSocial}
              onChange={(e) => definirRazaoSocial(e.target.value)}
            />
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="cnpj">CNPJ</Label>
            <Input
              id="cnpj"
              required
              placeholder="00.000.000/0000-00"
              value={cnpj}
              onChange={(e) => definirCnpj(e.target.value)}
            />
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor="nomeFantasia">Nome fantasia</Label>
            <Input
              id="nomeFantasia"
              value={nomeFantasia}
              onChange={(e) => definirNomeFantasia(e.target.value)}
            />
          </div>

          {erro && (
            <div className="sm:col-span-3">
              <Alert variant="destructive" role="alert">
                <AlertDescription>{erro}</AlertDescription>
              </Alert>
            </div>
          )}

          <div className="sm:col-span-3">
            <Button type="submit" size="sm" disabled={enviando}>
              {enviando ? 'Criando...' : 'Criar empresa'}
            </Button>
          </div>
        </form>
      </CardContent>
    </Card>
  )
}
