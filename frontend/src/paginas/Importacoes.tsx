import { AlertTriangle, CheckCircle2, Download, FileSpreadsheet, Upload, XCircle } from 'lucide-react'
import { useCallback, useEffect, useId, useMemo, useRef, useState } from 'react'
import { podeAdministrarPessoas } from '@/api/autenticacao'
import {
  EXTENSOES_ACEITAS,
  ROTULO_TRABALHO,
  enfileirarImportacao,
  ROTULO_FORMATO,
  ROTULO_STATUS,
  baixarModelo,
  confirmarFuncionarios,
  listarImportacoes,
  obterImportacao,
  previewFuncionarios,
  type ConfirmacaoImportacao,
  type ImportacaoDetalhe,
  type ImportacaoResumo,
  type LinhaPreview,
  type MapeamentoFuncionarios,
  type PreviewImportacao,
  type StatusImportacao,
} from '@/api/importacoes'
import { useSessao } from '@/auth/useSessao'
import { useTrabalho } from './useTrabalho'
import { DataTable, type Coluna } from '@/components/sistema/DataTable'
import {
  CabecalhoPagina,
  CabecalhoSecao,
  Campo,
  ListaCampos,
  StatusBadge,
} from '@/components/sistema/Primitivos'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Drawer, DrawerContent } from '@/components/ui/drawer'
import { Label } from '@/components/ui/label'
import { usePagina } from '@/layout/usePagina'
import { cn } from '@/lib/utils'

const TOM_STATUS: Record<StatusImportacao, 'sucesso' | 'critico' | 'info'> = {
  Aplicada: 'sucesso',
  Recusada: 'critico',
  Analisada: 'info',
}

/**
 * Importação de funcionários por arquivo (Fase 5, etapa 5).
 *
 * ## O que esta tela NÃO faz
 *
 * Ela não decide o que é válido. O resumo, os erros e a marcação de cada linha
 * vêm inteiramente da resposta do servidor, e na confirmação **o arquivo é
 * reenviado** para ser lido e validado de novo, do zero.
 *
 * Não existe "id da prévia" nem lista de linhas aprovadas trafegando daqui para
 * lá. Se alguém alterar esta página no navegador, o resultado da importação não
 * muda — porque nada do que ela calculou é aproveitado pelo backend.
 *
 * A tela valida para dar boa experiência; a autoridade é do servidor
 * (`CLAUDE.md §21`).
 */
export default function Importacoes() {
  usePagina([{ texto: 'Pessoas' }, { texto: 'Importações' }])

  const { usuario } = useSessao()
  const importa = podeAdministrarPessoas(usuario?.perfil)

  const [historico, definirHistorico] = useState<ImportacaoResumo[]>([])
  const [carregando, definirCarregando] = useState(true)
  const [erro, definirErro] = useState<string | null>(null)

  const carregar = useCallback(async () => {
    definirCarregando(true)
    definirErro(null)

    try {
      definirHistorico((await listarImportacoes()).itens)
    } catch (falha) {
      definirErro(
        falha instanceof Error ? falha.message : 'Não foi possível carregar o histórico.',
      )
    } finally {
      definirCarregando(false)
    }
  }, [])

  useEffect(() => {
    // O estado só muda DEPOIS do await, quando a resposta da API chega.
    // oxlint-disable-next-line react/set-state-in-effect
    void carregar()
  }, [carregar])

  return (
    <>
      <CabecalhoPagina
        titulo="Importações"
        descricao="Cadastro de funcionários em massa, por CSV ou XLSX. Nada é gravado antes da confirmação."
        acao={importa && <BaixarModelo />}
      />

      {importa && <Enviar aoConcluir={carregar} />}

      <Historico
        itens={historico}
        carregando={carregando}
        erro={erro}
        aoTentarNovamente={() => void carregar()}
      />
    </>
  )
}

// --------------------------------------------------------------------- modelo

