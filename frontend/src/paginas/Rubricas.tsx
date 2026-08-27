import { Plus } from 'lucide-react'
import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import { podeAdministrar } from '@/api/autenticacao'
import {
  BASES,
  criarRubrica,
  inativarRubrica,
  juntarBases,
  listarRubricas,
  ORIGEM_DO_VALOR,
  ROTULO_BASE,
  ROTULO_TIPO_RUBRICA,
  separarBases,
  type BaseCalculo,
  type Rubrica,
  type TipoRubrica,
} from '@/api/folha'
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
import { Dialog, DialogClose, DialogContent, DialogTrigger } from '@/components/ui/dialog'
import { Drawer, DrawerClose, DrawerContent, DrawerTrigger } from '@/components/ui/drawer'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { usePagina } from '@/layout/usePagina'

export default function Rubricas() {
  const { usuario } = useSessao()
  const administra = podeAdministrar(usuario?.perfil)

  usePagina([{ texto: 'Folha' }, { texto: 'Rubricas' }])

  const [rubricas, definirRubricas] = useState<Rubrica[]>([])
  const [carregando, definirCarregando] = useState(true)
  const [erro, definirErro] = useState<string | null>(null)
  const [busca, definirBusca] = useState('')
  const [tipo, definirTipo] = useState<'todos' | TipoRubrica>('todos')

  const carregar = useCallback(async () => {
    definirCarregando(true)
    definirErro(null)

    try {
      definirRubricas(await listarRubricas())
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Não foi possível carregar as rubricas.')
    } finally {
      definirCarregando(false)
    }
  }, [])

  useEffect(() => {
    // O estado só muda DEPOIS do await, quando a resposta da API chega.
    // oxlint-disable-next-line react/set-state-in-effect
    void carregar()
  }, [carregar])

  const temSalario = rubricas.some(
    (r) => r.ativa && r.estrategia === 'SalarioBaseProporcional',
  )

  const filtradas = useMemo(() => {
    const termo = busca.trim().toLowerCase()

    return rubricas.filter(
      (r) =>
        (tipo === 'todos' || r.tipo === tipo) &&
        (!termo || `${r.codigo} ${r.nome}`.toLowerCase().includes(termo)),
    )
  }, [rubricas, busca, tipo])

  const colunas: Coluna<Rubrica>[] = [
    {
      cabecalho: 'Código',
      largura: '110px',
      celula: (r) => <span className="tabular text-muted-foreground">{r.codigo}</span>,
    },
    {
      cabecalho: 'Rubrica',
      celula: (r) => <span className="font-medium text-foreground">{r.nome}</span>,
    },
    {
      cabecalho: 'Tipo',
      largura: '120px',
      celula: (r) => ROTULO_TIPO_RUBRICA[r.tipo],
    },
    {
      cabecalho: 'Origem do valor',
      largura: '180px',
      secundaria: true,
      celula: (r) => <span className="text-muted-foreground">{ORIGEM_DO_VALOR[r.estrategia]}</span>,
    },
    {
      cabecalho: 'Compõe base de',
      largura: '190px',
      celula: (r) => {
        const bases = separarBases(r.basesIncidentes)

        /*
         * Texto, e não três badges coloridos por linha: numa lista de vinte
         * rubricas, sessenta pastilhas viram ruído e a coluna deixa de poder
         * ser lida de cima a baixo.
         */
        if (bases.length === 0) {
          return (
            <span className="text-muted-foreground">
              {r.tipo === 'Desconto' ? '—' : 'nenhuma'}
            </span>
          )
        }

        return <span>{bases.map((b) => ROTULO_BASE[b]).join(' · ')}</span>
      },
    },
    {
      cabecalho: 'Situação',
      largura: '110px',
      celula: (r) => (
        <StatusBadge tom={r.ativa ? 'sucesso' : 'neutro'}>
          {r.ativa ? 'Ativa' : 'Inativa'}
        </StatusBadge>
      ),
    },
    ...(administra
      ? [
          {
            cabecalho: '',
            largura: '90px',
            className: 'text-right',
            celula: (r: Rubrica) =>
              r.ativa ? <Inativar rubrica={r} aoInativar={carregar} /> : null,
          } satisfies Coluna<Rubrica>,
        ]
      : []),
  ]

  return (
    <>
      <CabecalhoPagina
        titulo="Rubricas"
        descricao="Catálogo de eventos da folha. A incidência define em quais bases cada rubrica entra."
        acao={administra && <NovaRubrica temSalario={temSalario} aoCriar={carregar} />}
      />

      {!carregando && !erro && !temSalario && rubricas.length > 0 && (
        <Alert className="mb-5">
          <AlertDescription>
            Nenhuma rubrica de salário-base ativa. Sem ela a folha não tem o que calcular.
          </AlertDescription>
        </Alert>
      )}

      <DataTable
        rotulo="Catálogo de rubricas"
        colunas={colunas}
        itens={filtradas}
        chave={(r) => r.id}
        carregando={carregando}
        erro={erro}
        aoTentarNovamente={() => void carregar()}
        filtrado={busca.trim().length > 0 || tipo !== 'todos'}
        aoLimparFiltros={() => {
          definirBusca('')
          definirTipo('todos')
        }}
        vazio={{
          titulo: 'Nenhuma rubrica cadastrada',
          descricao: 'A folha precisa de ao menos uma rubrica de salário-base para calcular.',
        }}
        toolbar={
          <>
            <CampoBusca
              rotulo="Buscar rubrica"
              placeholder="Buscar por código ou nome"
              valor={busca}
              aoMudar={definirBusca}
            />

            <FiltroSelect
              rotulo="Tipo"
              valor={tipo}
              aoMudar={(v) => definirTipo(v as 'todos' | TipoRubrica)}
              opcoes={[
                { valor: 'todos', texto: 'Todos os tipos' },
                { valor: 'Provento', texto: 'Proventos' },
                { valor: 'Desconto', texto: 'Descontos' },
                { valor: 'Informativo', texto: 'Informativos' },
              ]}
            />

            <EspacoToolbar />
          </>
        }
        rodape={
          <span>
            {filtradas.length} {filtradas.length === 1 ? 'rubrica' : 'rubricas'}
          </span>
        }
      />
    </>
  )
}

