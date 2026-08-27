import { Plus } from 'lucide-react'
import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router'
import { podeAdministrarPessoas } from '@/api/autenticacao'
import {
  criarFuncionario,
  formatarData,
  listarFuncionarios,
  type FiltroFuncionarios,
  type Funcionario,
} from '@/api/pessoas'
import { useSessao } from '@/auth/useSessao'
import { DataTable, type Coluna } from '@/components/sistema/DataTable'
import {
  CabecalhoPagina,
  CampoBusca,
  EspacoToolbar,
  FiltroSelect,
  StatusBadge,
} from '@/components/sistema/Primitivos'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Drawer, DrawerClose, DrawerContent, DrawerTrigger } from '@/components/ui/drawer'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { usePagina } from '@/layout/usePagina'

type Situacao = 'todos' | 'ativos' | 'inativos'

export default function Funcionarios() {
  const { usuario } = useSessao()
  const navegar = useNavigate()
  const administra = podeAdministrarPessoas(usuario?.perfil)

  usePagina([{ texto: 'Pessoas' }, { texto: 'Funcionários' }])

  const [itens, definirItens] = useState<Funcionario[]>([])
  const [total, definirTotal] = useState(0)
  const [carregando, definirCarregando] = useState(true)
  const [erro, definirErro] = useState<string | null>(null)

  const [nome, definirNome] = useState('')
  const [cpf, definirCpf] = useState('')
  const [situacao, definirSituacao] = useState<Situacao>('todos')

  const filtro = useMemo<FiltroFuncionarios>(
    () => ({
      nome: nome.trim() || undefined,
      cpf: cpf.trim() || undefined,
      ativo: situacao === 'todos' ? undefined : situacao === 'ativos',
    }),
    [nome, cpf, situacao],
  )

  const carregar = useCallback(async (aplicado: FiltroFuncionarios) => {
    definirCarregando(true)
    definirErro(null)

    try {
      const pagina = await listarFuncionarios(aplicado)
      definirItens(pagina.itens)
      definirTotal(pagina.total)
    } catch (falha) {
      definirErro(
        falha instanceof Error ? falha.message : 'Não foi possível carregar os funcionários.',
      )
    } finally {
      definirCarregando(false)
    }
  }, [])

  useEffect(() => {
    // A busca espera o usuário parar de digitar: sem isso, cada tecla vira uma
    // requisição e a lista pisca a cada letra.
    const relogio = setTimeout(() => void carregar(filtro), 250)
    return () => clearTimeout(relogio)
  }, [carregar, filtro])

  const temFiltro = Boolean(filtro.nome || filtro.cpf || filtro.ativo !== undefined)

  const limpar = useCallback(() => {
    definirNome('')
    definirCpf('')
    definirSituacao('todos')
  }, [])

  const colunas: Coluna<Funcionario>[] = [
    {
      cabecalho: 'Funcionário',
      largura: '34%',
      celula: (f) => <span className="font-medium text-foreground">{f.nome}</span>,
    },
    {
      cabecalho: 'CPF',
      celula: (f) => (
        <span className="tabular text-muted-foreground" title="Documento parcial por privacidade">
          {f.cpfFormatado}
        </span>
      ),
    },
    {
      cabecalho: 'Nascimento',
      secundaria: true,
      celula: (f) => <span className="tabular">{formatarData(f.dataNascimento)}</span>,
    },
    {
      cabecalho: 'Situação',
      largura: '120px',
      celula: (f) => (
        <StatusBadge tom={f.ativo ? 'sucesso' : 'neutro'}>{f.ativo ? 'Ativo' : 'Inativo'}</StatusBadge>
      ),
    },
  ]

  return (
    <>
      <CabecalhoPagina
        titulo="Funcionários"
        descricao="Pessoas da organização. O vínculo com a empresa fica no contrato."
        acao={administra && <NovoFuncionario aoCriar={() => carregar(filtro)} />}
      />

      <DataTable
        rotulo="Funcionários da organização"
        colunas={colunas}
        itens={itens}
        chave={(f) => f.id}
        carregando={carregando}
        erro={erro}
        aoTentarNovamente={() => void carregar(filtro)}
        filtrado={temFiltro}
        aoLimparFiltros={limpar}
        aoClicarLinha={(f) => navegar(`/funcionarios/${f.id}`)}
        vazio={{
          titulo: 'Nenhum funcionário cadastrado',
          descricao: administra
            ? 'Cadastre a primeira pessoa para começar a montar os contratos.'
            : 'Ninguém foi cadastrado nesta organização ainda.',
        }}
        toolbar={
          <>
            <CampoBusca
              rotulo="Buscar por nome"
              placeholder="Buscar por nome"
              valor={nome}
              aoMudar={definirNome}
            />

            <Input
              aria-label="Buscar por CPF completo"
              placeholder="CPF completo"
              value={cpf}
              onChange={(e) => definirCpf(e.target.value)}
              className="tabular h-8 w-40 text-[13px]"
            />

            <FiltroSelect
              rotulo="Situação"
              valor={situacao}
              aoMudar={(v) => definirSituacao(v as Situacao)}
              opcoes={[
                { valor: 'todos', texto: 'Todas as situações' },
                { valor: 'ativos', texto: 'Ativos' },
                { valor: 'inativos', texto: 'Inativos' },
              ]}
            />

            <EspacoToolbar />

            {temFiltro && (
              <Button variant="ghost" size="sm" onClick={limpar}>
                Limpar
              </Button>
            )}
          </>
        }
        rodape={
          <>
            <span>
              {total} {total === 1 ? 'funcionário' : 'funcionários'}
            </span>
            <span className="text-xs">
              A busca por CPF exige o documento completo — busca parcial permitiria descobrir
              documentos por tentativa.
            </span>
          </>
        }
      />
    </>
  )
}

