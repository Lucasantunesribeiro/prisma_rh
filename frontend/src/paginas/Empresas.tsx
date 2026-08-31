import { Plus, Search } from 'lucide-react'
import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import { podeAdministrar } from '@/api/autenticacao'
import {
  consultarCnpj,
  criarEmpresa,
  listarEmpresas,
  type ConsultaCnpj,
  type Empresa,
} from '@/api/empresas'
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

/** Só os dígitos. A máscara é conforto visual; o servidor recebe o número. */
const soDigitos = (texto: string) => texto.replace(/\D/g, '')

function NovaEmpresa({ aoCriar }: { aoCriar: () => Promise<void> }) {
  const [aberto, definirAberto] = useState(false)
  const [razaoSocial, definirRazaoSocial] = useState('')
  const [cnpj, definirCnpj] = useState('')
  const [nomeFantasia, definirNomeFantasia] = useState('')
  const [erro, definirErro] = useState<string | null>(null)
  const [enviando, definirEnviando] = useState(false)

  const [consultando, definirConsultando] = useState(false)
  const [consulta, definirConsulta] = useState<ConsultaCnpj | null>(null)

  const digitos = soDigitos(cnpj)

  const aoConsultar = async () => {
    definirConsultando(true)
    definirConsulta(null)

    try {
      definirConsulta(await consultarCnpj(digitos))
    } catch (falha) {
      // A consulta é auxílio, nunca requisito. Falhar aqui vira aviso na área
      // da consulta e não toca no formulário: o cadastro manual segue igual.
      definirConsulta({
        situacao: 'Indisponivel',
        mensagem: falha instanceof Error ? falha.message : 'Não foi possível consultar agora.',
        dados: null,
        jaCadastrada: false,
      })
    } finally {
      definirConsultando(false)
    }
  }

  /**
   * Preenche o formulário — só quando a pessoa clica.
   *
   * A resposta da Receita **não** cai nos campos sozinha. O que ela substitui
   * fica escrito antes, ao lado de cada campo, para ninguém perder o que
   * digitou sem ver.
   */
  const aoUsarDados = () => {
    if (!consulta?.dados) {
      return
    }

    definirRazaoSocial(consulta.dados.razaoSocial)

    // Nome fantasia vazio na Receita não apaga o que a pessoa escreveu:
    // "a Receita não sabe" é diferente de "não tem".
    if (consulta.dados.nomeFantasia) {
      definirNomeFantasia(consulta.dados.nomeFantasia)
    }
  }

  const aoEnviar = async (evento: FormEvent) => {
    evento.preventDefault()
    definirErro(null)
    definirEnviando(true)

    try {
      await criarEmpresa({ razaoSocial, cnpj: digitos, nomeFantasia: nomeFantasia || null })
      definirRazaoSocial('')
      definirCnpj('')
      definirNomeFantasia('')
      definirConsulta(null)
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
              <div className="flex gap-2">
                <Input
                  id="cnpj"
                  required
                  inputMode="numeric"
                  placeholder="00.000.000/0000-00"
                  value={cnpj}
                  onChange={(e) => {
                    definirCnpj(e.target.value)
                    definirConsulta(null)
                  }}
                  className="tabular"
                />
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  className="shrink-0"
                  // Sem os quatorze dígitos não há o que perguntar. O servidor
                  // recusaria de qualquer jeito; a tela evita a viagem.
                  disabled={digitos.length !== 14 || consultando}
                  onClick={() => void aoConsultar()}
                >
                  <Search aria-hidden />
                  {consultando ? 'Buscando…' : 'Buscar'}
                </Button>
              </div>
              <p className="text-xs text-muted-foreground">
                Busca razão social e nome fantasia na Receita Federal. Opcional — dá para
                preencher tudo à mão.
              </p>
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

          {consulta && (
            <ResultadoDaConsulta
              consulta={consulta}
              razaoSocialAtual={razaoSocial}
              nomeFantasiaAtual={nomeFantasia}
              aoUsar={aoUsarDados}
            />
          )}

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

/**
 * O que a Receita respondeu, e o que aceitar isso vai mudar.
 *
 * ## Nada é preenchido sozinho
 *
 * A resposta fica **aqui**, fora do formulário, até alguém clicar em "Usar
 * estes dados". Preencher automaticamente pareceria mais prático e apagaria o
 * que a pessoa digitou sem ela ver — e num cadastro de empresa o que ela
 * digitou pode ser justamente a correção.
 *
 * Por isso cada campo que **vai ser substituído** mostra antes o valor atual.
 * O aviso é a diferença entre ajudar e atropelar.
 *
 * ## Isto não é fonte de verdade
 *
 * A Receita informa; quem decide é quem cadastra. A situação cadastral aparece
 * para a pessoa ver que o CNPJ está BAIXADO **antes** de criar a empresa — e
 * não é guardada em lugar nenhum.
 */
function ResultadoDaConsulta({
  consulta,
  razaoSocialAtual,
  nomeFantasiaAtual,
  aoUsar,
}: {
  consulta: ConsultaCnpj
  razaoSocialAtual: string
  nomeFantasiaAtual: string
  aoUsar: () => void
}) {
  if (!consulta.dados) {
    return (
      <Alert role="status">
        <AlertDescription>
          {consulta.mensagem}
          {consulta.situacao === 'Indisponivel' && (
            <span className="mt-1 block text-xs text-muted-foreground">
              A consulta é opcional: preencha os campos à mão e cadastre normalmente.
            </span>
          )}
        </AlertDescription>
      </Alert>
    )
  }

  const { razaoSocial, nomeFantasia, situacaoCadastral, ativaNaReceita } = consulta.dados

  const substituiRazao = razaoSocialAtual.trim() !== '' && razaoSocialAtual !== razaoSocial
  const substituiFantasia =
    nomeFantasia !== null && nomeFantasiaAtual.trim() !== '' && nomeFantasiaAtual !== nomeFantasia

  return (
    <div className="space-y-3 rounded-md border border-border bg-muted/40 p-3">
      <div className="flex items-start justify-between gap-3">
        <span className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
          Encontrado na Receita Federal
        </span>
        <span
          className={
            ativaNaReceita
              ? 'rounded px-1.5 py-0.5 text-xs text-muted-foreground'
              : 'rounded bg-destructive/10 px-1.5 py-0.5 text-xs font-medium text-destructive'
          }
        >
          {situacaoCadastral}
        </span>
      </div>

      <dl className="space-y-2 text-sm">
        <div>
          <dt className="text-xs text-muted-foreground">Razão social</dt>
          <dd className="font-medium text-foreground">{razaoSocial}</dd>
          {substituiRazao && (
            <dd className="text-xs text-amber-600 dark:text-amber-500">
              substitui o que você digitou: “{razaoSocialAtual}”
            </dd>
          )}
        </div>

        {nomeFantasia && (
          <div>
            <dt className="text-xs text-muted-foreground">Nome fantasia</dt>
            <dd className="font-medium text-foreground">{nomeFantasia}</dd>
            {substituiFantasia && (
              <dd className="text-xs text-amber-600 dark:text-amber-500">
                substitui o que você digitou: “{nomeFantasiaAtual}”
              </dd>
            )}
          </div>
        )}
      </dl>

      {!ativaNaReceita && (
        <p className="text-xs text-destructive">
          Este CNPJ não está ativo na Receita. Confira antes de cadastrar.
        </p>
      )}

      {consulta.jaCadastrada && (
        <p className="text-xs text-amber-600 dark:text-amber-500">
          Já existe uma empresa com este CNPJ nesta organização.
        </p>
      )}

      <Button type="button" size="sm" variant="secondary" onClick={aoUsar}>
        Usar estes dados
      </Button>
    </div>
  )
}
