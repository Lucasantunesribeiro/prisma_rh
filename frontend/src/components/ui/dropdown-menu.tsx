import { DropdownMenu as Primitiva } from 'radix-ui'
import type { ComponentProps } from 'react'
import { cn } from '@/lib/utils'

export const DropdownMenu = Primitiva.Root
export const DropdownMenuTrigger = Primitiva.Trigger

export function DropdownMenuContent({
  className,
  sideOffset = 6,
  ...props
}: ComponentProps<typeof Primitiva.Content>) {
  return (
    <Primitiva.Portal>
      <Primitiva.Content
        sideOffset={sideOffset}
        className={cn(
          'z-50 min-w-[12rem] overflow-hidden rounded-md border border-border bg-popover p-1',
          'text-popover-foreground shadow-md',
          'data-[state=open]:animate-in data-[state=closed]:animate-out',
          'data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0',
          className,
        )}
        {...props}
      />
    </Primitiva.Portal>
  )
}

export function DropdownMenuItem({
  className,
  variant = 'padrao',
  ...props
}: ComponentProps<typeof Primitiva.Item> & { variant?: 'padrao' | 'destrutivo' }) {
  return (
    <Primitiva.Item
      className={cn(
        'relative flex cursor-default select-none items-center gap-2 rounded-sm px-2 py-1.5 text-sm outline-none',
        'focus:bg-accent focus:text-accent-foreground',
        'data-[disabled]:pointer-events-none data-[disabled]:opacity-50',
        '[&_svg]:size-4 [&_svg]:shrink-0',
        variant === 'destrutivo' && 'text-destructive focus:bg-critico-suave focus:text-destructive',
        className,
      )}
      {...props}
    />
  )
}

export function DropdownMenuLabel({ className, ...props }: ComponentProps<typeof Primitiva.Label>) {
  return (
    <Primitiva.Label
      className={cn('px-2 py-1.5 text-xs font-medium text-muted-foreground', className)}
      {...props}
    />
  )
}

export function DropdownMenuSeparator({
  className,
  ...props
}: ComponentProps<typeof Primitiva.Separator>) {
  return <Primitiva.Separator className={cn('-mx-1 my-1 h-px bg-border', className)} {...props} />
}
