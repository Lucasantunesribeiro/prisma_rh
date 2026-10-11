/**
 * A pagina e baixada sob demanda (`React.lazy` em `App.tsx`). Se o download do
 * pedaco falhar - rede caiu, ou um deploy novo trocou os nomes dos arquivos
 * enquanto a aba estava aberta -, o erro chega aqui com uma destas mensagens,
 * que variam por navegador.
 */
export function ehFalhaDeDownload(erro: Error): boolean {
  return /dynamically imported module|importing a module script failed|error loading dynamically imported|failed to fetch/i.test(
    erro.message,
  )
}
