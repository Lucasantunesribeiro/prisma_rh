import { Plus } from 'lucide-react'
import { useCallback, useEffect, useState, type FormEvent } from 'react'
import {
  criarEstabelecimento,
  listarEstabelecimentos,
  type Empresa,
  type Estabelecimento,
} from '@/api/empresas'
import { DataTable, type Coluna } from '@/components/sistema/DataTable'
import { StatusBadge } from '@/components/sistema/Primitivos'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Drawer, DrawerClose, DrawerContent, DrawerTrigger } from '@/components/ui/drawer'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

/**
 * Estabelecimentos de uma empresa, num painel lateral.
 *
 * Não é rota própria de propósito: um estabelecimento só existe dentro de uma
 * empresa, e uma página separada obrigaria a escolher a empresa de novo, em
 * outro seletor, para ver o que já estava na linha que a pessoa acabou de
 * clicar.
 */
export function PainelEstabelecimentos({
  empresa,
  administra,
  aoFechar,
}: {
  empresa: Empresa
  administra: boolean
  aoFechar: () => void
}) {
  const [itens, definirItens] = useState<Estabelecimento[]>([])
  const [carregando, definirCarregando] = useState(true)
  const [erro, definirErro] = useState<string | null>(null)

  const carregar = useCallback(async () => {
    definirCarregando(true)
    definirErro(null)

    try {
      definirItens(await listarEstabelecimentos(empresa.id))
    } catch (falha) {
      definirErro(
        falha instanceof Error ? falha.message : 'Não foi possível carregar os estabelecimentos.',
      )
    } finally {
      definirCarregando(false)
    }
  }, [empresa.id])

  useEffect(() => {
    // O estado só muda DEPOIS do await, quando a resposta da API chega.
    // oxlint-disable-next-line react/set-state-in-effect
    void carregar()
  }, [carregar])

  const colunas: Coluna<Estabelecimento>[] = [
    {
      cabecalho: 'Código',
      largura: '110px',
      celula: (e) => <span className="tabular text-muted-foreground">{e.codigo}</span>,
    },
    {
      cabecalho: 'Nome',
      celula: (e) => <span className="font-medium text-foreground">{e.nome}</span>,
    },
    {
      cabecalho: 'Situação',
      largura: '110px',
      celula: (e) => (
        <StatusBadge tom={e.ativo ? 'sucesso' : 'neutro'}>
          {e.ativo ? 'Ativo' : 'Inativo'}
        </StatusBadge>
      ),
    },
  ]

  return (
    <Drawer open onOpenChange={(estado) => !estado && aoFechar()}>
      <DrawerContent
        titulo={empresa.razaoSocial}
        descricao={
          <>
            <span className="tabular">{empresa.cnpjFormatado}</span> · Estabelecimentos
          </>
        }
        className="max-w-2xl"
      >
        <div className="mb-3 flex items-center justify-between gap-4">
          <h3 className="text-[15px] font-semibold tracking-tight">Estabelecimentos</h3>
          {administra && <NovoEstabelecimento idEmpresa={empresa.id} aoCriar={carregar} />}
        </div>

        <DataTable
          rotulo={`Estabelecimentos de ${empresa.razaoSocial}`}
          colunas={colunas}
          itens={itens}
          chave={(e) => e.id}
          carregando={carregando}
          erro={erro}
          aoTentarNovamente={() => void carregar()}
          vazio={{
            titulo: 'Nenhum estabelecimento',
            descricao: 'O contrato de trabalho precisa de um estabelecimento para a lotação.',
          }}
        />
      </DrawerContent>
    </Drawer>
  )
}

function NovoEstabelecimento({
  idEmpresa,
  aoCriar,
}: {
  idEmpresa: string
  aoCriar: () => Promise<void>
}) {
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
      await criarEstabelecimento(idEmpresa, { codigo, nome })
      definirCodigo('')
      definirNome('')
      definirAberto(false)
      await aoCriar()
    } catch (falha) {
      definirErro(
        falha instanceof Error ? falha.message : 'Não foi possível criar o estabelecimento.',
      )
    } finally {
      definirEnviando(false)
    }
  }

  return (
    <Drawer open={aberto} onOpenChange={definirAberto}>
      <DrawerTrigger asChild>
        <Button variant="outline" size="sm">
          <Plus aria-hidden />
          Novo estabelecimento
        </Button>
      </DrawerTrigger>

      <DrawerContent titulo="Novo estabelecimento" className="max-w-md">
        <form onSubmit={aoEnviar} className="space-y-4" noValidate>
          <div className="space-y-1.5">
            <Label htmlFor="codigoEstab">Código</Label>
            <Input
              id="codigoEstab"
              required
              autoFocus
              placeholder="001"
              value={codigo}
              onChange={(e) => definirCodigo(e.target.value)}
              className="tabular"
            />
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="nomeEstab">Nome</Label>
            <Input
              id="nomeEstab"
              required
              placeholder="Matriz"
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
              {enviando ? 'Criando...' : 'Criar estabelecimento'}
            </Button>
          </div>
        </form>
      </DrawerContent>
    </Drawer>
  )
}
