import { useState, type FormEvent } from 'react'
import { Navigate } from 'react-router'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { useSessao } from '@/auth/useSessao'

/**
 * Login.
 *
 * Uma coluna centrada, sem painel decorativo, sem ilustração e sem gradiente:
 * a tela existe para o usuário digitar duas coisas e entrar. Metade da tela
 * ocupada por arte é metade da tela que não ajuda ninguém a trabalhar — e
 * numa ferramenta usada todo dia, ela cansa muito antes de impressionar.
 */
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
    return <Navigate to="/funcionarios" replace />
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
    <main className="flex min-h-screen items-center justify-center bg-background px-6 py-12">
      <div className="w-full max-w-[22rem]">
        <div className="mb-7 flex items-center gap-2.5">
          <span
            aria-hidden
            className="grid size-7 place-items-center rounded bg-primary text-xs font-semibold text-primary-foreground"
          >
            P
          </span>
          <span className="text-[15px] font-semibold tracking-tight">Prisma RH</span>
        </div>

        <h1 className="text-[22px] font-semibold leading-tight tracking-tight">
          Acessar Prisma RH
        </h1>
        <p className="mt-1.5 text-[13px] text-muted-foreground">
          Gestão, cálculo e conferência de folha de pagamento.
        </p>

        <form onSubmit={aoEnviar} className="mt-7 space-y-4" noValidate>
          <div className="space-y-1.5">
            <Label htmlFor="email">E-mail</Label>
            <Input
              id="email"
              type="email"
              autoComplete="username"
              required
              autoFocus
              value={email}
              onChange={(e) => definirEmail(e.target.value)}
            />
          </div>

          <div className="space-y-1.5">
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

          <Button type="submit" className="w-full" disabled={enviando}>
            {enviando ? 'Entrando...' : 'Entrar'}
          </Button>
        </form>

        {/*
         * Sinal de confiança discreto, e não slogan: diz como a sessão é
         * tratada, que é o que importa a quem digita a senha.
         */}
        <p className="mt-6 text-center text-xs text-muted-foreground">
          Sessão protegida. O acesso expira automaticamente por inatividade.
        </p>
      </div>
    </main>
  )
}
