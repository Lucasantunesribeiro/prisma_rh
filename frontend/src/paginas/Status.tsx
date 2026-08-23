import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { LinhaStatus } from '@/components/LinhaStatus'
import { useStatusSistema } from '@/hooks/useStatusSistema'
import { VERIFICACAO_BANCO, type StatusSaude } from '@/api/saude'

export default function Status() {
  const { estado, recarregar } = useStatusSistema()

  return (
    <main className="mx-auto flex min-h-screen w-full max-w-xl flex-col justify-center px-6 py-12">
      <header className="mb-6">
        <h1 className="text-2xl font-semibold tracking-tight">Prisma RH</h1>
        <p className="mt-1 text-sm text-muted-foreground">{descreverSistema(estado.situacao, estado.situacao === 'sucesso' ? estado.saude.status : undefined)}</p>
      </header>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Status do sistema</CardTitle>
        </CardHeader>

        <CardContent aria-live="polite" aria-busy={estado.situacao === 'carregando'}>
          {estado.situacao === 'carregando' && (
            <p className="py-2 text-sm text-muted-foreground">Verificando...</p>
          )}

          {estado.situacao === 'sucesso' && (
            <div>
              <LinhaStatus rotulo="API" status="saudavel" />
              <LinhaStatus rotulo="Banco de dados" status={statusDoBanco(estado.saude.verificacoes)} />
            </div>
          )}

          {estado.situacao === 'erro' && (
            <div>
              <LinhaStatus rotulo="API" status="indisponivel" />
              <LinhaStatus rotulo="Banco de dados" status="indisponivel" />

              <p className="mt-4 text-sm text-muted-foreground">
                Não foi possível acessar a API. Verifique se o backend está em execução.
              </p>
              <p className="mt-1 text-xs text-muted-foreground/80">{estado.mensagem}</p>

              <Button className="mt-4" variant="outline" size="sm" onClick={recarregar}>
                Tentar novamente
              </Button>
            </div>
          )}
        </CardContent>
      </Card>
    </main>
  )
}

function statusDoBanco(verificacoes: { nome: string; status: StatusSaude }[]): StatusSaude {
  return verificacoes.find((verificacao) => verificacao.nome === VERIFICACAO_BANCO)?.status ?? 'indisponivel'
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
