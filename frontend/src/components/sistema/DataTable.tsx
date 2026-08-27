import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'
import { EsqueletoTabela, EstadoErro, EstadoSemResultado, EstadoVazio } from './Estados'

/**
 * A tabela do Prisma RH.
 *
 * E o componente mais importante do produto: quase toda tarefa de folha
 * comeca lendo uma lista. Por isso ela e um componente unico em vez de um
 * padrao que cada pagina reimplementa - cinco tabelas parecidas mas diferentes
 * obrigam o usuario a reaprender a mesma tela cinco vezes.
 *
 * Nao fica dentro de card: o card apertaria a tabela e criaria uma moldura sem
 * funcao. Ela ocupa o workspace com borda discreta, como fazem os produtos
 * operacionais de referencia.
 */

export interface Coluna<T> {
  /** Cabecalho da coluna. Texto simples: header de tabela nao e lugar de icone. */
  cabecalho: string
  /** Conteudo da celula. */
  celula: (item: T) => ReactNode
  /** Numero, dinheiro ou data curta: alinhado a direita e com algarismo de largura fixa. */
  numerica?: boolean
  /** Largura sugerida, quando a coluna precisa ser previsivel. */
  largura?: string
  /**
   * Esconde a coluna abaixo de 1280px. Serve para dado secundario: melhor
   * ocultar do que espremer ate ficar ilegivel.
   */
  secundaria?: boolean
  className?: string
}

interface Props<T> {
  colunas: Coluna<T>[]
  itens: T[]
  chave: (item: T) => string
  /** Toolbar da tabela: busca, filtros e acao contextual. */
  toolbar?: ReactNode
  carregando?: boolean
  erro?: string | null
  aoTentarNovamente?: () => void
  /** Vazio de verdade - a organizacao nao tem nenhum registro ainda. */
  vazio?: { titulo: string; descricao?: string; acao?: ReactNode }
  /** Vazio por filtro: mensagem diferente, porque a acao util e outra. */
  filtrado?: boolean
  aoLimparFiltros?: () => void
  aoClicarLinha?: (item: T) => void
  /** Rotulo da tabela para leitor de tela. */
  rotulo: string
  rodape?: ReactNode
}

export function DataTable<T>({
  colunas,
  itens,
  chave,
  toolbar,
  carregando,
  erro,
  aoTentarNovamente,
  vazio,
  filtrado,
  aoLimparFiltros,
  aoClicarLinha,
  rotulo,
  rodape,
}: Props<T>) {
  const clicavel = Boolean(aoClicarLinha)

  return (
    <section className="overflow-hidden rounded-lg border border-border-forte bg-card shadow-[0_1px_2px_rgba(17,26,46,0.04)]">
      {toolbar && (
        <div className="flex flex-wrap items-center gap-2.5 border-b border-border px-4 py-3">
          {toolbar}
        </div>
      )}

      <div aria-live="polite" aria-busy={carregando}>
        {carregando && <EsqueletoTabela colunas={colunas.length} />}

        {!carregando && erro && <EstadoErro mensagem={erro} aoTentarNovamente={aoTentarNovamente} />}

        {!carregando && !erro && itens.length === 0 && filtrado && (
          <EstadoSemResultado aoLimpar={aoLimparFiltros} />
        )}

        {!carregando && !erro && itens.length === 0 && !filtrado && vazio && (
          <EstadoVazio {...vazio} />
        )}

        {!carregando && !erro && itens.length > 0 && (
          /*
           * Scroll interno horizontal: numa tela de 1280px uma tabela larga
           * rola dentro do proprio container, sem empurrar a pagina inteira.
           */
          <div className="overflow-x-auto">
            <table className="w-full caption-bottom border-collapse text-[13.5px]">
              <caption className="sr-only">{rotulo}</caption>

              <thead>
                <tr className="border-b border-border-forte bg-muted/60">
                  {colunas.map((coluna) => (
                    <th
                      key={coluna.cabecalho}
                      scope="col"
                      style={coluna.largura ? { width: coluna.largura } : undefined}
                      className={cn(
                        'rotulo-secao whitespace-nowrap px-4 py-2.5 text-left align-middle',
                        coluna.numerica && 'text-right',
                        coluna.secundaria && 'hidden xl:table-cell',
                        coluna.className,
                      )}
                    >
                      {coluna.cabecalho}
                    </th>
                  ))}
                </tr>
              </thead>

              <tbody className="divide-y divide-border">
                {itens.map((item) => (
                  <tr
                    key={chave(item)}
                    onClick={clicavel ? () => aoClicarLinha?.(item) : undefined}
                    className={cn(
                      'transition-colors',
                      clicavel && 'cursor-pointer hover:bg-accent/70',
                      !clicavel && 'hover:bg-muted/50',
                    )}
                  >
                    {colunas.map((coluna) => (
                      <td
                        key={coluna.cabecalho}
                        className={cn(
                          'px-4 py-3 align-middle',
                          coluna.numerica && 'text-right tabular',
                          coluna.secundaria && 'hidden xl:table-cell',
                          coluna.className,
                        )}
                      >
                        {coluna.celula(item)}
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {rodape && (
        <div className="flex items-center justify-between gap-4 border-t border-border bg-muted/30 px-4 py-2.5 text-[13px] text-muted-foreground">
          {rodape}
        </div>
      )}
    </section>
  )
}
