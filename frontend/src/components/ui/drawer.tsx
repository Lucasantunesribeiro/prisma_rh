import { Dialog as Primitiva } from 'radix-ui'
import { X } from 'lucide-react'
import type { ComponentProps, ReactNode } from 'react'
import { cn } from '@/lib/utils'

/**
 * Painel lateral para detalhe que nao merece pagina propria.
 *
 * Existe para a memoria de calculo: a conta de uma rubrica e informacao de
 * apoio, e mante-la permanentemente ao lado do holerite transformaria a tela
 * numa parede de numeros. No drawer, o fluxo fica natural - holerite, clicar
 * na rubrica, ver a conta, fechar.
 *
 * E um Dialog do Radix, e nao um <aside> caseiro: foco preso, Escape, retorno
 * de foco ao gatilho e aria-modal vem prontos e corretos.
 */
export const Drawer = Primitiva.Root
export const DrawerTrigger = Primitiva.Trigger
export const DrawerClose = Primitiva.Close

export function DrawerContent({
  className,
  children,
  titulo,
  descricao,
  ...props
}: ComponentProps<typeof Primitiva.Content> & { titulo: string; descricao?: ReactNode }) {
  return (
    <Primitiva.Portal>
      <Primitiva.Overlay
        className={cn(
          'fixed inset-0 z-50 bg-foreground/20',
          'data-[state=open]:animate-in data-[state=open]:fade-in-0',
          'data-[state=closed]:animate-out data-[state=closed]:fade-out-0',
        )}
      />
      <Primitiva.Content
        className={cn(
          'fixed inset-y-0 right-0 z-50 flex w-full max-w-xl flex-col border-l border-border bg-card shadow-lg',
          'data-[state=open]:animate-in data-[state=open]:slide-in-from-right',
          'data-[state=closed]:animate-out data-[state=closed]:slide-out-to-right',
          className,
        )}
        {...props}
      >
        <header className="flex items-start justify-between gap-4 border-b border-border px-6 py-4">
          <div className="min-w-0">
            <Primitiva.Title className="text-[15px] font-semibold tracking-tight">
              {titulo}
            </Primitiva.Title>
            {descricao && (
              <Primitiva.Description className="mt-0.5 text-[13px] text-muted-foreground">
                {descricao}
              </Primitiva.Description>
            )}
          </div>

          <Primitiva.Close
            aria-label="Fechar"
            className="-mr-1 rounded-md p-1.5 text-muted-foreground hover:bg-accent hover:text-foreground"
          >
            <X className="size-4" />
          </Primitiva.Close>
        </header>

        <div className="min-h-0 flex-1 overflow-y-auto px-6 py-5">{children}</div>
      </Primitiva.Content>
    </Primitiva.Portal>
  )
}
