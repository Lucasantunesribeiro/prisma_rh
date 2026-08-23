import { Navigate, Outlet } from 'react-router'
import { useSessao } from '@/auth/useSessao'

/**
 * Guarda de rota. Adapta a NAVEGACAO, nao a autorizacao: quem decide o que cada
 * perfil pode fazer e o backend. Aqui so evitamos mostrar uma tela que voltaria
 * 401 ou 403 de qualquer jeito.
 */
export function RotaProtegida() {
  const { usuario, carregando } = useSessao()

  if (carregando) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <p className="text-sm text-muted-foreground" role="status">
          Restaurando sessão...
        </p>
      </div>
    )
  }

  return usuario ? <Outlet /> : <Navigate to="/entrar" replace />
}
