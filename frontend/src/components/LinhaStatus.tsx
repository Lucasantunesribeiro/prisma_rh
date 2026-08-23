import { Badge } from '@/components/ui/badge'
import type { StatusSaude } from '@/api/saude'

const ROTULOS: Record<StatusSaude, string> = {
  saudavel: 'disponível',
  degradado: 'degradado',
  indisponivel: 'indisponível',
}

const VARIANTES: Record<StatusSaude, 'default' | 'secondary' | 'destructive'> = {
  saudavel: 'default',
  degradado: 'secondary',
  indisponivel: 'destructive',
}

export function LinhaStatus({ rotulo, status }: { rotulo: string; status: StatusSaude }) {
  return (
    <div className="flex items-center justify-between gap-4 border-b border-border py-2.5 last:border-b-0">
      <span className="text-sm text-muted-foreground">{rotulo}</span>
      <Badge variant={VARIANTES[status]}>{ROTULOS[status]}</Badge>
    </div>
  )
}
