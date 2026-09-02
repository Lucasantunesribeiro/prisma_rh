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
