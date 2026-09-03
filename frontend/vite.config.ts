import path from 'node:path'
import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      '@': path.resolve(import.meta.dirname, './src'),
    },
  },
  server: {
    port: 5173,
  },
  build: {
    // ⚠️ MELHORIA NAO BLOQUEANTE, registrada de proposito. O bundle unico fica
    // em ~543 kB crus / ~158 kB gzip — acima do aviso padrao de 500 kB CRUS do
    // Vite, mas 158 kB gzip de primeira carga e adequado para um app B2B.
    //
    // O split de rota (React.lazy + Suspense) seria o conserto real, mas mexe no
    // roteador e no error boundary de um repo que esta sendo CONGELADO — risco
    // sem ganho claro. O limite fica no padrao de propósito: mascarar o numero
    // subindo o teto seria silenciar a metrica, nao melhora-la. Fica visivel e
    // registrado ate haver motivo real para dividir.
    chunkSizeWarningLimit: 500,
  },
  test: {
    // ⚠️ FUSO FIXO. Sem isto, todo teste que renderiza data depende da maquina.
    //
    // Foi assim que a suite passou local (UTC-3) e falhou no GitHub Actions
    // (UTC): a mesma execucao de analise aparecia como 10:00 aqui e 13:00 la.
    //
    // Nao e um contorno: o `CLAUDE.md secao 23` declara o Brasil como fuso da
    // interface, entao rodar a suite em `America/Sao_Paulo` e testar o produto
    // no fuso em que ele existe. Deixar o fuso do runner decidir era o defeito.
    env: { TZ: 'America/Sao_Paulo' },

    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/testes/configuracao.ts'],
    include: ['src/**/*.teste.{ts,tsx}'],
    css: true,
  },
})
