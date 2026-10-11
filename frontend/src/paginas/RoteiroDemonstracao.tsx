import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { listarInconsistencias, type Inconsistencia } from '@/api/workflow'

/**
 * Roteiro de três a cinco minutos para quem entra pela conta pública.
 *
 * Quem chega pela demonstração não conhece o produto, e a primeira tela antes
 * era a lista de funcionários - nada dizia por onde começar. Este cartão diz o
 * caminho que conta a história do Prisma RH: folha → holerite → bases →
 * memória de cálculo → inconsistência → auditoria.
 *
 * Os links vêm da API, e não de id fixo no código: o roteiro procura o achado
 * `LiquidoNegativo` e aponta para a folha onde ele foi detectado. Se a base
 * mudar, o roteiro acompanha; se o achado não existir, ele cai para as listas.
 *
 * Só aparece para o Visualizador - a conta pública, somente leitura. Quem
 * opera a folha não precisa de tutorial no painel.
 */
export function RoteiroDemonstracao() {
  const [achado, definirAchado] = useState<Inconsistencia | null>(null)

  useEffect(() => {
    let vivo = true

    listarInconsistencias()
      .then((pagina) => {
        if (!vivo) return
        definirAchado(pagina.itens.find((i) => i.codigo === 'LiquidoNegativo') ?? null)
      })
      // Sem o achado o roteiro continua útil, apontando para as listas. Falhar
      // aqui não pode derrubar o painel, que tem os números que importam.
      .catch(() => {})

    return () => {
      vivo = false
    }
  }, [])

  const folha = achado ? `/folhas/${achado.idFolha}` : '/folhas'

  return (
    <section
      aria-labelledby="roteiro-titulo"
      className="mb-8 rounded-lg border border-border-forte bg-card px-5 py-4"
    >
      <h2 id="roteiro-titulo" className="text-[15px] font-semibold tracking-tight">
        Roteiro da demonstração
      </h2>
      <p className="mt-1 text-[13px] text-muted-foreground">
        Conta pública, somente leitura, com dados 100% fictícios.
        {achado && (
          <>
            {' '}A folha do roteiro é a de <span className="tabular">{achado.competencia}</span>.
          </>
        )}
      </p>

      <ol className="mt-3 list-decimal space-y-1.5 pl-5 text-[13px]">
        <li>
          <Link to={folha} className="font-medium text-primary underline-offset-4 hover:underline">
            Abra a folha
          </Link>{' '}
          e veja os totais calculados pelo sistema.
        </li>
        <li>
          Abra um holerite pelo nome do funcionário — com o mouse ou com <kbd>Tab</kbd> e{' '}
          <kbd>Enter</kbd>. As <strong>bases de cálculo</strong> mostram de quais rubricas saiu
          cada base de INSS, FGTS e IRRF.
        </li>
        <li>
          Clique numa rubrica sublinhada para ver a <strong>memória de cálculo</strong>: cada conta,
          com os parâmetros vigentes na competência.
        </li>
        <li>
          Em{' '}
          <Link
            to="/inconsistencias"
            className="font-medium text-primary underline-offset-4 hover:underline"
          >
            Inconsistências
          </Link>
          , veja o que a conferência automática encontrou e o tratamento de cada achado.
        </li>
        <li>
          Em{' '}
          <Link
            to="/auditoria"
            className="font-medium text-primary underline-offset-4 hover:underline"
          >
            Auditoria
          </Link>
          , a trilha de quem fez o quê — sem nenhum registro editável.
        </li>
      </ol>

      <p className="mt-3 border-t border-border pt-3 text-[12.5px] text-muted-foreground">
        <strong className="text-foreground">Sobre o líquido negativo:</strong> num dos holerites da
        demonstração os descontos superam os proventos, e o líquido fica abaixo de zero. Isso está
        lá de propósito — é o tipo de problema que a conferência existe para pegar antes do
        fechamento. O cálculo não foi “ajustado” para escondê-lo: o sistema mostra o número e
        aponta o achado.
      </p>
    </section>
  )
}
