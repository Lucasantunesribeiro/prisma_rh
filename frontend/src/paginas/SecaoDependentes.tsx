import { Plus, Trash2 } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import {
  atualizarDependente,
  criarDependente,
  formatarData,
  removerDependente,
  ROTULO_RELACAO,
  type DadosDependente,
  type Dependente,
  type RelacaoDependente,
} from '@/api/pessoas'
import { DataTable, type Coluna } from '@/components/sistema/DataTable'
import { CabecalhoSecao, StatusBadge } from '@/components/sistema/Primitivos'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Drawer, DrawerClose, DrawerContent, DrawerTrigger } from '@/components/ui/drawer'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

const RELACOES = Object.keys(ROTULO_RELACAO) as RelacaoDependente[]

/**
 * Quem depende do funcionário.
 *
 * O que a tela precisa deixar óbvio: **cadastrar um dependente não faz o
 * imposto cair sozinho.** Só abate IRRF quem tem período declarado, e é por
 * isso que a coluna "Abate IRRF" existe e o formulário pede a data em vez de
 * assumir "a partir de hoje".
 *
 * A regra legal de idade — 21 anos, 24 se estudante — NÃO está codificada aqui
 * nem no backend: ela exige fonte oficial registrada, que o projeto ainda não
 * tem. Quem declara é o analista, e a declaração fica auditável.
 */
export function SecaoDependentes({
  idFuncionario,
  dependentes,
  administra,
  aoMudar,
}: {
  idFuncionario: string
  dependentes: Dependente[]
  administra: boolean
  aoMudar: () => Promise<void>
}) {
  const [erro, definirErro] = useState<string | null>(null)

  const apagar = async (dependente: Dependente) => {
    definirErro(null)

    try {
      await removerDependente(idFuncionario, dependente.id)
      await aoMudar()
    } catch (falha) {
      definirErro(
        falha instanceof Error ? falha.message : 'Não foi possível remover o dependente.',
      )
    }
  }

  const colunas: Coluna<Dependente>[] = [
    { cabecalho: 'Nome', celula: (d) => d.nome },
    {
      cabecalho: 'Relação',
      largura: '160px',
      celula: (d) => <span className="text-muted-foreground">{ROTULO_RELACAO[d.relacao]}</span>,
    },
    {
      cabecalho: 'Nascimento',
      numerica: true,
      largura: '130px',
      celula: (d) => formatarData(d.dataNascimento),
    },
    {
      cabecalho: 'Abate IRRF',
      largura: '210px',
      celula: (d) =>
        d.dedutivelIrrf ? (
          <div className="flex flex-wrap items-center gap-2">
            <StatusBadge tom="sucesso">Sim</StatusBadge>
            <span className="text-xs text-muted-foreground">
              {formatarData(d.inicioDeducaoIrrf)}
              {d.fimDeducaoIrrf ? ` até ${formatarData(d.fimDeducaoIrrf)}` : ' em diante'}
            </span>
          </div>
        ) : (
          <StatusBadge tom="neutro">Não</StatusBadge>
        ),
    },
    ...(administra
      ? [
          {
            cabecalho: '',
            largura: '92px',
            celula: (d: Dependente) => (
              <div className="flex justify-end gap-1">
                <FormularioDependente
                  idFuncionario={idFuncionario}
                  dependente={d}
                  aoSalvar={aoMudar}
                />
                <Button
                  variant="ghost"
                  size="sm"
                  aria-label={`Remover ${d.nome}`}
                  className="size-7 p-0"
                  onClick={() => void apagar(d)}
                >
                  <Trash2 className="size-3.5" aria-hidden />
                </Button>
              </div>
            ),
          },
        ]
      : []),
  ]

  return (
    <section>
      <CabecalhoSecao
        titulo="Dependentes"
        descricao="Usados pelo IRRF. Cadastrar não basta: só abate imposto quem tem período declarado."
        acao={
          administra ? (
            <FormularioDependente idFuncionario={idFuncionario} aoSalvar={aoMudar} />
          ) : undefined
        }
      />

      {erro && (
        <Alert variant="destructive" role="alert" className="mb-3">
          <AlertDescription>{erro}</AlertDescription>
        </Alert>
      )}

      <DataTable
        rotulo="Dependentes do funcionário"
        colunas={colunas}
        itens={dependentes}
        chave={(d) => d.id}
        vazio={{
          titulo: 'Nenhum dependente',
          descricao: 'Sem dependentes, o IRRF desta pessoa não terá dedução por dependente.',
        }}
      />
    </section>
  )
}

