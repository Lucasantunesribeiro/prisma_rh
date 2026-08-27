import { Plus } from 'lucide-react'
import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import { podeAdministrarPessoas } from '@/api/autenticacao'
import { criarCargo, listarCargos, type Cargo } from '@/api/pessoas'
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

export default function Cargos() {
  const { usuario } = useSessao()
  const administra = podeAdministrarPessoas(usuario?.perfil)

  usePagina([{ texto: 'Pessoas' }, { texto: 'Cargos' }])

  const [cargos, definirCargos] = useState<Cargo[]>([])
  const [carregando, definirCarregando] = useState(true)
  const [erro, definirErro] = useState<string | null>(null)
  const [busca, definirBusca] = useState('')

  const carregar = useCallback(async () => {
    definirCarregando(true)
    definirErro(null)

    try {
      definirCargos(await listarCargos())
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Não foi possível carregar os cargos.')
    } finally {
      definirCarregando(false)
    }
  }, [])

  useEffect(() => {
    // O estado só muda DEPOIS do await, quando a resposta da API chega.
    // oxlint-disable-next-line react/set-state-in-effect
    void carregar()
  }, [carregar])

  const filtrados = useMemo(() => {
    const termo = busca.trim().toLowerCase()
    if (!termo) return cargos

    return cargos.filter((c) => `${c.codigo} ${c.nome}`.toLowerCase().includes(termo))
  }, [cargos, busca])

  const colunas: Coluna<Cargo>[] = [
    {
      cabecalho: 'Código',
      largura: '140px',
      celula: (c) => <span className="tabular text-muted-foreground">{c.codigo}</span>,
    },
    {
      cabecalho: 'Cargo',
      celula: (c) => <span className="font-medium text-foreground">{c.nome}</span>,
    },
    {
      cabecalho: 'Situação',
      largura: '120px',
      celula: (c) => (
        <StatusBadge tom={c.ativo ? 'sucesso' : 'neutro'}>
          {c.ativo ? 'Ativo' : 'Inativo'}
        </StatusBadge>
      ),
    },
  ]

  return (
    <>
      <CabecalhoPagina
        titulo="Cargos"
        descricao="Catálogo da organização. O cargo de cada pessoa é definido na vigência do contrato."
        acao={administra && <NovoCargo aoCriar={carregar} />}
      />

      <DataTable
        rotulo="Catálogo de cargos"
        colunas={colunas}
        itens={filtrados}
        chave={(c) => c.id}
        carregando={carregando}
        erro={erro}
        aoTentarNovamente={() => void carregar()}
        filtrado={busca.trim().length > 0}
        aoLimparFiltros={() => definirBusca('')}
        vazio={{
          titulo: 'Nenhum cargo cadastrado',
          descricao: 'O contrato de trabalho exige um cargo — cadastre o primeiro.',
        }}
        toolbar={
          <>
            <CampoBusca
              rotulo="Buscar cargo"
              placeholder="Buscar por código ou nome"
              valor={busca}
              aoMudar={definirBusca}
            />
            <EspacoToolbar />
          </>
        }
        rodape={
          <span>
            {filtrados.length} {filtrados.length === 1 ? 'cargo' : 'cargos'}
          </span>
        }
      />
    </>
  )
}

function NovoCargo({ aoCriar }: { aoCriar: () => Promise<void> }) {
  const [aberto, definirAberto] = useState(false)
  const [codigo, definirCodigo] = useState('')
  const [nome, definirNome] = useState('')
  const [erro, definirErro] = useState<string | null>(null)
  const [enviando, definirEnviando] = useState(false)

  const aoEnviar = async (evento: FormEvent) => {
    evento.preventDefault()
    definirErro(null)
    definirEnviando(true)

    try {
      await criarCargo({ codigo, nome })
      definirCodigo('')
      definirNome('')
      definirAberto(false)
      await aoCriar()
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Não foi possível criar o cargo.')
    } finally {
      definirEnviando(false)
    }
  }

  return (
    <Drawer open={aberto} onOpenChange={definirAberto}>
      <DrawerTrigger asChild>
        <Button size="sm">
          <Plus aria-hidden />
          Novo cargo
        </Button>
      </DrawerTrigger>

      <DrawerContent titulo="Novo cargo" className="max-w-md">
        <form onSubmit={aoEnviar} className="space-y-4" noValidate>
          <div className="space-y-1.5">
            <Label htmlFor="codigoCargo">Código</Label>
            <Input
              id="codigoCargo"
              required
              autoFocus
              placeholder="ANA"
              value={codigo}
              onChange={(e) => definirCodigo(e.target.value)}
              className="tabular"
            />
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="nomeCargo">Nome</Label>
            <Input
              id="nomeCargo"
              required
              placeholder="Analista"
              value={nome}
              onChange={(e) => definirNome(e.target.value)}
            />
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
              {enviando ? 'Criando...' : 'Criar cargo'}
            </Button>
          </div>
        </form>
      </DrawerContent>
    </Drawer>
  )
}
