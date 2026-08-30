import { useCallback, useEffect, useId, useState } from 'react'
import {
  ROTULO_CATEGORIA,
  ROTULO_SEVERIDADE,
  SEVERIDADES,
  TOM_SEVERIDADE,
  configurarRegra,
  listarRegras,
  podeConfigurarRegras,
  type Regra,
  type Severidade,
} from '@/api/analises'
import { useSessao } from '@/auth/useSessao'
import { EstadoCarregando, EstadoErro } from '@/components/sistema/Estados'
import { CabecalhoPagina, StatusBadge } from '@/components/sistema/Primitivos'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { usePagina } from '@/layout/usePagina'
import { cn } from '@/lib/utils'

/**
 * Configuração das regras de conferência (Fase 6).
 *
 * ## O que esta tela permite, e o que ela não permite
 *
 * A pessoa liga e desliga regra, muda a severidade e ajusta números **dentro da
 * faixa que o servidor declarou**. Ela não escreve regra, não escreve SQL e não
 * escreve expressão — as seis regras são código do sistema, e o catálogo vem
 * pronto da API.
 *
 * Os campos numéricos usam `min`/`max` vindos do servidor. Isso é conforto: quem
 * decide é o backend, e há teste de integração provando que um valor fora da
 * faixa volta 400 mesmo que o navegador tenha deixado digitar.
 */