function BaixarModelo() {
  const [baixando, definirBaixando] = useState<string | null>(null)
  const [erro, definirErro] = useState<string | null>(null)

  const baixar = async (formato: 'csv' | 'xlsx') => {
    definirBaixando(formato)
    definirErro(null)

    try {
      await baixarModelo(formato)
    } catch {
      definirErro('Não foi possível baixar o modelo.')
    } finally {
      definirBaixando(null)
    }
  }

  return (
    <div className="flex flex-col items-end gap-1">
      <div className="flex gap-2">
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={baixando !== null}
          onClick={() => void baixar('csv')}
        >
          <Download aria-hidden />
          {baixando === 'csv' ? 'Baixando...' : 'Modelo CSV'}
        </Button>

        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={baixando !== null}
          onClick={() => void baixar('xlsx')}
        >
          <Download aria-hidden />
          {baixando === 'xlsx' ? 'Baixando...' : 'Modelo XLSX'}
        </Button>
      </div>

      {erro && (
        <p role="alert" className="text-[11px] text-critico">
          {erro}
        </p>
      )}
    </div>
  )
}

// ---------------------------------------------------------------------- envio

function Enviar({ aoConcluir }: { aoConcluir: () => Promise<void> }) {
  const idCampo = useId()
  const campo = useRef<HTMLInputElement>(null)

  const [arquivo, definirArquivo] = useState<File | null>(null)
  const [previa, definirPrevia] = useState<PreviewImportacao | null>(null)
  const [mapeamento, definirMapeamento] = useState<MapeamentoFuncionarios | null>(null)
  const [resultado, definirResultado] = useState<ConfirmacaoImportacao | null>(null)

  const [analisando, definirAnalisando] = useState(false)
  const [confirmando, definirConfirmando] = useState(false)
  const [falha, definirFalha] = useState<string | null>(null)

  // Processamento em segundo plano (Fase 9). `idTrabalho` liga o acompanhamento;
  // o hook para sozinho quando o trabalho deixa de estar pendente.
  const [idTrabalho, definirIdTrabalho] = useState<string | null>(null)
  const [enfileirando, definirEnfileirando] = useState(false)
  const acompanhamento = useTrabalho(idTrabalho)

  const analisar = useCallback(
    async (escolhido: File, escolha?: MapeamentoFuncionarios) => {
      definirAnalisando(true)
      definirFalha(null)
      definirResultado(null)

      try {
        const resposta = await previewFuncionarios(escolhido, escolha)

        definirPrevia(resposta)
        definirMapeamento(resposta.mapeamento)
      } catch (erro) {
        definirPrevia(null)
        definirMapeamento(null)
        definirFalha(erro instanceof Error ? erro.message : 'Não foi possível ler o arquivo.')
      } finally {
        definirAnalisando(false)
      }
    },
    [],
  )

  const escolher = (lista: FileList | null) => {
    const escolhido = lista?.[0] ?? null

    definirArquivo(escolhido)
    definirPrevia(null)
    definirMapeamento(null)
    definirResultado(null)
    definirFalha(null)

    if (escolhido) {
      void analisar(escolhido)
    }
  }

  const remapear = (parte: Partial<MapeamentoFuncionarios>) => {
    if (!arquivo || !mapeamento) return

    const novo = { ...mapeamento, ...parte }

    definirMapeamento(novo)
    void analisar(arquivo, novo)
  }

  /**
   * Manda a planilha para a fila em vez de processar na requisição.
   *
   * O arquivo vai de novo, como na confirmação síncrona — o servidor relê e
   * revalida, e é a leitura dele que decide. O que muda é **onde** isso
   * acontece: fora da requisição, num worker.
   */
  const enfileirar = async () => {
    if (!arquivo) return

    definirEnfileirando(true)
    definirFalha(null)
    definirIdTrabalho(null)

    try {
      const trabalho = await enfileirarImportacao(arquivo)

      definirIdTrabalho(trabalho.id)
      definirPrevia(null)
    } catch (erro) {
      definirFalha(erro instanceof Error ? erro.message : 'Não foi possível enfileirar.')
    } finally {
      definirEnfileirando(false)
    }
  }

  const confirmar = async () => {
    if (!arquivo) return

    definirConfirmando(true)
    definirFalha(null)

    try {
      // O ARQUIVO vai de novo. Não os totais, não as linhas, não o hash: o
      // servidor relê e revalida, e é a leitura dele que decide.
      const resposta = await confirmarFuncionarios(arquivo, mapeamento ?? undefined)

      definirResultado(resposta)
      definirPrevia(null)
      await aoConcluir()
    } catch (erro) {
      definirFalha(erro instanceof Error ? erro.message : 'Não foi possível confirmar.')
    } finally {
      definirConfirmando(false)
    }
  }

  const recomecar = () => {
    definirArquivo(null)
    definirPrevia(null)
    definirMapeamento(null)
    definirResultado(null)
    definirFalha(null)

    if (campo.current) {
      // Sem limpar o input, escolher o MESMO arquivo de novo não dispara
      // `change` — e a tela ficaria parada sem explicar por quê.
      campo.current.value = ''
    }
  }

  return (
    <section className="mb-4 overflow-hidden rounded-lg border border-border-forte bg-card shadow-[0_1px_2px_rgba(17,26,46,0.04)]">
      <div className="border-b border-border px-4 py-3">
        <CabecalhoSecao
          titulo="Novo arquivo"
          descricao="CSV ou XLSX, até 5 MB e 10 mil linhas. Colunas: nome, CPF e data de nascimento."
        />
      </div>

      <div className="space-y-4 px-4 py-4">
        <div className="flex flex-wrap items-end gap-3">
          <div className="min-w-0 flex-1 space-y-1.5">
            <Label htmlFor={idCampo}>Arquivo</Label>
            <input
              ref={campo}
              id={idCampo}
              type="file"
              accept={EXTENSOES_ACEITAS}
              onChange={(e) => escolher(e.target.files)}
              className={cn(
                'block w-full rounded-md border border-input bg-card text-[13px] text-foreground shadow-xs',
                'file:mr-3 file:cursor-pointer file:border-0 file:bg-secondary file:px-3 file:py-2',
                'file:text-[13px] file:font-medium file:text-secondary-foreground',
                'focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2',
              )}
            />
          </div>

          {(previa || resultado || falha) && (
            <Button type="button" variant="outline" size="sm" onClick={recomecar}>
              Recomeçar
            </Button>
          )}
        </div>

        <div aria-live="polite" aria-busy={analisando} className="space-y-4">
          {analisando && <Analisando />}

          {!analisando && falha && (
            <Alert variant="destructive" role="alert">
              <AlertDescription>{falha}</AlertDescription>
            </Alert>
          )}

          {!analisando && !falha && previa && mapeamento && (
            <Previa
              previa={previa}
              mapeamento={mapeamento}
              aoRemapear={remapear}
              confirmando={confirmando}
              aoConfirmar={() => void confirmar()}
              enfileirando={enfileirando}
              aoEnfileirar={() => void enfileirar()}
            />
          )}

          {idTrabalho && (
            <Acompanhamento
              acompanhamento={acompanhamento}
              aoConcluir={aoConcluir}
              aoFechar={() => definirIdTrabalho(null)}
            />
          )}

          {!analisando && resultado && <Resultado resultado={resultado} />}

          {!analisando && !falha && !previa && !resultado && !arquivo && (
            <p className="text-[13px] text-muted-foreground">
              Escolha um arquivo para ver a prévia. Nada é gravado até você confirmar.
            </p>
          )}
        </div>
      </div>
    </section>
  )
}

