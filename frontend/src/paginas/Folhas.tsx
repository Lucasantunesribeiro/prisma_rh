import { Plus } from 'lucide-react'
import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router'
import {
  abrirFolha,
  competenciaPorExtenso,
  listarFolhas,
  normalizarCompetencia,
  podeProcessarFolha,
  ROTULO_SITUACAO_FOLHA,
  ROTULO_TIPO_FOLHA,
  type FolhaResumo,
  type SituacaoFolha,
  type TipoFolha,
} from '@/api/folha'
import { useSessao } from '@/auth/useSessao'
import { DataTable, type Coluna } from '@/components/sistema/DataTable'
import {
  CabecalhoPagina,
  Dinheiro,
  EspacoToolbar,
  FiltroSelect,
  StatusBadge,
} from '@/components/sistema/Primitivos'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Drawer, DrawerClose, DrawerContent, DrawerTrigger } from '@/components/ui/drawer'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { useEmpresaAtual } from '@/layout/contexto'
import { usePagina } from '@/layout/usePagina'

const TOM: Record<SituacaoFolha, 'neutro' | 'info' | 'sucesso'> = {
  Rascunho: 'neutro',
  Calculada: 'info',
  Fechada: 'sucesso',
}

export default function Folhas() {
  const { usuario } = useSessao()
  const navegar = useNavigate()
  const { empresas, empresaAtual } = useEmpresaAtual()
  const processa = podeProcessarFolha(usuario?.perfil)

  usePagina([{ texto: 'Folha' }, { texto: 'Folhas' }])

  const [folhas, definirFolhas] = useState<FolhaResumo[]>([])
  const [carregando, definirCarregando] = useState(true)
  const [erro, definirErro] = useState<string | null>(null)
  const [situacao, definirSituacao] = useState<'todas' | SituacaoFolha>('todas')

  /*
   * A empresa da sidebar pré-filtra a lista. É conveniência de interface: o
   * backend continua devolvendo só o que a organização do token pode ver.
   */
  const [idEmpresa, definirIdEmpresa] = useState('')

  const carregar = useCallback(async () => {
    definirCarregando(true)
    definirErro(null)

    try {
      definirFolhas(await listarFolhas())
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Não foi possível carregar as folhas.')
    } finally {
      definirCarregando(false)
    }
  }, [])

  useEffect(() => {
    // O estado só muda DEPOIS do await, quando a resposta da API chega.
    // oxlint-disable-next-line react/set-state-in-effect
    void carregar()
  }, [carregar])

  const filtradas = useMemo(
    () =>
      folhas.filter(
        (f) =>
          (!idEmpresa || f.idEmpresa === idEmpresa) &&
          (situacao === 'todas' || f.situacao === situacao),
      ),
    [folhas, idEmpresa, situacao],
  )

  const temFiltro = Boolean(idEmpresa) || situacao !== 'todas'

  const colunas: Coluna<FolhaResumo>[] = [
    {
      cabecalho: 'Competência',
      largura: '190px',
      celula: (f) => (
        <div>
          <span className="tabular block font-medium text-foreground">{f.competencia}</span>
          <span className="block text-xs text-muted-foreground">
            {competenciaPorExtenso(f.competencia)}
          </span>
        </div>
      ),
    },
    {
      cabecalho: 'Empresa',
      celula: (f) => <span className="truncate">{f.empresa}</span>,
    },
    {
      cabecalho: 'Funcionários',
      numerica: true,
      largura: '110px',
      celula: (f) => <span className="tabular">{f.quantidadeFuncionarios}</span>,
    },
    {
      cabecalho: 'Proventos',
      numerica: true,
      secundaria: true,
      celula: (f) => <Dinheiro valor={f.totalProventos} />,
    },
    {
      cabecalho: 'Descontos',
      numerica: true,
      secundaria: true,
      celula: (f) => <Dinheiro valor={f.totalDescontos} sinal="desconto" />,
    },
    {
      cabecalho: 'Líquido',
      numerica: true,
      celula: (f) => <Dinheiro valor={f.totalLiquido} enfase />,
    },
    {
      cabecalho: 'Status',
      largura: '130px',
      celula: (f) => <StatusBadge tom={TOM[f.situacao]}>{ROTULO_SITUACAO_FOLHA[f.situacao]}</StatusBadge>,
    },
    {
      cabecalho: 'Tipo',
      largura: '110px',
      celula: (f) => (
        // Sem badge de propósito: o tipo não é um estado que muda, é o que a
        // folha É. Dois badges lado a lado competiriam por atenção.
        <span className={f.tipo === 'Mensal' ? 'text-muted-foreground' : 'font-medium'}>
          {ROTULO_TIPO_FOLHA[f.tipo]}
        </span>
      ),
    },
  ]

  return (
    <>
      <CabecalhoPagina
        titulo="Folhas"
        descricao="Uma folha por empresa e competência. Fechar é definitivo."
        acao={
          processa &&
          empresas.length > 0 && (
            <AbrirFolha
              empresas={empresas}
              idPadrao={empresaAtual?.id ?? empresas[0].id}
              aoAbrir={carregar}
            />
          )
        }
      />

      <DataTable
        rotulo="Folhas de pagamento"
        colunas={colunas}
        itens={filtradas}
        chave={(f) => f.id}
        carregando={carregando}
        erro={erro}
        aoTentarNovamente={() => void carregar()}
        filtrado={temFiltro}
        aoLimparFiltros={() => {
          definirIdEmpresa('')
          definirSituacao('todas')
        }}
        aoClicarLinha={(f) => navegar(`/folhas/${f.id}`)}
        vazio={{
          titulo: 'Nenhuma folha aberta',
          descricao: processa
            ? 'Abra a folha de uma competência para começar o processamento.'
            : 'Nenhuma competência foi aberta ainda.',
        }}
        toolbar={
          <>
            <FiltroSelect
              rotulo="Empresa"
              valor={idEmpresa}
              aoMudar={definirIdEmpresa}
              opcoes={[
                { valor: '', texto: 'Todas as empresas' },
                ...empresas.map((e) => ({
                  valor: e.id,
                  texto: e.nomeFantasia ?? e.razaoSocial,
                })),
              ]}
            />

            <FiltroSelect
              rotulo="Status"
              valor={situacao}
              aoMudar={(v) => definirSituacao(v as 'todas' | SituacaoFolha)}
              opcoes={[
                { valor: 'todas', texto: 'Todos os status' },
                { valor: 'Rascunho', texto: 'Rascunho' },
                { valor: 'Calculada', texto: 'Calculada' },
                { valor: 'Fechada', texto: 'Fechada' },
              ]}
            />

            <EspacoToolbar />
          </>
        }
        rodape={
          <span>
            {filtradas.length} {filtradas.length === 1 ? 'folha' : 'folhas'}
          </span>
        }
      />
    </>
  )
}

