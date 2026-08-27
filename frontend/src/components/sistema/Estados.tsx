import { AlertCircle, Inbox, Loader2, Lock, SearchX } from 'lucide-react'
import type { ReactNode } from 'react'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'

/**
 * Os estados que toda tela de dado tem: carregando, vazio, sem resultado, sem
 * permissao e erro.
 *
 * Existem como componentes porque a alternativa e cada pagina inventar o seu -
 * e foi o que aconteceu antes: quatro telas, quatro jeitos de dizer
 * "carregando". A mensagem tambem e sempre especifica: "Nao foi possivel
 * carregar os funcionarios" diz o que fazer; "Ops, algo deu errado" nao diz
 * nada e ainda soa infantil num sistema de folha.
 */

function Moldura({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <div
      className={cn(
        'flex min-h-[280px] flex-col items-center justify-center gap-3 px-6 py-12 text-center',
        className,
      )}
    >
      {children}
    </div>
  )
}

export function EstadoCarregando({ rotulo = 'Carregando' }: { rotulo?: string }) {
  return (
    <Moldura>
      <Loader2 className="size-5 animate-spin text-muted-foreground" aria-hidden />
      <p className="text-[13px] text-muted-foreground" role="status">
        {rotulo}...
      </p>
    </Moldura>
  )
}

/**
 * Esqueleto de tabela. Preferido ao spinner quando a forma do resultado ja e
 * conhecida: o olho ja se posiciona nas colunas enquanto o dado chega.
 */
export function EsqueletoTabela({ linhas = 6, colunas = 5 }: { linhas?: number; colunas?: number }) {
  return (
    <div className="divide-y divide-border" aria-hidden>
      {Array.from({ length: linhas }, (_, linha) => (
        <div key={linha} className="flex items-center gap-4 px-4 py-3">
          {Array.from({ length: colunas }, (_, coluna) => (
            <div
              key={coluna}
              className="h-3 animate-pulse rounded bg-muted"
              style={{ width: coluna === 0 ? '22%' : `${Math.max(8, 18 - coluna * 2)}%` }}
            />
          ))}
        </div>
      ))}
    </div>
  )
}

export function EstadoVazio({
  titulo,
  descricao,
  acao,
}: {
  titulo: string
  descricao?: string
  acao?: ReactNode
}) {
  return (
    <Moldura>
      <Inbox className="size-6 text-muted-foreground/70" aria-hidden />
      <div className="max-w-sm space-y-1">
        <p className="text-sm font-medium">{titulo}</p>
        {descricao && <p className="text-[13px] text-muted-foreground">{descricao}</p>}
      </div>
      {acao}
    </Moldura>
  )
}

export function EstadoSemResultado({ aoLimpar }: { aoLimpar?: () => void }) {
  return (
    <Moldura>
      <SearchX className="size-6 text-muted-foreground/70" aria-hidden />
      <div className="max-w-sm space-y-1">
        <p className="text-sm font-medium">Nenhum resultado</p>
        <p className="text-[13px] text-muted-foreground">
          Nenhum registro corresponde aos filtros aplicados.
        </p>
      </div>
      {aoLimpar && (
        <Button variant="outline" size="sm" onClick={aoLimpar}>
          Limpar filtros
        </Button>
      )}
    </Moldura>
  )
}

export function EstadoSemPermissao({ recurso }: { recurso: string }) {
  return (
    <Moldura>
      <Lock className="size-6 text-muted-foreground/70" aria-hidden />
      <div className="max-w-sm space-y-1">
        <p className="text-sm font-medium">Sem acesso</p>
        <p className="text-[13px] text-muted-foreground">
          Seu perfil não tem acesso a {recurso}.
        </p>
      </div>
    </Moldura>
  )
}

export function EstadoErro({
  mensagem,
  aoTentarNovamente,
}: {
  mensagem: string
  aoTentarNovamente?: () => void
}) {
  return (
    <Moldura>
      <AlertCircle className="size-6 text-destructive" aria-hidden />
      <div className="max-w-md space-y-1">
        <p className="text-sm font-medium" role="alert">
          {mensagem}
        </p>
      </div>
      {aoTentarNovamente && (
        <Button variant="outline" size="sm" onClick={aoTentarNovamente}>
          Tentar novamente
        </Button>
      )}
    </Moldura>
  )
}