function Analisando() {
  return (
    <div className="space-y-2">
      <div className="h-3 w-1/3 animate-pulse rounded bg-muted" />
      <div className="h-3 w-2/3 animate-pulse rounded bg-muted" />
      <div className="h-3 w-1/2 animate-pulse rounded bg-muted" />
      <span className="sr-only">Lendo e validando o arquivo</span>
    </div>
  )
}

// --------------------------------------------------------------------- prévia

function Previa({
  previa,
  mapeamento,
  aoRemapear,
  confirmando,
  aoConfirmar,
  enfileirando,
  aoEnfileirar,
}: {
  previa: PreviewImportacao
  mapeamento: MapeamentoFuncionarios
  aoRemapear: (parte: Partial<MapeamentoFuncionarios>) => void
  confirmando: boolean
  aoConfirmar: () => void
  enfileirando: boolean
  aoEnfileirar: () => void
}) {
  const colunas: Coluna<LinhaPreview>[] = useMemo(
    () => [
      {
        cabecalho: 'Linha',
        largura: '72px',
        numerica: true,
        celula: (l) => <span className="tabular text-muted-foreground">{l.linha}</span>,
      },
      {
        cabecalho: 'Nome',
        celula: (l) => <span className="font-medium text-foreground">{l.nome ?? '—'}</span>,
      },
      {
        cabecalho: 'CPF',
        largura: '150px',
        celula: (l) => <span className="tabular">{l.cpf ?? '—'}</span>,
      },
      {
        cabecalho: 'Nascimento',
        largura: '130px',
        secundaria: true,
        celula: (l) => <span className="tabular">{formatarData(l.dataNascimento)}</span>,
      },
      {
        cabecalho: 'Situação',
        celula: (l) =>
          l.erros.length === 0 ? (
            <StatusBadge tom="sucesso">Válida</StatusBadge>
          ) : (
            <ul className="space-y-0.5">
              {l.erros.map((e) => (
                <li key={e} className="text-[12.5px] text-critico">
                  {e}
                </li>
              ))}
            </ul>
          ),
      },
    ],
    [],
  )

  return (
    <div className="space-y-4">
      <ListaCampos colunas={4}>
        <Campo rotulo="Arquivo">{previa.nomeArquivo}</Campo>
        <Campo rotulo="Formato">{ROTULO_FORMATO[previa.formato]}</Campo>
        <Campo rotulo="Linhas">
          <span className="tabular">{previa.total}</span>
        </Campo>
        <Campo rotulo="Com erro">
          <span className={cn('tabular', previa.comErro > 0 && 'font-semibold text-critico')}>
            {previa.comErro}
          </span>
        </Campo>
      </ListaCampos>

      {previa.colunas.length > 0 && (
        <Mapeamento colunas={previa.colunas} mapeamento={mapeamento} aoMudar={aoRemapear} />
      )}

      {previa.errosDoArquivo.length > 0 && (
        <Alert variant="destructive" role="alert">
          <AlertDescription>
            <ul className="space-y-1">
              {previa.errosDoArquivo.map((e) => (
                <li key={e}>{e}</li>
              ))}
            </ul>
          </AlertDescription>
        </Alert>
      )}

      {previa.linhas.length > 0 && (
        <DataTable
          rotulo="Prévia das linhas do arquivo"
          colunas={colunas}
          itens={previa.linhas}
          chave={(l) => String(l.linha)}
        />
      )}

      <div className="flex flex-wrap items-center justify-between gap-3 border-t border-border pt-4">
        <p className="text-[12.5px] text-muted-foreground">
          {previa.importavel ? (
            <>
              Ao confirmar, o arquivo é enviado de novo e o servidor o lê e valida outra vez antes
              de gravar.
            </>
          ) : (
            <>
              <AlertTriangle className="mr-1 inline size-3.5 align-[-2px]" aria-hidden />
              Corrija o arquivo e envie de novo. Nenhuma linha é importada enquanto houver erro.
            </>
          )}
        </p>

        <div className="flex gap-2">
          {/*
            Segundo plano: mesma planilha, mesmo servidor revalidando, mas fora
            da requisição. Fica ao lado e não no lugar — para uma folha de RH o
            caminho direto responde em segundos, e resposta imediata é melhor
            que resposta correta daqui a pouco.
          */}
          <Button
            type="button"
            size="sm"
            variant="outline"
            disabled={!previa.importavel || confirmando || enfileirando}
            onClick={aoEnfileirar}
          >
            {enfileirando ? 'Enviando...' : 'Processar em segundo plano'}
          </Button>

          <Button
            type="button"
            size="sm"
            disabled={!previa.importavel || confirmando || enfileirando}
            onClick={aoConfirmar}
          >
            <Upload aria-hidden />
            {confirmando ? 'Importando...' : rotuloDoBotao(previa)}
          </Button>
        </div>
      </div>
    </div>
  )
}

