import { Tooltip as Primitiva } from 'radix-ui'
import type { ComponentProps, ReactNode } from 'react'
import { cn } from '@/lib/utils'

export const ProvedorTooltip = Primitiva.Provider

/**
 * Tooltip so para o que ja tem nome acessivel por outro caminho.
 *
 * Ele NAO substitui aria-label: um botao icon-only precisa do nome mesmo que
 * o tooltip exista, porque leitor de tela e navegacao por teclado nao dependem
 * de hover.
 */
export function Tooltip({
  children,
  conteudo,
  lado = 'right',
  ...props
}: ComponentProps<typeof Primitiva.Root> & { children: ReactNode; conteudo: ReactNode; lado?: 'top' | 'right' | 'bottom' | 'left' }) {
  return (
    <Primitiva.Root delayDuration={400} {...props}>
      <Primitiva.Trigger asChild>{children}</Primitiva.Trigger>
      <Primitiva.Portal>
        <Primitiva.Content
          side={lado}
          sideOffset={8}
          className={cn(
            'z-50 rounded-md bg-foreground px-2 py-1 text-xs text-background shadow-md',
            'data-[state=delayed-open]:animate-in data-[state=delayed-open]:fade-in-0',
          )}
        >
          {conteudo}
        </Primitiva.Content>
      </Primitiva.Portal>
    </Primitiva.Root>
  )
}
