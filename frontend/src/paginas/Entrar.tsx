import { useState, type FormEvent } from 'react'
import { Navigate } from 'react-router'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { useSessao } from '@/auth/useSessao'

export default function Entrar() {
  const { usuario, entrar, carregando } = useSessao()

  const [email, definirEmail] = useState('')
  const [senha, definirSenha] = useState('')
  const [erro, definirErro] = useState<string | null>(null)
  const [enviando, definirEnviando] = useState(false)

  if (carregando) {
    return null
  }

  if (usuario) {
    return <Navigate to="/empresas" replace />
  }

  const aoEnviar = async (evento: FormEvent) => {
    evento.preventDefault()
    definirErro(null)
    definirEnviando(true)

    try {
      await entrar(email, senha)
    } catch (falha) {
      // A mensagem vem do backend e e a MESMA para e-mail inexistente e senha
      // errada. Detalhar aqui entregaria quais e-mails existem.
      definirErro(falha instanceof Error ? falha.message : 'Não foi possível entrar.')
    } finally {
      definirEnviando(false)
    }
  }

  return (
    <main className="mx-auto flex min-h-screen w-full max-w-sm flex-col justify-center px-6">
      <header className="mb-6">
        <h1 className="text-2xl font-semibold tracking-tight">Prisma RH</h1>
        <p className="mt-1 text-sm text-muted-foreground">Entre com suas credenciais.</p>
      </header>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Acessar</CardTitle>
        </CardHeader>

        <CardContent>
          <form onSubmit={aoEnviar} className="flex flex-col gap-4" noValidate>
            <div className="flex flex-col gap-2">
              <Label htmlFor="email">E-mail</Label>
              <Input
                id="email"
                type="email"
                autoComplete="username"
                required
                value={email}
                onChange={(e) => definirEmail(e.target.value)}
              />
            </div>

            <div className="flex flex-col gap-2">
              <Label htmlFor="senha">Senha</Label>
              <Input
                id="senha"
                type="password"
                autoComplete="current-password"
                required
                value={senha}
                onChange={(e) => definirSenha(e.target.value)}
              />
            </div>

            {erro && (
              <Alert variant="destructive" role="alert">
                <AlertDescription>{erro}</AlertDescription>
              </Alert>
            )}

            <Button type="submit" disabled={enviando}>
              {enviando ? 'Entrando...' : 'Entrar'}
            </Button>
          </form>
        </CardContent>
      </Card>
    </main>
  )
}