/**
 * O rótulo do botão diz quantas pessoas entram — quando alguma entra.
 *
 * Com o arquivo recusado, "Importar 0 funcionários" soa como uma ação que
 * alguém poderia querer executar. O botão está desabilitado de qualquer jeito,
 * mas o texto de um botão desabilitado ainda é a explicação do que falta.
 */
function rotuloDoBotao(previa: PreviewImportacao): string {
  if (!previa.importavel) return 'Importar'

  return previa.validas === 1 ? 'Importar 1 funcionário' : `Importar ${previa.validas} funcionários`
}

/**
 * De qual coluna do arquivo sai cada campo.
 *
 * As opções são o cabeçalho que o SERVIDOR leu — a tela não inventa nomes. E o
 * backend reconfere a escolha contra o arquivo reenviado: nome de coluna que
 * não existe lá não vira importação, vira recusa.
 */
function Mapeamento({
  colunas,
  mapeamento,
  aoMudar,
}: {
  colunas: string[]
  mapeamento: MapeamentoFuncionarios
  aoMudar: (parte: Partial<MapeamentoFuncionarios>) => void
}) {
  return (
    <fieldset className="rounded-md border border-border bg-muted/30 px-3 py-3">
      <legend className="px-1 text-xs font-medium text-muted-foreground">
        De onde vem cada campo
      </legend>

      <div className="grid gap-3 sm:grid-cols-3">
        <SelectColuna
          rotulo="Nome"
          valor={mapeamento.nome}
          colunas={colunas}
          aoMudar={(nome) => aoMudar({ nome })}
        />
        <SelectColuna
          rotulo="CPF"
          valor={mapeamento.cpf}
          colunas={colunas}
          aoMudar={(cpf) => aoMudar({ cpf })}
        />
        <SelectColuna
          rotulo="Data de nascimento"
          valor={mapeamento.dataNascimento}
          colunas={colunas}
          aoMudar={(dataNascimento) => aoMudar({ dataNascimento })}
        />
      </div>
    </fieldset>
  )
}

