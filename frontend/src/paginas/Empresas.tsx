import { Plus } from 'lucide-react'
import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import { podeAdministrar } from '@/api/autenticacao'
import { criarEmpresa, listarEmpresas, type Empresa } from '@/api/empresas'
import { useSessao } from '@/auth/useSessao'
import { DataTable, type Coluna } from '@/components/sistema/DataTable'
import {
  CabecalhoPagina,
  CampoBusca,
  EspacoToolbar,
  StatusBadge,
} from '@/components/sistema/Primitivos'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Drawer, DrawerClose, DrawerContent, DrawerTrigger } from '@/components/ui/drawer'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { usePagina } from '@/layout/usePagina'
import { PainelEstabelecimentos } from './Estabelecimentos'

export default function Empresas() {
  const { usuario } = useSessao()
  const administra = podeAdministrar(usuario?.perfil)

  usePagina([{ texto: 'Administração' }, { texto: 'Empresas' }])

  const [empresas, definirEmpresas] = useState<Empresa[]>([])
  const [carregando, definirCarregando] = useState(true)
  const [erro, definirErro] = useState<string | null>(null)
  const [busca, definirBusca] = useState('')
  const [selecionada, definirSelecionada] = useState<Empresa | null>(null)

  const carregar = useCallback(async () => {
    definirCarregando(true)
    definirErro(null)

    try {
      const pagina = await listarEmpresas()
      definirEmpresas(pagina.itens)
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Não foi possível carregar as empresas.')
    } finally {
      definirCarregando(false)
    }
  }, [])

  useEffect(() => {
    // O estado só muda DEPOIS do await, quando a resposta da API chega.
    // oxlint-disable-next-line react/set-state-in-effect
    void carregar()
  }, [carregar])

  const filtradas = useMemo(() => {
    const termo = busca.trim().toLowerCase()
    if (!termo) return empresas

    return empresas.filter((e) =>
      `${e.razaoSocial} ${e.nomeFantasia ?? ''} ${e.cnpjFormatado}`.toLowerCase().includes(termo),
    )
  }, [empresas, busca])

  const colunas: Coluna<Empresa>[] = [
    {
      cabecalho: 'Razão social',
      largura: '38%',
      celula: (e) => (
        <div className="min-w-0">
          <span className="block truncate font-medium text-foreground">{e.razaoSocial}</span>
          {e.nomeFantasia && (
            <span className="block truncate text-xs text-muted-foreground">{e.nomeFantasia}</span>
          )}
        </div>
      ),
    },
    {
      cabecalho: 'CNPJ',
      largura: '190px',
      celula: (e) => <span className="tabular text-muted-foreground">{e.cnpjFormatado}</span>,
    },
    {
      cabecalho: 'Situação',
      largura: '120px',
      celula: (e) => (
        <StatusBadge tom={e.ativa ? 'sucesso' : 'neutro'}>
          {e.ativa ? 'Ativa' : 'Inativa'}
        </StatusBadge>
      ),
    },
  ]

  return (
    <>
      <CabecalhoPagina
        titulo="Empresas"
        descricao="Empresas administradas pela organização. Clique para ver os estabelecimentos."
        acao={administra && <NovaEmpresa aoCriar={carregar} />}
      />

      <DataTable
        rotulo="Empresas da organização"
        colunas={colunas}
        itens={filtradas}
        chave={(e) => e.id}
        carregando={carregando}
        erro={erro}
        aoTentarNovamente={() => void carregar()}
        filtrado={busca.trim().length > 0}
        aoLimparFiltros={() => definirBusca('')}
        aoClicarLinha={(e) => definirSelecionada(e)}
        vazio={{
          titulo: 'Nenhuma empresa cadastrada',
          descricao: administra
            ? 'Cadastre a primeira empresa para começar.'
            : 'Nenhuma empresa foi cadastrada nesta organização.',
        }}
        toolbar={
          <>
            <CampoBusca
              rotulo="Buscar empresa"
              placeholder="Buscar por razão social ou CNPJ"
              valor={busca}
              aoMudar={definirBusca}
            />
            <EspacoToolbar />
          </>
        }
        rodape={
          <span>
            {filtradas.length} {filtradas.length === 1 ? 'empresa' : 'empresas'}
          </span>
        }
      />

      {selecionada && (
        <PainelEstabelecimentos
          empresa={selecionada}
          administra={administra}
          aoFechar={() => definirSelecionada(null)}
        />
      )}
    </>
  )
}

function NovaEmpresa({ aoCriar }: { aoCriar: () => Promise<void> }) {
  const [aberto, definirAberto] = useState(false)
  const [razaoSocial, definirRazaoSocial] = useState('')
  const [cnpj, definirCnpj] = useState('')
  const [nomeFantasia, definirNomeFantasia] = useState('')
  const [erro, definirErro] = useState<string | null>(null)
  const [enviando, definirEnviando] = useState(false)

  const aoEnviar = async (evento: FormEvent) => {
    evento.preventDefault()
    definirErro(null)
    definirEnviando(true)

    try {
      await criarEmpresa({ razaoSocial, cnpj, nomeFantasia: nomeFantasia || null })
      definirRazaoSocial('')
      definirCnpj('')
      definirNomeFantasia('')
      definirAberto(false)
      await aoCriar()
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Não foi possível criar a empresa.')
    } finally {
      definirEnviando(false)
    }
  }

  return (
    <Drawer open={aberto} onOpenChange={definirAberto}>
      <DrawerTrigger asChild>
        <Button size="sm">
          <Plus aria-hidden />
          Nova empresa
        </Button>
      </DrawerTrigger>

      <DrawerContent titulo="Nova empresa" className="max-w-lg">
        <form onSubmit={aoEnviar} className="space-y-4" noValidate>
          <div className="space-y-1.5">
            <Label htmlFor="razaoSocial">Razão social</Label>
            <Input
              id="razaoSocial"
              required
              autoFocus
              value={razaoSocial}
              onChange={(e) => definirRazaoSocial(e.target.value)}
            />
          </div>

          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor="cnpj">CNPJ</Label>
              <Input
                id="cnpj"
                required
                inputMode="numeric"
                placeholder="00.000.000/0000-00"
                value={cnpj}
                onChange={(e) => definirCnpj(e.target.value)}
                className="tabular"
              />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="nomeFantasia">Nome fantasia</Label>
              <Input
                id="nomeFantasia"
                value={nomeFantasia}
                onChange={(e) => definirNomeFantasia(e.target.value)}
              />
            </div>
          </div>

          {erro && (
            <Alert variant="destructive" role="alert">
              <AlertDescription>{erro}</AlertDescription>
            </Alert>
          )}

          <div className="flex justify-end gap-2 border-t border-border pt-4">
            <DrawerClose asChild>
              <Button type="button" variant="outline" size="sm">
                Cancelar
              </Button>
            </DrawerClose>
            <Button type="submit" size="sm" disabled={enviando}>
              {enviando ? 'Criando...' : 'Criar empresa'}
            </Button>
          </div>
        </form>
      </DrawerContent>
    </Drawer>
  )
}
