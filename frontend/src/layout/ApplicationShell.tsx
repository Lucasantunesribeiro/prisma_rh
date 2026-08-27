import { useCallback, useEffect, useMemo, useState } from 'react'
import { Outlet } from 'react-router'
import { listarEmpresas, type Empresa } from '@/api/empresas'
import { ProvedorTooltip } from '@/components/ui/tooltip'
import { EmpresaContexto, PaginaContexto, type Trilha } from './contexto'
import { Sidebar } from './Sidebar'
import { Topbar } from './Topbar'

const CHAVE_RECOLHIDA = 'prisma-rh:sidebar-recolhida'
const CHAVE_EMPRESA = 'prisma-rh:empresa'

/**
 * O único shell da aplicação.
 *
 * Toda página autenticada — as de hoje e as das fases seguintes — renderiza
 * dentro dele. Shell por módulo é o caminho mais curto para cinco telas com
 * cinco arquiteturas visuais diferentes, que foi exatamente o problema desta
 * reforma.
 *
 * O workspace ocupa a largura restante. Não existe `max-width` global: numa
 * tela de 1440px, limitar o conteúdo a 1200px joga fora uma coluna inteira de
 * tabela para ganhar uma margem que ninguém pediu. Páginas de formulário
 * limitam a própria largura, individualmente.
 */
export function ApplicationShell() {
  const [recolhida, definirRecolhida] = useState(() => lerBooleano(CHAVE_RECOLHIDA))
  const [empresas, definirEmpresas] = useState<Empresa[]>([])
  const [idEmpresa, definirIdEmpresa] = useState<string | null>(() => lerTexto(CHAVE_EMPRESA))
  const [carregandoEmpresas, definirCarregandoEmpresas] = useState(true)

  const [trilha, definirTrilha] = useState<Trilha[]>([])
  const [competencia, definirCompetencia] = useState<string | null>(null)

  useEffect(() => {
    let ativo = true

    listarEmpresas()
      .then((pagina) => {
        if (!ativo) return
        definirEmpresas(pagina.itens)
      })
      .catch(() => {
        // A falha já é reportada pela página que precisa dos dados. O seletor
        // apenas fica vazio; derrubar o shell inteiro por causa dele seria
        // desproporcional.
      })
      .finally(() => {
        if (ativo) definirCarregandoEmpresas(false)
      })

    return () => {
      ativo = false
    }
  }, [])

  const alternar = useCallback(() => {
    definirRecolhida((atual) => {
      gravar(CHAVE_RECOLHIDA, String(!atual))
      return !atual
    })
  }, [])

  const selecionar = useCallback((id: string) => {
    definirIdEmpresa(id)
    gravar(CHAVE_EMPRESA, id)
  }, [])

  const definirPagina = useCallback(
    (dados: { trilha?: Trilha[]; competencia?: string | null }) => {
      if (dados.trilha) definirTrilha(dados.trilha)
      definirCompetencia(dados.competencia ?? null)
    },
    [],
  )

  const ativas = useMemo(() => empresas.filter((e) => e.ativa), [empresas])

  const empresaAtual = useMemo(
    () => ativas.find((e) => e.id === idEmpresa) ?? ativas[0] ?? null,
    [ativas, idEmpresa],
  )

  const contextoEmpresa = useMemo(
    () => ({ empresas: ativas, empresaAtual, selecionar, carregando: carregandoEmpresas }),
    [ativas, empresaAtual, selecionar, carregandoEmpresas],
  )

  const contextoPagina = useMemo(
    () => ({ trilha, competencia, definir: definirPagina }),
    [trilha, competencia, definirPagina],
  )

  return (
    <ProvedorTooltip>
      <EmpresaContexto value={contextoEmpresa}>
        <PaginaContexto value={contextoPagina}>
          <div className="flex h-screen overflow-hidden bg-background">
            <Sidebar recolhida={recolhida} aoAlternar={alternar} />

            <div className="flex min-w-0 flex-1 flex-col">
              <Topbar trilha={trilha} competencia={competencia} />

              <main className="min-h-0 flex-1 overflow-y-auto px-6 py-6">
                <Outlet />
              </main>
            </div>
          </div>
        </PaginaContexto>
      </EmpresaContexto>
    </ProvedorTooltip>
  )
}

/*
 * Preferência local, sempre em try/catch: navegador em janela privada ou com
 * dados de site bloqueados lança ao acessar o localStorage, e derrubar a
 * aplicação por causa da lembrança de uma sidebar seria absurdo.
 */
function lerBooleano(chave: string): boolean {
  try {
    return localStorage.getItem(chave) === 'true'
  } catch {
    return false
  }
}

function lerTexto(chave: string): string | null {
  try {
    return localStorage.getItem(chave)
  } catch {
    return null
  }
}

function gravar(chave: string, valor: string) {
  try {
    localStorage.setItem(chave, valor)
  } catch {
    // Preferência não persistida não é erro do usuário.
  }
}