function Inativar({ rubrica, aoInativar }: { rubrica: Rubrica; aoInativar: () => Promise<void> }) {
  const [aberto, definirAberto] = useState(false)

  return (
    <Dialog open={aberto} onOpenChange={definirAberto}>
      <DialogTrigger asChild>
        <Button variant="ghost" size="sm">
          Inativar
        </Button>
      </DialogTrigger>

      <DialogContent
        titulo={`Inativar ${rubrica.codigo}?`}
        descricao="A rubrica deixa de aparecer para novos lançamentos. Os holerites já emitidos não mudam."
      >
        <DialogClose asChild>
          <Button variant="outline" size="sm">
            Cancelar
          </Button>
        </DialogClose>
        <Button
          size="sm"
          onClick={() => {
            definirAberto(false)
            void inativarRubrica(rubrica.id).then(aoInativar)
          }}
        >
          Inativar
        </Button>
      </DialogContent>
    </Dialog>
  )
}

function NovaRubrica({
  temSalario,
  aoCriar,
}: {
  temSalario: boolean
  aoCriar: () => Promise<void>
}) {
  const [aberto, definirAberto] = useState(false)
  const [codigo, definirCodigo] = useState('')
  const [nome, definirNome] = useState('')
  const [tipo, definirTipo] = useState<TipoRubrica>('Provento')
  const [bases, definirBases] = useState<BaseCalculo[]>([])
  const [salarioBase, definirSalarioBase] = useState(false)
  const [erro, definirErro] = useState<string | null>(null)
  const [enviando, definirEnviando] = useState(false)

  const tipoAtual: TipoRubrica = salarioBase ? 'Provento' : tipo
  const desconto = tipoAtual === 'Desconto'

  const aoEnviar = async (evento: FormEvent) => {
    evento.preventDefault()
    definirErro(null)
    definirEnviando(true)

    try {
      await criarRubrica({
        codigo,
        nome,
        tipo: tipoAtual,
        estrategia: salarioBase ? 'SalarioBaseProporcional' : 'ValorInformado',
        // Desconto nunca compõe base: o backend recusa, e mandar assim evita
        // um 400 previsível quando alguém marca as caixas e depois troca o tipo.
        basesIncidentes: desconto ? 'Nenhuma' : juntarBases(bases),
      })

      definirCodigo('')
      definirNome('')
      definirBases([])
      definirSalarioBase(false)
      definirAberto(false)
      await aoCriar()
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Não foi possível criar a rubrica.')
    } finally {
      definirEnviando(false)
    }
  }

  return (
    <Drawer open={aberto} onOpenChange={definirAberto}>
      <DrawerTrigger asChild>
        <Button size="sm">
          <Plus aria-hidden />
          Nova rubrica
        </Button>
      </DrawerTrigger>

      <DrawerContent titulo="Nova rubrica" className="max-w-lg">
        <form onSubmit={aoEnviar} className="space-y-5" noValidate>
          <section className="space-y-4">
            <div className="grid gap-4 sm:grid-cols-[9rem_1fr]">
              <div className="space-y-1.5">
                <Label htmlFor="codigoRubrica">Código</Label>
                <Input
                  id="codigoRubrica"
                  required
                  autoFocus
                  placeholder="VT"
                  value={codigo}
                  onChange={(e) => definirCodigo(e.target.value)}
                  className="tabular"
                />
              </div>

              <div className="space-y-1.5">
                <Label htmlFor="nomeRubrica">Nome</Label>
                <Input
                  id="nomeRubrica"
                  required
                  value={nome}
                  onChange={(e) => definirNome(e.target.value)}
                />
              </div>
            </div>

            <div className="grid gap-4 sm:grid-cols-2">
              <div className="space-y-1.5">
                <Label htmlFor="tipoRubrica">Tipo</Label>
                <select
                  id="tipoRubrica"
                  value={tipoAtual}
                  disabled={salarioBase}
                  onChange={(e) => definirTipo(e.target.value as TipoRubrica)}
                  className="h-9 w-full rounded-md border border-input bg-card px-3 text-[13px] shadow-xs disabled:opacity-50"
                >
                  <option value="Provento">Provento</option>
                  <option value="Desconto">Desconto</option>
                  <option value="Informativo">Informativo</option>
                </select>
              </div>

              <div className="flex items-end pb-2">
                <label className="flex items-center gap-2 text-[13px]">
                  <input
                    type="checkbox"
                    className="size-4"
                    checked={salarioBase}
                    disabled={temSalario}
                    onChange={(e) => definirSalarioBase(e.target.checked)}
                  />
                  Salário-base
                  {temSalario && (
                    <span className="text-xs text-muted-foreground">(já existe)</span>
                  )}
                </label>
              </div>
            </div>
          </section>

          <fieldset disabled={desconto} className="border-t border-border pt-4">
            <legend className="sr-only">Incidências</legend>

            <p className="mb-2 text-[13px] font-medium">Compõe base de</p>

            {desconto ? (
              <p className="text-xs text-muted-foreground">
                Desconto não compõe base. O que reduz base é dedução, que é outro conceito.
              </p>
            ) : (
              <div className="flex flex-wrap gap-4">
                {BASES.map((base) => (
                  <label key={base} className="flex items-center gap-2 text-[13px]">
                    <input
                      type="checkbox"
                      className="size-4"
                      checked={bases.includes(base)}
                      onChange={(e) =>
                        definirBases((atuais) =>
                          e.target.checked ? [...atuais, base] : atuais.filter((b) => b !== base),
                        )
                      }
                    />
                    {ROTULO_BASE[base]}
                  </label>
                ))}
              </div>
            )}
          </fieldset>

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
              {enviando ? 'Criando...' : 'Criar rubrica'}
            </Button>
          </div>
        </form>
      </DrawerContent>
    </Drawer>
  )
}
