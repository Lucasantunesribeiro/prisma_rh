import {
  Building2,
  Check,
  ChevronsUpDown,
  ClipboardList,
  Contact,
  IdCard,
  Upload,
  LogOut,
  PanelLeftClose,
  PanelLeftOpen,
  Receipt,
  Scale,
  Search,
  Activity,
} from 'lucide-react'
import { useState, type ComponentType } from 'react'
import { NavLink, useMatch } from 'react-router'
import { ROTULO_PERFIL } from '@/api/autenticacao'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Input } from '@/components/ui/input'
import { Tooltip } from '@/components/ui/tooltip'
import { useSessao } from '@/auth/useSessao'
import { cn } from '@/lib/utils'
import { useEmpresaAtual } from './contexto'

interface Item {
  para: string
  texto: string
  icone: ComponentType<{ className?: string }>
}

interface Grupo {
  titulo: string
  itens: Item[]
}

/**
 * A navegação é derivada das ROTAS QUE EXISTEM.
 *
 * Nenhum item aponta para tela inexistente, e nenhum módulo do ROADMAP aparece
 * antes de ser implementado: um link que não leva a lugar nenhum ensina o
 * usuário a desconfiar da navegação inteira.
 */
const GRUPOS: Grupo[] = [
  {
    titulo: 'Pessoas',
    itens: [
      { para: '/funcionarios', texto: 'Funcionários', icone: Contact },
      { para: '/cargos', texto: 'Cargos', icone: IdCard },
      { para: '/importacoes', texto: 'Importações', icone: Upload },
    ],
  },
  {
    titulo: 'Folha',
    itens: [
      { para: '/folhas', texto: 'Folhas', icone: Receipt },
      { para: '/rubricas', texto: 'Rubricas', icone: ClipboardList },
      { para: '/parametros', texto: 'Parâmetros legais', icone: Scale },
    ],
  },
  {
    titulo: 'Administração',
    itens: [
      { para: '/empresas', texto: 'Empresas', icone: Building2 },
      { para: '/status', texto: 'Status do sistema', icone: Activity },
    ],
  },
]

export function Sidebar({
  recolhida,
  aoAlternar,
}: {
  recolhida: boolean
  aoAlternar: () => void
}) {
  const { usuario, sair } = useSessao()

  return (
    <aside
      className={cn(
        'flex shrink-0 flex-col border-r border-border-forte bg-sidebar',
        'transition-[width] duration-150',
      )}
      style={{
        width: recolhida ? 'var(--largura-sidebar-recolhida)' : 'var(--largura-sidebar)',
      }}
    >
      <Marca recolhida={recolhida} />

      {!recolhida && <SeletorEmpresa />}

      <nav className="min-h-0 flex-1 overflow-y-auto px-2 py-3" aria-label="Navegação principal">
        {GRUPOS.map((grupo) => (
          <div key={grupo.titulo} className="mb-5 last:mb-0">
              {!recolhida && (
                <p className="rotulo-secao px-2.5 pb-2 pt-1">{grupo.titulo}</p>
              )}

            <ul className="space-y-0.5">
              {grupo.itens.map((item) => (
                <li key={item.para}>
                  <ItemNavegacao item={item} recolhida={recolhida} />
                </li>
              ))}
            </ul>
          </div>
        ))}
      </nav>

      <div className="border-t border-sidebar-border p-2">
        {usuario && (
          <DropdownMenu>
            <DropdownMenuTrigger
              className={cn(
                'flex w-full items-center gap-2.5 rounded-md p-1.5 text-left',
                'hover:bg-sidebar-accent focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2',
              )}
            >
              <Iniciais nome={usuario.nome} perfil={ROTULO_PERFIL[usuario.perfil]} />

              {!recolhida && (
                <span className="min-w-0 flex-1">
                  <span className="block truncate text-[13px] font-medium leading-tight">
                    {usuario.nome ?? ROTULO_PERFIL[usuario.perfil]}
                  </span>
                  {usuario.nome && (
                    <span className="block truncate text-[11px] leading-tight text-muted-foreground">
                      {ROTULO_PERFIL[usuario.perfil]}
                    </span>
                  )}
                </span>
              )}
            </DropdownMenuTrigger>

            <DropdownMenuContent align="start" side="top" className="w-56">
              {usuario.email && <DropdownMenuLabel>{usuario.email}</DropdownMenuLabel>}
              {usuario.email && <DropdownMenuSeparator />}
              <DropdownMenuItem variant="destrutivo" onSelect={() => void sair()}>
                <LogOut aria-hidden />
                Sair
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        )}

        <button
          type="button"
          onClick={aoAlternar}
          aria-label={recolhida ? 'Expandir menu' : 'Recolher menu'}
          className={cn(
            'mt-1 flex w-full items-center gap-2.5 rounded-md p-1.5 text-[13px] text-muted-foreground',
            'hover:bg-sidebar-accent hover:text-foreground',
          )}
        >
          {recolhida ? (
            <PanelLeftOpen className="size-4 shrink-0" aria-hidden />
          ) : (
            <>
              <PanelLeftClose className="size-4 shrink-0" aria-hidden />
              Recolher
            </>
          )}
        </button>
      </div>
    </aside>
  )
}

