import { useState, type FormEvent } from 'react'
import { Navigate } from 'react-router'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { useSessao } from '@/auth/useSessao'

/**
 * A conta pública da demonstração.
 *
 * ## Por que ela existe
 *
 * Um recrutador abria o site, via um formulário e **não tinha como entrar** —
 * a aplicação estava no ar e ninguém conseguia ver nada dela. Para um
 * portfólio, isso é pior que qualquer defeito de código.
 *
 * ## Por que é seguro publicar esta credencial
 *
 * ⚠️ **Só porque a conta é `Visualizador` e não escreve nada.** A autorização é
 * do backend (`CLAUDE.md §24.4`), e há teste provando que este perfil recebe
 * **403** em toda operação de escrita — cadastrar, editar, excluir, calcular,
 * fechar folha, mudar workflow, alterar regra e importar.
 *
 * Nenhuma credencial de Administrador da Plataforma, Administrador da Empresa,
 * Analista de RH ou Auditor é exposta.
 *
 * A senha chega por variável do Vite, que **termina pública no bundle** — e
 * isso é aceito aqui, e só aqui, porque a credencial é intencionalmente
 * pública. Ela não pode ser reaproveitada em nenhum outro usuário ou ambiente.
 *
 * ## O que este botão NÃO é
 *
 * Não é bypass. Não há endpoint de login-demo, nem tratamento privilegiado no
 * backend: o botão preenche os campos e usa **o mesmo fluxo de sempre** —
 * mesmo `entrar`, mesmo access token, mesmo refresh, mesmo rate limiting.
 */
const CONTA_DEMO = {
  email: 'visualizador@prisma.exemplo',
  senha: import.meta.env.VITE_DEMO_SENHA as string | undefined,
}

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

  const entrarComoDemonstracao = async () => {
    if (!CONTA_DEMO.senha) return

    definirEmail(CONTA_DEMO.email)
    definirSenha(CONTA_DEMO.senha)
    definirErro(null)
    definirEnviando(true)

    try {
      // Mesmo fluxo do formulario. Nada de especial acontece no backend.
      await entrar(CONTA_DEMO.email, CONTA_DEMO.senha)
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Não foi possível entrar.')
    } finally {
      definirEnviando(false)
    }
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

        {CONTA_DEMO.senha && (
          <section
            aria-labelledby="demonstracao"
            className="mt-7 rounded-lg border border-dashed border-border bg-muted/40 p-4"
          >
            <h2 id="demonstracao" className="text-[13px] font-semibold text-foreground">
              Demonstração
            </h2>

            <p className="mt-1 text-[12px] leading-relaxed text-muted-foreground">
              Entre com uma conta pública de <strong>somente leitura</strong> e explore
              folhas calculadas, memória de cálculo, conferência e auditoria — com dados
              fictícios.
            </p>

            <p className="mt-2 font-mono text-[12px] text-foreground">{CONTA_DEMO.email}</p>

            <Button
              type="button"
              variant="outline"
              className="mt-3 w-full"
              disabled={enviando}
              onClick={entrarComoDemonstracao}
            >
              {enviando ? 'Entrando...' : 'Entrar na demonstração'}
            </Button>
          </section>
        )}

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
