import { Search } from 'lucide-react'
import type { ReactNode } from 'react'
import { Input } from '@/components/ui/input'
import { cn } from '@/lib/utils'

/**
 * Peças pequenas e repetidas do sistema visual. Ficam juntas de propósito: são
 * componentes de uma ou duas dezenas de linhas cuja separação em arquivos
 * próprios só criaria navegação sem ganho.
 */

// ---------------------------------------------------------------- cabecalhos

/**
 * O cabecalho de toda pagina. UMA acao principal, no maximo.
 *
 * Duas acoes primarias lado a lado nao tem hierarquia: o usuario para para
 * decidir qual e a certa, toda vez que abre a tela.
 */
export function CabecalhoPagina({
  titulo,
  descricao,
  acao,
  meta,
}: {
  titulo: string
  descricao?: string
  acao?: ReactNode
  meta?: ReactNode
}) {
  return (
    <header className="mb-7 flex flex-wrap items-start justify-between gap-4">
      <div className="min-w-0 space-y-1.5">
        <h1 className="text-[26px] font-semibold leading-none tracking-[-0.02em]">{titulo}</h1>
        {descricao && <p className="text-[13.5px] text-muted-foreground">{descricao}</p>}
        {meta}
      </div>
      {acao && <div className="flex shrink-0 items-center gap-2">{acao}</div>}
    </header>
  )
}

/** Divisor de secao dentro de uma pagina ou de um formulario. */
export function CabecalhoSecao({
  titulo,
  descricao,
  acao,
  className,
}: {
  titulo: string
  descricao?: string
  acao?: ReactNode
  className?: string
}) {
  return (
    <div className={cn('mb-3 flex items-end justify-between gap-4', className)}>
      <div className="min-w-0">
        <h2 className="rotulo-secao">{titulo}</h2>
        {descricao && <p className="mt-1 text-[13px] text-muted-foreground">{descricao}</p>}
      </div>
      {acao}
    </div>
  )
}

// --------------------------------------------------------------------- busca

export function CampoBusca({
  valor,
  aoMudar,
  placeholder,
  rotulo,
  className,
}: {
  valor: string
  aoMudar: (valor: string) => void
  placeholder: string
  rotulo: string
  className?: string
}) {
  return (
    <div className={cn('relative w-full max-w-xs', className)}>
      <Search
        className="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
        aria-hidden
      />
      <Input
        type="search"
        aria-label={rotulo}
        value={valor}
        placeholder={placeholder}
        onChange={(e) => aoMudar(e.target.value)}
        className="h-8 pl-8 text-[13px]"
      />
    </div>
  )
}

/** Select curto da toolbar. Nativo de propósito: teclado e leitor de tela já corretos. */
export function FiltroSelect({
  rotulo,
  valor,
  aoMudar,
  opcoes,
}: {
  rotulo: string
  valor: string
  aoMudar: (valor: string) => void
  opcoes: { valor: string; texto: string }[]
}) {
  return (
    <select
      aria-label={rotulo}
      value={valor}
      onChange={(e) => aoMudar(e.target.value)}
      className={cn(
        'h-8 rounded-md border border-input bg-card px-2 text-[13px] text-foreground shadow-xs',
        'focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2',
      )}
    >
      {opcoes.map((o) => (
        <option key={o.valor} value={o.valor}>
          {o.texto}
        </option>
      ))}
    </select>
  )
}

/** Empurra o que vier depois para a direita da toolbar. */
export function EspacoToolbar() {
  return <div className="ml-auto" />
}

// -------------------------------------------------------------------- status

type Tom = 'sucesso' | 'atencao' | 'critico' | 'info' | 'neutro'

const TONS: Record<Tom, string> = {
  sucesso: 'bg-sucesso-suave text-sucesso',
  atencao: 'bg-atencao-suave text-atencao',
  critico: 'bg-critico-suave text-critico',
  info: 'bg-info-suave text-info',
  neutro: 'bg-neutro-suave text-neutro',
}

const PONTOS: Record<Tom, string> = {
  sucesso: 'bg-sucesso',
  atencao: 'bg-atencao',
  critico: 'bg-critico',
  info: 'bg-info',
  neutro: 'bg-neutro',
}