export default function RegrasAnalise() {
  usePagina([{ texto: 'Folha' }, { texto: 'Regras de conferência' }])

  const { usuario } = useSessao()
  const configura = podeConfigurarRegras(usuario?.perfil)

  const [regras, definirRegras] = useState<Regra[]>([])
  const [carregando, definirCarregando] = useState(true)
  const [erro, definirErro] = useState<string | null>(null)

  const carregar = useCallback(async () => {
    definirCarregando(true)
    definirErro(null)

    try {
      definirRegras(await listarRegras())
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Não foi possível carregar as regras.')
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
        titulo="Regras de conferência"
        descricao="Regras oficiais do Prisma RH. Você configura os números; a regra é do sistema."
      />

      <div aria-live="polite" aria-busy={carregando}>
        {carregando && <EstadoCarregando />}

        {!carregando && erro && (
          <EstadoErro mensagem={erro} aoTentarNovamente={() => void carregar()} />
        )}

        {!carregando && !erro && (
          <div className="space-y-3">
            {!configura && (
              <p className="text-[13px] text-muted-foreground">
                Somente a administração da empresa altera regras. Você está vendo como elas estão
                configuradas.
              </p>
            )}

            {regras.map((regra) => (
              <CartaoRegra
                key={regra.codigo}
                regra={regra}
                editavel={configura}
                aoSalvar={(atualizada) =>
                  definirRegras((atuais) =>
                    atuais.map((r) => (r.codigo === atualizada.codigo ? atualizada : r)),
                  )
                }
              />
            ))}
          </div>
        )}
      </div>
    </>
  )
}

function CartaoRegra({
  regra,
  editavel,
  aoSalvar,
}: {
  regra: Regra
  editavel: boolean
  aoSalvar: (regra: Regra) => void
}) {
  const idSeveridade = useId()

  const [ativa, definirAtiva] = useState(regra.ativa)
  const [severidade, definirSeveridade] = useState<Severidade>(regra.severidade)
  const [valores, definirValores] = useState<Record<string, string>>(() =>
    Object.fromEntries(regra.parametros.map((p) => [p.chave, p.valor])),
  )

  const [salvando, definirSalvando] = useState(false)
  const [erro, definirErro] = useState<string | null>(null)
  const [salvo, definirSalvo] = useState(false)

  const mudou =
    ativa !== regra.ativa ||
    severidade !== regra.severidade ||
    regra.parametros.some((p) => valores[p.chave] !== p.valor)

  const salvar = async () => {
    definirSalvando(true)
    definirErro(null)
    definirSalvo(false)

    try {
      const atualizada = await configurarRegra(regra.codigo, { ativa, severidade, parametros: valores })

      aoSalvar(atualizada)
      definirSalvo(true)
    } catch (falha) {
      definirErro(falha instanceof Error ? falha.message : 'Não foi possível salvar.')
    } finally {
      definirSalvando(false)
    }
  }

  return (
    <section
      className={cn(
        'rounded-lg border border-border-forte bg-card px-4 py-3.5',
        'shadow-[0_1px_2px_rgba(17,26,46,0.04)]',
        !ativa && 'opacity-70',
      )}
    >
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <h2 className="text-[14px] font-semibold text-foreground">{regra.nome}</h2>
            <StatusBadge tom={TOM_SEVERIDADE[severidade]}>
              {ROTULO_SEVERIDADE[severidade]}
            </StatusBadge>
            <span className="text-[11px] text-muted-foreground">
              {ROTULO_CATEGORIA[regra.categoria]} · v{regra.versao}
            </span>
            {!regra.configurada && (
              <span className="text-[11px] text-muted-foreground">no padrão</span>
            )}
          </div>

          <p className="mt-1 max-w-3xl text-[13px] text-muted-foreground">{regra.explicacao}</p>
        </div>

        <label className="flex shrink-0 items-center gap-2 text-[13px]">
          <input
            type="checkbox"
            checked={ativa}
            disabled={!editavel}
            onChange={(e) => definirAtiva(e.target.checked)}
            className="size-4 rounded border-input accent-primary"
          />
          Ativa
        </label>
      </div>

      {editavel && (
        <div className="mt-3 flex flex-wrap items-end gap-3 border-t border-border pt-3">
          <div className="space-y-1">
            <Label htmlFor={idSeveridade} className="text-xs text-muted-foreground">
              Severidade
            </Label>
            <select
              id={idSeveridade}
              value={severidade}
              onChange={(e) => definirSeveridade(e.target.value as Severidade)}
              className={cn(
                'h-8 rounded-md border border-input bg-card px-2 text-[13px] text-foreground shadow-xs',
                'focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2',
              )}
            >
              {SEVERIDADES.map((s) => (
                <option key={s} value={s}>
                  {ROTULO_SEVERIDADE[s]}
                </option>
              ))}
            </select>
          </div>

          {regra.parametros.map((p) => (
            <CampoParametro
              key={p.chave}
              rotulo={p.rotulo}
              explicacao={p.explicacao}
              minimo={p.minimo}
              maximo={p.maximo}
              valor={valores[p.chave] ?? p.padrao}
              aoMudar={(valor) => definirValores((atuais) => ({ ...atuais, [p.chave]: valor }))}
            />
          ))}

          <div className="ml-auto flex items-center gap-2">
            {salvo && !mudou && (
              <span role="status" className="text-[12px] text-sucesso">
                Salvo
              </span>
            )}
            <Button
              type="button"
              size="sm"
              disabled={!mudou || salvando}
              onClick={() => void salvar()}
            >
              {salvando ? 'Salvando...' : 'Salvar'}
            </Button>
          </div>
        </div>
      )}

      {erro && (
        <Alert variant="destructive" role="alert" className="mt-3">
          <AlertDescription>{erro}</AlertDescription>
        </Alert>
      )}
    </section>
  )
}

function CampoParametro({
  rotulo,
  explicacao,
  minimo,
  maximo,
  valor,
  aoMudar,
}: {
  rotulo: string
  explicacao: string
  minimo: string
  maximo: string
  valor: string
  aoMudar: (valor: string) => void
}) {
  const id = useId()

  return (
    <div className="space-y-1">
      <Label htmlFor={id} className="text-xs text-muted-foreground">
        {rotulo}
      </Label>
      <Input
        id={id}
        type="number"
        inputMode="decimal"
        min={minimo}
        max={maximo}
        step="any"
        value={valor}
        title={explicacao}
        onChange={(e) => aoMudar(e.target.value)}
        className="tabular h-8 w-32 text-[13px]"
      />
      <p className="text-[11px] text-muted-foreground">
        de {minimo} a {maximo}
      </p>
    </div>
  )
}