function SelectColuna({
  rotulo,
  valor,
  colunas,
  aoMudar,
}: {
  rotulo: string
  valor: string
  colunas: string[]
  aoMudar: (valor: string) => void
}) {
  const id = useId()

  // Quando a coluna escolhida não está no arquivo, ela ainda precisa aparecer
  // no select — senão o campo mostraria outra coisa e esconderia o erro.
  const opcoes = colunas.includes(valor) ? colunas : [valor, ...colunas]

  return (
    <div className="space-y-1">
      <Label htmlFor={id} className="text-xs text-muted-foreground">
        {rotulo}
      </Label>
      <select
        id={id}
        value={valor}
        onChange={(e) => aoMudar(e.target.value)}
        className={cn(
          'h-8 w-full rounded-md border border-input bg-card px-2 text-[13px] text-foreground shadow-xs',
          'focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2',
          !colunas.includes(valor) && 'border-critico text-critico',
        )}
      >
        {opcoes.map((c) => (
          <option key={c} value={c}>
            {c}
          </option>
        ))}
      </select>
    </div>
  )
}

// ------------------------------------------------------------------ resultado

function Resultado({ resultado }: { resultado: ConfirmacaoImportacao }) {
  const aplicada = resultado.status === 'Aplicada'

  return (
    <div
      className={cn(
        'rounded-md border px-4 py-3',
        aplicada ? 'border-sucesso/40 bg-sucesso-suave' : 'border-critico/40 bg-critico-suave',
      )}
      role="status"
    >
      <p className="flex items-center gap-2 text-[13.5px] font-semibold">
        {aplicada ? (
          <CheckCircle2 className="size-4 text-sucesso" aria-hidden />
        ) : (
          <XCircle className="size-4 text-critico" aria-hidden />
        )}
        {aplicada
          ? `${resultado.funcionariosCriados} ${resultado.funcionariosCriados === 1 ? 'funcionário importado' : 'funcionários importados'}`
          : 'Importação recusada — nenhum funcionário foi criado'}
      </p>

      {!aplicada && resultado.errosDoArquivo.length > 0 && (
        <ul className="mt-2 space-y-1 text-[12.5px] text-critico">
          {resultado.errosDoArquivo.map((e) => (
            <li key={e}>{e}</li>
          ))}
        </ul>
      )}

      {!aplicada && resultado.comErro > 0 && (
        <p className="mt-2 text-[12.5px] text-critico">
          {resultado.comErro} {resultado.comErro === 1 ? 'linha' : 'linhas'} com erro. O relatório
          completo fica no histórico abaixo.
        </p>
      )}
    </div>
  )
}

