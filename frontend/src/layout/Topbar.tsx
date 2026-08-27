import { ChevronRight } from 'lucide-react'
import { Fragment } from 'react'
import { Link } from 'react-router'
import type { Trilha } from './contexto'

/**
 * Topbar deliberadamente magra: breadcrumb à esquerda e o contexto de
 * competência à direita, quando houver.
 *
 * Não tem busca global nem sino de notificação porque nenhuma das duas coisas
 * existe no sistema. Ícone que não faz nada ocupa espaço, sugere função
 * inexistente e é a marca registrada de interface montada para a foto.
 */
export function Topbar({
  trilha,
  competencia,
}: {
  trilha: Trilha[]
  competencia: string | null
}) {
  return (
    <header
      className="flex shrink-0 items-center justify-between gap-4 border-b border-border bg-card px-6"
      style={{ height: 'var(--altura-topbar)' }}
    >
      <nav aria-label="Trilha de navegação" className="min-w-0">
        <ol className="flex items-center gap-2 text-[13px]">
          {trilha.map((item, indice) => {
            const ultimo = indice === trilha.length - 1

            return (
              <Fragment key={`${item.texto}-${indice}`}>
                {indice > 0 && (
                  <ChevronRight className="size-3.5 shrink-0 text-muted-foreground/60" aria-hidden />
                )}
                <li className="min-w-0">
                  {item.para && !ultimo ? (
                    <Link
                      to={item.para}
                      className="truncate text-muted-foreground hover:text-foreground hover:underline"
                    >
                      {item.texto}
                    </Link>
                  ) : (
                    <span
                      className={
                        ultimo ? 'truncate font-semibold' : 'truncate text-muted-foreground'
                      }
                      aria-current={ultimo ? 'page' : undefined}
                    >
                      {item.texto}
                    </span>
                  )}
                </li>
              </Fragment>
            )
          })}
        </ol>
      </nav>

      {competencia && (
        <p className="shrink-0 text-[13px] text-muted-foreground">
          Competência <span className="font-medium text-foreground">{competencia}</span>
        </p>
      )}
    </header>
  )
}