function Marca({ recolhida }: { recolhida: boolean }) {
  return (
    <div
      className={cn(
        'flex shrink-0 items-center gap-2.5 border-b border-sidebar-border',
        // Recolhida, o símbolo centraliza no mesmo eixo dos ícones da
        // navegação. Com padding lateral fixo ele fica a 28px do canto e os
        // ícones a 30px - dois pixels que o olho lê como torto.
        recolhida ? 'justify-center px-0' : 'px-3.5',
      )}
      style={{ height: 'var(--altura-topbar)' }}
    >
      {/* Símbolo geométrico, não logotipo grande nem card de marca. */}
      <span
        aria-hidden
        className="grid size-7 shrink-0 place-items-center rounded-md bg-primary text-[13px] font-bold text-primary-foreground"
      >
        P
      </span>
      {!recolhida && (
        <span className="truncate text-[15px] font-bold tracking-tight text-primary">
          Prisma RH
        </span>
      )}
    </div>
  )
}

function ItemNavegacao({ item, recolhida }: { item: Item; recolhida: boolean }) {
  const Icone = item.icone

  /*
   * O estado ativo é calculado aqui, e o className vai como STRING.
   *
   * O NavLink aceita className como função `({ isActive }) => string`, mas
   * quando recolhido ele é envolvido pelo Tooltip com `asChild`: o Radix
   * clona o elemento e mescla as props concatenando `className`. Concatenar
   * uma função com uma string produz lixo, e NENHUMA classe é aplicada — o
   * link vira `display: inline`, os ícones desalinham e a altura da linha
   * dobra. Só o estado recolhido quebrava, e era exatamente por isso.
   */
  const ativo = useMatch({ path: item.para, end: false }) !== null

  const classe = cn(
    'flex items-center gap-3 rounded-md px-2.5 py-2 text-[13.5px] transition-colors',
    recolhida && 'justify-center px-0',
    ativo
      ? 'bg-sidebar-accent font-semibold text-sidebar-accent-foreground'
      : 'text-foreground/75 hover:bg-sidebar-hover hover:text-foreground',
  )

  const link = (
    <NavLink to={item.para} className={classe} aria-current={ativo ? 'page' : undefined}>
      <Icone className="size-[17px] shrink-0" aria-hidden />
      {!recolhida && <span className="truncate">{item.texto}</span>}
      {recolhida && <span className="sr-only">{item.texto}</span>}
    </NavLink>
  )

  return recolhida ? <Tooltip conteudo={item.texto}>{link}</Tooltip> : link
}

/**
 * Seletor de empresa.
 *
 * Só vira menu quando há mais de uma empresa acessível: um seletor com uma
 * opção é um clique que nunca muda nada.
 */
