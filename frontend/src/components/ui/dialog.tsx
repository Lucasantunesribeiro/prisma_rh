import { Dialog as Primitiva } from 'radix-ui'
import type { ComponentProps, ReactNode } from 'react'
import { cn } from '@/lib/utils'

/**
 * Confirmacao para acao com consequencia real: fechar folha, desligar
 * contrato, inativar.
 *
 * Nao usar para acao banal - um modal por clique treina o usuario a confirmar
 * sem ler, e ai ele confirma o que importava tambem.
 */
export const Dialog = Primitiva.Root
export const DialogTrigger = Primitiva.Trigger
export const DialogClose = Primitiva.Close

export function DialogContent({
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
          'fixed left-1/2 top-1/2 z-50 w-full max-w-md -translate-x-1/2 -translate-y-1/2',
          'rounded-lg border border-border bg-card p-6 shadow-lg',
          'data-[state=open]:animate-in data-[state=open]:fade-in-0 data-[state=open]:zoom-in-95',
          className,
        )}
        {...props}
      >
        <Primitiva.Title className="text-[15px] font-semibold tracking-tight">
          {titulo}
        </Primitiva.Title>

        {descricao && (
          <Primitiva.Description className="mt-2 text-[13px] leading-relaxed text-muted-foreground">
            {descricao}
          </Primitiva.Description>
        )}

        <div className="mt-5 flex justify-end gap-2">{children}</div>
      </Primitiva.Content>
    </Primitiva.Portal>
  )
}