function FormularioDependente({
  idFuncionario,
  dependente,
  aoSalvar,
}: {
  idFuncionario: string
  dependente?: Dependente
  aoSalvar: () => Promise<void>
}) {
  const editando = dependente !== undefined

  const [aberto, definirAberto] = useState(false)
  const [nome, definirNome] = useState(dependente?.nome ?? '')
  const [nascimento, definirNascimento] = useState(dependente?.dataNascimento.slice(0, 10) ?? '')
  const [relacao, definirRelacao] = useState<RelacaoDependente>(dependente?.relacao ?? 'Filho')
  const [abate, definirAbate] = useState(dependente?.dedutivelIrrf ?? false)
  const [inicio, definirInicio] = useState(dependente?.inicioDeducaoIrrf?.slice(0, 10) ?? '')
  const [fim, definirFim] = useState(dependente?.fimDeducaoIrrf?.slice(0, 10) ?? '')
  const [erro, definirErro] = useState<string | null>(null)
  const [enviando, definirEnviando] = useState(false)

  const aoEnviar = async (evento: FormEvent) => {
    evento.preventDefault()
    definirErro(null)
    definirEnviando(true)

    // Sem "abate", o período inteiro vai nulo. Mandar uma data com o campo
    // desmarcado gravaria uma dedução que o usuário não pediu.
    const dados: DadosDependente = {
      nome,
      dataNascimento: nascimento,
      relacao,
      inicioDeducaoIrrf: abate ? inicio : null,
      fimDeducaoIrrf: abate && fim !== '' ? fim : null,
    }

    try {
      if (dependente) {
        await atualizarDependente(idFuncionario, dependente.id, dados)
      } else {
        await criarDependente(idFuncionario, dados)
        definirNome('')
        definirNascimento('')
        definirAbate(false)
        definirInicio('')
        definirFim('')
      }

      definirAberto(false)
      await aoSalvar()
    } catch (falha) {
      definirErro(
        falha instanceof Error ? falha.message : 'Não foi possível salvar o dependente.',
      )
    } finally {
      definirEnviando(false)
    }
  }

  const prefixo = editando ? `editar-${dependente.id}` : 'novo'

  return (
    <Drawer open={aberto} onOpenChange={definirAberto}>
      <DrawerTrigger asChild>
        {editando ? (
          <Button
            variant="ghost"
            size="sm"
            aria-label={`Editar ${dependente.nome}`}
            className="size-7 p-0"
          >
            <span aria-hidden className="text-xs">
              ✎
            </span>
          </Button>
        ) : (
          <Button variant="outline" size="sm">
            <Plus aria-hidden />
            Novo dependente
          </Button>
        )}
      </DrawerTrigger>

      <DrawerContent
        titulo={editando ? 'Editar dependente' : 'Novo dependente'}
        descricao="Guardamos só o que o cálculo exige: são dados pessoais de alguém que não usa o sistema."
        className="max-w-lg"
      >
        <form onSubmit={aoEnviar} className="space-y-4" noValidate>
          <div className="space-y-1.5">
            <Label htmlFor={`${prefixo}-nome`}>Nome</Label>
            <Input
              id={`${prefixo}-nome`}
              required
              autoFocus
              maxLength={200}
              value={nome}
              onChange={(e) => definirNome(e.target.value)}
            />
          </div>

          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor={`${prefixo}-nascimento`}>Nascimento</Label>
              <Input
                id={`${prefixo}-nascimento`}
                type="date"
                required
                value={nascimento}
                onChange={(e) => definirNascimento(e.target.value)}
              />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor={`${prefixo}-relacao`}>Relação</Label>
              <select
                id={`${prefixo}-relacao`}
                value={relacao}
                onChange={(e) => definirRelacao(e.target.value as RelacaoDependente)}
                className="h-9 w-full rounded-md border border-input bg-card px-3 text-[13px] shadow-xs"
              >
                {RELACOES.map((r) => (
                  <option key={r} value={r}>
                    {ROTULO_RELACAO[r]}
                  </option>
                ))}
              </select>
            </div>
          </div>

          <div className="rounded-md border border-border bg-muted/30 p-3">
            <label className="flex items-center gap-2 text-[13px] font-medium">
              <input
                type="checkbox"
                checked={abate}
                onChange={(e) => definirAbate(e.target.checked)}
                className="size-4 accent-[var(--primary)]"
              />
              Abate IRRF
            </label>

            <p className="mt-1 text-xs text-muted-foreground">
              Marque só quando este dependente puder ser deduzido. O período é declarado por quem
              cadastra — o sistema não decide isso pela idade.
            </p>

            {abate && (
              <div className="mt-3 grid gap-4 sm:grid-cols-2">
                <div className="space-y-1.5">
                  <Label htmlFor={`${prefixo}-inicio`}>A partir de</Label>
                  <Input
                    id={`${prefixo}-inicio`}
                    type="date"
                    required
                    value={inicio}
                    onChange={(e) => definirInicio(e.target.value)}
                  />
                </div>

                <div className="space-y-1.5">
                  <Label htmlFor={`${prefixo}-fim`}>Até (opcional)</Label>
                  <Input
                    id={`${prefixo}-fim`}
                    type="date"
                    value={fim}
                    onChange={(e) => definirFim(e.target.value)}
                  />
                </div>
              </div>
            )}
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
              {enviando ? 'Salvando...' : editando ? 'Salvar' : 'Cadastrar'}
            </Button>
          </div>
        </form>
      </DrawerContent>
    </Drawer>
  )
}
