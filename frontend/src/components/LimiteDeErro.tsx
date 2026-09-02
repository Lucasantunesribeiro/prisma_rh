import { Component, type ReactNode } from 'react'
import { Button } from '@/components/ui/button'

/**
 * Contém um erro de renderização a UMA página, em vez de derrubar o app inteiro.
 *
 * ## O defeito que isto corrige
 *
 * ⚠️ Descoberto em 02/09/2026: a tela `/status` chamava `.find` num campo que a
 * produção omite por segurança, lançava em render, e — sem nenhum error
 * boundary no projeto — **desmontava a árvore React inteira**. Um recrutador
 * clicando em "Status do sistema" via a aplicação virar tela branca.
 *
 * Um error boundary transforma isso num aviso contido: a página quebrada mostra
 * um fallback, o resto do app (menu, sessão, navegação) continua vivo.
 *
 * ## O que ele NÃO pega
 *
 * Erro de evento, de código assíncrono e de `fetch` não passam por aqui — só
 * erro lançado durante o **render** dos filhos. Falha de rede continua tratada
 * no `catch` de quem chama a API. Isto é a rede de segurança de último recurso,
 * não o tratamento de erro do dia a dia.
 *
 * A `key` remontada pela rota (ver uso em `App.tsx`) reseta o boundary a cada
 * navegação, para um erro numa página não travar as outras.
 */
interface Estado {
  erro: Error | null
}

export class LimiteDeErro extends Component<{ children: ReactNode }, Estado> {
  state: Estado = { erro: null }

  static getDerivedStateFromError(erro: Error): Estado {
    return { erro }
  }

  private tentarDeNovo = () => {
    this.setState({ erro: null })
  }

  render() {
    if (!this.state.erro) {
      return this.props.children
    }

    return (
      <main
        role="alert"
        className="flex min-h-[60vh] flex-col items-center justify-center gap-3 px-6 text-center"
      >
        <h1 className="text-lg font-semibold text-foreground">Algo deu errado nesta página</h1>
        <p className="max-w-md text-[13px] text-muted-foreground">
          Um erro inesperado interrompeu esta tela. O restante do sistema continua funcionando —
          use o menu para navegar, ou tente carregar esta página de novo.
        </p>
        <Button variant="outline" size="sm" onClick={this.tentarDeNovo}>
          Tentar novamente
        </Button>
      </main>
    )
  }
}