// ------------------------------------------------------------------ histórico

function Historico({
  itens,
  carregando,
  erro,
  aoTentarNovamente,
}: {
  itens: ImportacaoResumo[]
  carregando: boolean
  erro: string | null
  aoTentarNovamente: () => void
}) {
  const [selecionada, definirSelecionada] = useState<string | null>(null)

  const colunas: Coluna<ImportacaoResumo>[] = [
    {
      cabecalho: 'Enviada em',
      largura: '170px',
      celula: (i) => <span className="tabular">{formatarInstante(i.enviadaEm)}</span>,
    },
    {
      cabecalho: 'Arquivo',
      celula: (i) => (
        <span className="flex min-w-0 items-center gap-2">
          <FileSpreadsheet className="size-3.5 shrink-0 text-muted-foreground" aria-hidden />
          <span className="truncate font-medium text-foreground">{i.nomeOriginalArquivo}</span>
        </span>
      ),
    },
    {
      cabecalho: 'Formato',
      largura: '90px',
      celula: (i) => <span className="text-muted-foreground">{ROTULO_FORMATO[i.formato]}</span>,
    },
    {
      cabecalho: 'Linhas',
      largura: '90px',
      numerica: true,
      celula: (i) => <span className="tabular">{i.totalLinhas}</span>,
    },
    {
      cabecalho: 'Com erro',
      largura: '90px',
      numerica: true,
      celula: (i) => (
        <span className={cn('tabular', i.linhasComErro > 0 && 'text-critico')}>
          {i.linhasComErro}
        </span>
      ),
    },
    {
      cabecalho: 'Situação',
      largura: '120px',
      celula: (i) => <StatusBadge tom={TOM_STATUS[i.status]}>{ROTULO_STATUS[i.status]}</StatusBadge>,
    },
  ]

  return (
    <>
      <DataTable
        rotulo="Histórico de importações"
        colunas={colunas}
        itens={itens}
        chave={(i) => i.id}
        carregando={carregando}
        erro={erro}
        aoTentarNovamente={aoTentarNovamente}
        aoClicarLinha={(i) => definirSelecionada(i.id)}
        vazio={{
          titulo: 'Nenhuma importação ainda',
          descricao: 'Envie um arquivo para ver aqui o registro de quem importou, quando e o quê.',
        }}
        rodape={
          <span>
            {itens.length} {itens.length === 1 ? 'importação' : 'importações'}
          </span>
        }
      />

      <Relatorio id={selecionada} aoFechar={() => definirSelecionada(null)} />
    </>
  )
}

