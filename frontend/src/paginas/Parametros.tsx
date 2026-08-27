import { useCallback, useEffect, useState } from 'react'
import { listarTabelasInss, type FaixaInss, type TabelaInss } from '@/api/folha'
import { DataTable, type Coluna } from '@/components/sistema/DataTable'
import {
  CabecalhoPagina,
  CabecalhoSecao,
  Dinheiro,
  StatusBadge,
} from '@/components/sistema/Primitivos'
import { EstadoCarregando, EstadoErro, EstadoVazio } from '@/components/sistema/Estados'
import { usePagina } from '@/layout/usePagina'

const DATA = new Intl.DateTimeFormat('pt-BR', { day: '2-digit', month: '2-digit', year: 'numeric' })

const formatarData = (iso: string) => DATA.format(new Date(`${iso.slice(0, 10)}T12:00:00`))

/**
 * Os parâmetros legais que o cálculo usa.
 *
 * Existe porque a Fase 4B guarda faixas, alíquotas, teto, vigência e fonte no
 * banco, e nenhum deles aparecia na interface: o analista via o desconto e não
 * tinha como conferir de onde ele saiu. Aqui a tabela é mostrada como ela é —
 * com a portaria que a originou.
 *
 * Somente leitura. Cadastrar vigência é operação de Administrador da
 * Plataforma e hoje acontece pela API; oferecer o formulário a quem receberia
 * 403 seria propor uma ação inválida.
 */
export default function Parametros() {
  usePagina([{ texto: 'Folha' }, { texto: 'Parâmetros legais' }])

  const [tabelas, definirTabelas] = useState<TabelaInss[]>([])
  const [carregando, definirCarregando] = useState(true)
  const [erro, definirErro] = useState<string | null>(null)

  const carregar = useCallback(async () => {
    definirCarregando(true)
    definirErro(null)

    try {
      definirTabelas(await listarTabelasInss())
    } catch (falha) {
      definirErro(
        falha instanceof Error ? falha.message : 'Não foi possível carregar os parâmetros legais.',
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
        titulo="Parâmetros legais"
        descricao="Tabelas vigentes usadas pelo cálculo, com a fonte oficial de cada uma."
      />

      {carregando && <EstadoCarregando rotulo="Carregando parâmetros" />}

      {!carregando && erro && <EstadoErro mensagem={erro} aoTentarNovamente={() => void carregar()} />}

      {!carregando && !erro && tabelas.length === 0 && (
        <EstadoVazio
          titulo="Nenhuma tabela cadastrada"
          descricao="Sem tabela vigente, a folha calcula sem o desconto de INSS."
        />
      )}

      {!carregando && !erro && tabelas.length > 0 && (
        <section className="space-y-8">
          <div>
            <CabecalhoSecao
              titulo="INSS — contribuição do segurado"
              descricao="Alíquotas progressivas: cada faixa incide apenas sobre a parcela da base que lhe cabe."
            />

            <div className="space-y-6">
              {tabelas.map((tabela) => (
                <TabelaVigencia key={tabela.id} tabela={tabela} />
              ))}
            </div>
          </div>
        </section>
      )}
    </>
  )
}

function TabelaVigencia({ tabela }: { tabela: TabelaInss }) {
  const colunas: Coluna<FaixaInss>[] = [
    {
      cabecalho: 'Faixa',
      largura: '80px',
      celula: (f) => <span className="tabular text-muted-foreground">{f.ordem}</span>,
    },
    {
      cabecalho: 'De',
      numerica: true,
      celula: (f) => <Dinheiro valor={f.limiteInferior} />,
    },
    {
      cabecalho: 'Até',
      numerica: true,
      celula: (f) => <Dinheiro valor={f.limiteSuperior} />,
    },
    {
      cabecalho: 'Alíquota',
      numerica: true,
      largura: '120px',
      celula: (f) => (
        <span className="tabular font-medium">
          {f.aliquotaPercentual.toLocaleString('pt-BR', { maximumFractionDigits: 2 })}%
        </span>
      ),
    },
  ]

  return (
    <div>
      <div className="mb-2 flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
        <div className="flex items-center gap-2">
          <h3 className="text-[13px] font-medium">
            Vigente a partir de {formatarData(tabela.vigenciaInicio)}
          </h3>
          {tabela.vigente && <StatusBadge tom="sucesso">Em vigor</StatusBadge>}
        </div>

        <p className="text-[13px] text-muted-foreground">
          Teto do salário-de-contribuição{' '}
          <span className="tabular font-medium text-foreground">
            <Dinheiro valor={tabela.teto} />
          </span>
        </p>
      </div>

      <DataTable
        rotulo={`Faixas de INSS vigentes a partir de ${formatarData(tabela.vigenciaInicio)}`}
        colunas={colunas}
        itens={tabela.faixas}
        chave={(f) => `${tabela.id}-${f.ordem}`}
        rodape={
          /*
           * A fonte é exibida junto da tabela, e não escondida num "sobre":
           * o CLAUDE.md §29 exige registrar de onde veio a regra, e a pessoa
           * que confere o desconto é justamente quem precisa dela à mão.
           */
          <span className="text-xs">
            <span className="text-muted-foreground">Fonte: </span>
            {tabela.fonte}
          </span>
        }
      />
    </div>
  )
}
