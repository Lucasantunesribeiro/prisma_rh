import { Link, Outlet } from 'react-router'
import { Button } from '@/components/ui/button'
import { ROTULO_PERFIL } from '@/api/autenticacao'
import { useSessao } from '@/auth/useSessao'

export default function Layout() {
  const { usuario, sair } = useSessao()

  return (
    <div className="min-h-screen">
      <header className="border-b border-border">
        <div className="mx-auto flex w-full max-w-5xl items-center justify-between gap-4 px-6 py-3">
          <div className="flex items-baseline gap-6">
            <Link to="/empresas" className="text-sm font-semibold tracking-tight">
              Prisma RH
            </Link>
            <nav className="flex gap-4 text-sm text-muted-foreground">
              <Link to="/empresas" className="hover:text-foreground">
                Empresas
              </Link>
              <Link to="/status" className="hover:text-foreground">
                Status
              </Link>
            </nav>
          </div>

          <div className="flex items-center gap-3">
            {usuario && (
              <span className="text-right text-xs text-muted-foreground">
                <span className="block font-medium text-foreground">{usuario.nome}</span>
                {ROTULO_PERFIL[usuario.perfil]}
              </span>
            )}
            <Button variant="outline" size="sm" onClick={() => void sair()}>
              Sair
            </Button>
          </div>
        </div>
      </header>

      <Outlet />
    </div>
  )
}