/** O relatório linha a linha de uma importação já gravada. */
function Relatorio({ id, aoFechar }: { id: string | null; aoFechar: () => void }) {
  const [detalhe, definirDetalhe] = useState<ImportacaoDetalhe | null>(null)
  const [carregando, definirCarregando] = useState(false)
  const [erro, definirErro] = useState<string | null>(null)

  const carregar = useCallback(async (alvo: string, ativo: () => boolean) => {
    definirCarregando(true)
    definirErro(null)

    try {
      const resposta = await obterImportacao(alvo)

      if (ativo()) definirDetalhe(resposta)
    } catch (falha) {
      if (ativo()) {
        definirErro(falha instanceof Error ? falha.message : 'Não foi possível abrir o relatório.')
      }
    } finally {
      if (ativo()) definirCarregando(false)
    }
  }, [])

  useEffect(() => {
    if (!id) {
      return
    }

    let vivo = true

    // O estado só muda DEPOIS do await, quando a resposta da API chega.
    // oxlint-disable-next-line react/set-state-in-effect
    void carregar(id, () => vivo)

    return () => {
      vivo = false
    }
  }, [id, carregar])

  const comErro = detalhe?.linhas.filter((l) => l.situacao === 'ComErro') ?? []

  return (
    <Drawer open={id !== null} onOpenChange={(aberto) => !aberto && aoFechar()}>
      <DrawerContent titulo="Relatório da importação" className="max-w-2xl">
        <div aria-live="polite" aria-busy={carregando} className="space-y-4">
          {carregando && <div className="h-24 animate-pulse rounded bg-muted" />}

          {!carregando && erro && (
            <Alert variant="destructive" role="alert">
              <AlertDescription>{erro}</AlertDescription>
            </Alert>
          )}

          {!carregando && !erro && detalhe && (
            <>
              <ListaCampos colunas={2}>
                <Campo rotulo="Arquivo">{detalhe.nomeOriginalArquivo}</Campo>
                <Campo rotulo="Formato">{ROTULO_FORMATO[detalhe.formato]}</Campo>
                <Campo rotulo="Enviada em">{formatarInstante(detalhe.enviadaEm)}</Campo>
                <Campo rotulo="Situação">
                  <StatusBadge tom={TOM_STATUS[detalhe.status]}>
                    {ROTULO_STATUS[detalhe.status]}
                  </StatusBadge>
                </Campo>
                <Campo rotulo="Linhas">
                  <span className="tabular">
                    {detalhe.totalLinhas} — {detalhe.linhasValidas} válidas,{' '}
                    {detalhe.linhasComErro} com erro
                  </span>
                </Campo>
                <Campo rotulo="Tamanho">
                  <span className="tabular">{formatarBytes(detalhe.tamanhoBytes)}</span>
                </Campo>
              </ListaCampos>

              <div>
                <p className="text-xs text-muted-foreground">Identificação do arquivo (SHA-256)</p>
                <p className="tabular mt-0.5 break-all text-[11.5px] text-foreground">
                  {detalhe.hashSha256}
                </p>
                <p className="mt-1 text-[11.5px] text-muted-foreground">
                  O conteúdo do arquivo não é guardado. Este resumo identifica exatamente qual
                  arquivo originou a importação, sem reter os dados dele.
                </p>
              </div>

              {comErro.length === 0 ? (
                <p className="border-t border-border pt-4 text-[13px] text-muted-foreground">
                  Nenhuma linha com erro.
                </p>
              ) : (
                <div className="border-t border-border pt-4">
                  <p className="mb-2 text-[13px] font-medium text-foreground">
                    Linhas com erro ({comErro.length})
                  </p>
                  <ul className="space-y-2">
                    {comErro.map((l) => (
                      <li key={l.numeroNoArquivo} className="text-[12.5px]">
                        <span className="tabular font-medium text-foreground">
                          Linha {l.numeroNoArquivo}
                        </span>
                        <ul className="mt-0.5 space-y-0.5 pl-4">
                          {l.erros.map((e) => (
                            <li key={e} className="text-critico">
                              {e}
                            </li>
                          ))}
                        </ul>
                      </li>
                    ))}
                  </ul>
                </div>
              )}
            </>
          )}
        </div>
      </DrawerContent>
    </Drawer>
  )
}