/**
 * Badge de ESTADO, nunca de valor qualquer.
 *
 * Transformar todo dado em pill e o atalho mais rapido para uma tela parecer
 * template. O ponto colorido existe para nao depender so da cor: em monocromia
 * ou com daltonismo, o texto continua dizendo tudo.
 */
export function StatusBadge({ tom, children }: { tom: Tom; children: ReactNode }) {
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 rounded-sm px-2 py-[3px]',
        'text-[10.5px] font-bold uppercase tracking-[0.04em]',
        TONS[tom],
      )}
    >
      <span className={cn('size-1.5 shrink-0 rounded-full', PONTOS[tom])} aria-hidden />
      {children}
    </span>
  )
}

// ----------------------------------------------------------------- dinheiro

const MOEDA = new Intl.NumberFormat('pt-BR', {
  style: 'currency',
  currency: 'BRL',
  minimumFractionDigits: 2,
})

/**
 * Valor monetario.
 *
 * Sempre tabular: sem largura fixa de algarismo, 1.111,11 e 8.888,88 nao se
 * alinham na vertical e a coluna perde a unica funcao que tinha, que e poder
 * ser conferida de relance.
 */
export function Dinheiro({
  valor,
  sinal,
  enfase,
  className,
}: {
  valor: number
  /** 'desconto' antepoe o menos e esmaece; 'nenhum' e o padrao. */
  sinal?: 'desconto' | 'nenhum'
  enfase?: boolean
  className?: string
}) {
  const desconto = sinal === 'desconto' && valor > 0

  return (
    <span
      className={cn(
        'tabular whitespace-nowrap',
        enfase && 'font-semibold',
        desconto && 'text-muted-foreground',
        className,
      )}
    >
      {desconto ? '− ' : ''}
      {MOEDA.format(valor)}
    </span>
  )
}

/**
 * Resumo financeiro em faixa tipografica, com divisores discretos.
 *
 * Substitui a grade de KPI cards: seis caixas com sombra ocupam meia tela para
 * dizer o que quatro numeros alinhados dizem numa linha - e a caixa nao
 * acrescenta informacao nenhuma ao numero que esta dentro dela.
 */
export function ResumoFinanceiro({
  itens,
  className,
}: {
  itens: { rotulo: string; valor: ReactNode; enfase?: boolean }[]
  className?: string
}) {
  return (
    <dl
      className={cn(
        'flex flex-wrap items-stretch gap-y-5 border-y border-border-forte py-4',
        className,
      )}
    >
      {itens.map((item, indice) => (
        <div
          key={item.rotulo}
          className={cn(
            'min-w-[10rem] flex-1 px-6 first:pl-0',
            indice > 0 && 'border-border sm:border-l',
          )}
        >
          <dt className="rotulo-secao">{item.rotulo}</dt>
          <dd
            className={cn(
              'mt-1.5 tabular text-[19px] leading-none tracking-[-0.02em]',
              item.enfase ? 'font-bold text-foreground' : 'font-medium text-foreground',
            )}
          >
            {item.valor}
          </dd>
        </div>
      ))}
    </dl>
  )
}

/**
 * Par rotulo/valor para blocos de detalhe.
 *
 * Existe para nao transformar cada campo num card - a tentacao classica que
 * produz a tela de vinte caixas.
 */
export function Campo({
  rotulo,
  children,
  className,
}: {
  rotulo: string
  children: ReactNode
  className?: string
}) {
  return (
    <div className={cn('min-w-0', className)}>
      <dt className="text-xs text-muted-foreground">{rotulo}</dt>
      <dd className="mt-0.5 text-[13px] text-foreground">{children}</dd>
    </div>
  )
}

export function ListaCampos({ children, colunas = 3 }: { children: ReactNode; colunas?: 2 | 3 | 4 }) {
  return (
    <dl
      className={cn(
        'grid gap-x-8 gap-y-4',
        colunas === 2 && 'sm:grid-cols-2',
        colunas === 3 && 'sm:grid-cols-2 lg:grid-cols-3',
        colunas === 4 && 'sm:grid-cols-2 lg:grid-cols-4',
      )}
    >
      {children}
    </dl>
  )
}