/**
 * O que cada tipo de folha faz, para o formulário de abertura.
 *
 * Record exaustivo de propósito: um tipo novo no backend sem explicação aqui
 * quebra a compilação, em vez de a tela descrever a folha errada.
 */
const EXPLICACAO_TIPO_FOLHA: Record<TipoFolha, string> = {
  Mensal:
    'Salário do mês, lançamentos e encargos. Entra quem teve vínculo em qualquer dia.',
  Ferias: 'Paga as férias que começam nesta competência. Só entra quem sai de férias.',
  Rescisao:
    'Paga o acerto de quem foi desligado nesta competência. Motivos sem fonte oficial ficam de fora.',
}

function AbrirFolha({
  empresas,
  idPadrao,
  aoAbrir,
}: {
  empresas: { id: string; razaoSocial: string; nomeFantasia: string | null }[]
  idPadrao: string
  aoAbrir: () => Promise<void>
}) {
  const [aberto, definirAberto] = useState(false)
  const [idEmpresa, definirIdEmpresa] = useState(idPadrao)
  const [competencia, definirCompetencia] = useState('')
  const [erro, definirErro] = useState<string | null>(null)
  const [enviando, definirEnviando] = useState(false)
  const [tipo, definirTipo] = useState<TipoFolha>('Mensal')

  const aoEnviar = async (evento: FormEvent) => {
    evento.preventDefault()
    definirErro(null)

    const normalizada = normalizarCompetencia(competencia)

    if (!normalizada) {
      definirErro('Informe a competência no formato 08/2026.')
      return
    }

    definirEnviando(true)

    try {
      await abrirFolha(idEmpresa, normalizada, tipo)
      definirCompetencia('')
      definirAberto(false)
      await aoAbrir()
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Não foi possível abrir a folha.')
    } finally {
      definirEnviando(false)
    }
  }

  return (
    <Drawer open={aberto} onOpenChange={definirAberto}>
      <DrawerTrigger asChild>
        <Button size="sm">
          <Plus aria-hidden />
          Abrir folha
        </Button>
      </DrawerTrigger>

      <DrawerContent
        titulo="Abrir folha"
        descricao="Uma folha por empresa, competência e tipo. Os três tipos do mesmo mês convivem."
        className="max-w-md"
      >
        <form onSubmit={aoEnviar} className="space-y-4" noValidate>
          <div className="space-y-1.5">
            <Label htmlFor="empresaFolha">Empresa</Label>
            <select
              id="empresaFolha"
              value={idEmpresa}
              onChange={(e) => definirIdEmpresa(e.target.value)}
              className="h-9 w-full rounded-md border border-input bg-card px-3 text-[13px] shadow-xs"
            >
              {empresas.map((e) => (
                <option key={e.id} value={e.id}>
                  {e.nomeFantasia ?? e.razaoSocial}
                </option>
              ))}
            </select>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="competenciaFolha">Competência</Label>
            <Input
              id="competenciaFolha"
              required
              placeholder="08/2026"
              value={competencia}
              onChange={(e) => definirCompetencia(e.target.value)}
              className="tabular"
            />
            <p className="text-xs text-muted-foreground">Mês e ano: 08/2026.</p>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="tipoFolha">Tipo</Label>
            <select
              id="tipoFolha"
              value={tipo}
              onChange={(e) => definirTipo(e.target.value as TipoFolha)}
              className="h-9 w-full rounded-md border border-input bg-card px-3 text-[13px] shadow-xs"
            >
              {(Object.keys(ROTULO_TIPO_FOLHA) as TipoFolha[]).map((x) => (
                <option key={x} value={x}>
                  {ROTULO_TIPO_FOLHA[x]}
                </option>
              ))}
            </select>
            <p className="text-xs text-muted-foreground">
              {EXPLICACAO_TIPO_FOLHA[tipo]}
            </p>
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
              {enviando ? 'Abrindo...' : 'Abrir folha'}
            </Button>
          </div>
        </form>
      </DrawerContent>
    </Drawer>
  )
}