function SeletorEmpresa() {
  const { empresas, empresaAtual, selecionar, carregando } = useEmpresaAtual()
  const [busca, definirBusca] = useState('')

  if (carregando) {
    return (
      <div className="border-b border-sidebar-border px-3.5 py-3">
        <div className="h-3 w-3/4 animate-pulse rounded bg-muted" />
        <div className="mt-1.5 h-2.5 w-1/2 animate-pulse rounded bg-muted" />
      </div>
    )
  }

  if (empresas.length === 0) {
    return (
      <div className="border-b border-sidebar-border px-3.5 py-3">
        <p className="text-[11px] text-muted-foreground">Nenhuma empresa cadastrada</p>
      </div>
    )
  }

  const conteudo = (
    <>
      <span className="block truncate text-[13px] font-medium leading-tight">
        {empresaAtual?.nomeFantasia ?? empresaAtual?.razaoSocial}
      </span>
      <span className="tabular block truncate text-[11px] leading-tight text-muted-foreground">
        {empresaAtual?.cnpjFormatado}
      </span>
    </>
  )

  if (empresas.length === 1) {
    return (
      <div className="border-b border-sidebar-border px-3.5 py-3">
        <div className="min-w-0">{conteudo}</div>
      </div>
    )
  }

  const filtradas = empresas.filter((e) =>
    `${e.razaoSocial} ${e.nomeFantasia ?? ''} ${e.cnpjFormatado}`
      .toLowerCase()
      .includes(busca.toLowerCase()),
  )

  return (
    <div className="border-b border-sidebar-border px-2.5 py-2.5">
      <DropdownMenu>
        <DropdownMenuTrigger
          aria-label="Trocar empresa"
          className={cn(
            'flex w-full items-center gap-2 rounded-md px-1.5 py-1 text-left',
            'hover:bg-sidebar-accent focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2',
          )}
        >
          <span className="min-w-0 flex-1">{conteudo}</span>
          <ChevronsUpDown className="size-3.5 shrink-0 text-muted-foreground" aria-hidden />
        </DropdownMenuTrigger>

        <DropdownMenuContent align="start" className="w-72">
          <div className="relative p-1">
            <Search
              className="pointer-events-none absolute left-3 top-1/2 size-3.5 -translate-y-1/2 text-muted-foreground"
              aria-hidden
            />
            <Input
              aria-label="Buscar empresa"
              placeholder="Buscar empresa"
              value={busca}
              onChange={(e) => definirBusca(e.target.value)}
              className="h-8 pl-7 text-[13px]"
            />
          </div>

          <DropdownMenuSeparator />

          <div className="max-h-72 overflow-y-auto">
            {filtradas.length === 0 && (
              <p className="px-2 py-3 text-center text-[13px] text-muted-foreground">
                Nenhuma empresa encontrada
              </p>
            )}

            {filtradas.map((empresa) => (
              <DropdownMenuItem key={empresa.id} onSelect={() => selecionar(empresa.id)}>
                <Check
                  className={cn(
                    'size-4 shrink-0',
                    empresa.id === empresaAtual?.id ? 'opacity-100' : 'opacity-0',
                  )}
                  aria-hidden
                />
                <span className="min-w-0 flex-1">
                  <span className="block truncate">
                    {empresa.nomeFantasia ?? empresa.razaoSocial}
                  </span>
                  <span className="tabular block truncate text-[11px] text-muted-foreground">
                    {empresa.cnpjFormatado}
                  </span>
                </span>
              </DropdownMenuItem>
            ))}
          </div>
        </DropdownMenuContent>
      </DropdownMenu>
    </div>
  )
}

/**
 * Iniciais do nome, ou do perfil quando a API nao mandou o nome.
 *
 * O fallback nao e enfeite: GET /api/autenticacao/eu nao devolve nome, entao
 * depois de um F5 e exatamente este caminho que roda.
 */
function Iniciais({ nome, perfil }: { nome?: string; perfil: string }) {
  const iniciais = (nome ?? perfil)
    .split(' ')
    .filter(Boolean)
    .slice(0, 2)
    .map((parte) => parte[0])
    .join('')
    .toUpperCase()

  return (
    <span
      aria-hidden
      className="grid size-7 shrink-0 place-items-center rounded-full bg-secondary text-[11px] font-medium text-secondary-foreground"
    >
      {iniciais}
    </span>
  )
}