function NovoFuncionario({ aoCriar }: { aoCriar: () => Promise<void> }) {
  const [aberto, definirAberto] = useState(false)
  const [nome, definirNome] = useState('')
  const [cpf, definirCpf] = useState('')
  const [dataNascimento, definirDataNascimento] = useState('')
  const [erro, definirErro] = useState<string | null>(null)
  const [enviando, definirEnviando] = useState(false)

  const aoEnviar = async (evento: FormEvent) => {
    evento.preventDefault()
    definirErro(null)
    definirEnviando(true)

    try {
      await criarFuncionario({ nome, cpf, dataNascimento })
      definirNome('')
      definirCpf('')
      definirDataNascimento('')
      definirAberto(false)
      await aoCriar()
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Não foi possível criar o funcionário.')
    } finally {
      definirEnviando(false)
    }
  }

  return (
    <Drawer open={aberto} onOpenChange={definirAberto}>
      <DrawerTrigger asChild>
        <Button size="sm">
          <Plus aria-hidden />
          Novo funcionário
        </Button>
      </DrawerTrigger>

      <DrawerContent
        titulo="Novo funcionário"
        descricao="Cadastre a pessoa. O contrato de trabalho é criado depois, no detalhe."
        className="max-w-lg"
      >
        {/*
         * O formulário mora no drawer, e não permanentemente acima da tabela.
         * Um formulário sempre aberto ocupa metade da tela para uma ação que o
         * analista faz poucas vezes por dia, e empurra a lista - que é o que
         * ele realmente veio ver - para baixo da dobra.
         */}
        <form onSubmit={aoEnviar} className="space-y-4" noValidate>
          <div className="space-y-1.5">
            <Label htmlFor="nomeFuncionario">Nome completo</Label>
            <Input
              id="nomeFuncionario"
              required
              autoFocus
              value={nome}
              onChange={(e) => definirNome(e.target.value)}
            />
          </div>

          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor="cpfFuncionario">CPF</Label>
              <Input
                id="cpfFuncionario"
                required
                inputMode="numeric"
                placeholder="000.000.000-00"
                value={cpf}
                onChange={(e) => definirCpf(e.target.value)}
                className="tabular"
              />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="nascimento">Data de nascimento</Label>
              <Input
                id="nascimento"
                type="date"
                required
                value={dataNascimento}
                onChange={(e) => definirDataNascimento(e.target.value)}
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
              {enviando ? 'Criando...' : 'Criar funcionário'}
            </Button>
          </div>
        </form>
      </DrawerContent>
    </Drawer>
  )
}
