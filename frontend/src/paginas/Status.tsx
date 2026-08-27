import { VERIFICACAO_BANCO, type StatusSaude } from '@/api/saude'
import { LinhaStatus } from '@/components/LinhaStatus'
import { CabecalhoPagina } from '@/components/sistema/Primitivos'
import { Button } from '@/components/ui/button'
import { useStatusSistema } from '@/hooks/useStatusSistema'
import { usePagina } from '@/layout/usePagina'

/**
 * Estado da API e do banco.
 *
 * Página de leitura, largura limitada: são duas linhas de informação, e
 * espalhá-las por 1440px só afastaria o rótulo do seu valor. A regra de "usar
 * toda a largura" vale para tabela, não para tudo.
 */
export default function Status() {
  const { estado, recarregar } = useStatusSistema()

  usePagina([{ texto: 'Administração' }, { texto: 'Status do sistema' }])

  return (
    <div className="max-w-2xl">
      <CabecalhoPagina
        titulo="Status do sistema"
        descricao={descreverSistema(
          estado.situacao,
          estado.situacao === 'sucesso' ? estado.saude.status : undefined,
        )}
      />

      <section
        className="rounded-lg border border-border bg-card"
        aria-live="polite"
        aria-busy={estado.situacao === 'carregando'}
      >
        {estado.situacao === 'carregando' && (
          <p className="px-4 py-6 text-[13px] text-muted-foreground">Verificando...</p>
        )}

        {estado.situacao === 'sucesso' && (
          <div className="px-4">
            <LinhaStatus rotulo="API" status="saudavel" />
            <LinhaStatus rotulo="Banco de dados" status={statusDoBanco(estado.saude.verificacoes)} />
          </div>
        )}

        {estado.situacao === 'erro' && (
          <div className="px-4 pb-5">
            <LinhaStatus rotulo="API" status="indisponivel" />
            <LinhaStatus rotulo="Banco de dados" status="indisponivel" />

            <p className="mt-4 text-[13px] text-muted-foreground">
              Não foi possível acessar a API. Verifique se o backend está em execução.
            </p>
            <p className="mt-1 text-xs text-muted-foreground/80">{estado.mensagem}</p>

            <Button className="mt-4" variant="outline" size="sm" onClick={recarregar}>
              Tentar novamente
            </Button>
          </div>
        )}
      </section>
    </div>
  )
}

function statusDoBanco(verificacoes: { nome: string; status: StatusSaude }[]): StatusSaude {
  return (
    verificacoes.find((verificacao) => verificacao.nome === VERIFICACAO_BANCO)?.status ??
    'indisponivel'
  )
}

function descreverSistema(situacao: string, statusGeral?: StatusSaude): string {
  if (situacao === 'carregando') {
    return 'Verificando o estado do sistema...'
  }

  if (situacao === 'erro' || statusGeral === 'indisponivel') {
    return 'Sistema com indisponibilidade'
  }

  if (statusGeral === 'degradado') {
    return 'Sistema operacional com restrições'
  }

  return 'Sistema operacional'
}