// ---------------------------------------------------------------- formatação

function formatarInstante(valor: string): string {
  const data = new Date(valor)

  return Number.isNaN(data.getTime()) ? valor : data.toLocaleString('pt-BR')
}

function formatarData(valor: string | null): string {
  if (!valor) return '—'

  const [ano, mes, dia] = valor.split('-')

  return dia ? `${dia}/${mes}/${ano}` : valor
}

function formatarBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`

  return `${(bytes / (1024 * 1024)).toFixed(2)} MB`
}

// ------------------------------------------- acompanhamento (Fase 9)

/**
 * Mostra em que pé está o trabalho que foi para a fila.
 *
 * A tela **não adivinha** o andamento: ela pergunta ao servidor de três em três
 * segundos e mostra o que ele responde. Barra de progresso falsa seria pior que
 * nenhuma — daria uma precisão que ninguém tem.
 */
function Acompanhamento({
  acompanhamento,
  aoConcluir,
  aoFechar,
}: {
  acompanhamento: ReturnType<typeof useTrabalho>
  aoConcluir: () => Promise<void>
  aoFechar: () => void
}) {
  const { trabalho, acompanhando, erro, desistiu } = acompanhamento

  useEffect(() => {
    // Concluiu: o histórico da tela precisa recarregar para mostrar a
    // importação que o worker acabou de gravar.
    if (trabalho && !trabalho.pendente && trabalho.status === 'Concluido') {
      void aoConcluir()
    }
  }, [trabalho, aoConcluir])

  if (!trabalho) {
    return (
      <Alert role="status">
        <AlertDescription>Enviando para a fila…</AlertDescription>
      </Alert>
    )
  }

  const falhou = trabalho.status === 'Falhou'

  return (
    <div className="space-y-2 rounded-md border border-border bg-muted/40 p-3">
      <div className="flex items-center justify-between gap-3">
        <span className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
          Processamento em segundo plano
        </span>
        <span className={cn('text-xs font-medium', falhou && 'text-destructive')}>
          {ROTULO_TRABALHO[trabalho.status]}
        </span>
      </div>

      <p className="text-[13px] text-muted-foreground">
        {acompanhando && 'Acompanhando… pode fechar esta tela, o processamento continua.'}
        {!acompanhando && trabalho.status === 'Concluido' && 'Pronto. O histórico abaixo já mostra o resultado.'}
        {falhou && (trabalho.erro ?? 'O processamento falhou.')}
        {desistiu && 'Passou do tempo esperado. Confira o histórico mais tarde.'}
      </p>

      {trabalho.tentativas > 1 && (
        <p className="text-xs text-muted-foreground">
          Tentativa {trabalho.tentativas} — a primeira não deu certo e a fila devolveu o trabalho.
        </p>
      )}

      {erro && <p className="text-xs text-amber-600 dark:text-amber-500">{erro}</p>}

      {!acompanhando && (
        <Button type="button" size="sm" variant="outline" onClick={aoFechar}>
          Fechar
        </Button>
      )}
    </div>
  )
}
