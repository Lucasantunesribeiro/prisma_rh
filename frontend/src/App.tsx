import { lazy } from 'react'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router'
import { ProvedorSessao } from '@/auth/SessaoContexto'
import { RotaProtegida } from '@/rotas/RotaProtegida'
import { ApplicationShell } from '@/layout/ApplicationShell'
import Entrar from '@/paginas/Entrar'

/*
 * Cada pagina vira um pedaco proprio do build, baixado na primeira visita a
 * rota (Vite + import() dinamico). Antes, o arquivo inicial levava as quinze
 * telas - inclusive importacao, regras e auditoria, que a maior parte das
 * visitas nunca abre.
 *
 * `Entrar` continua no arquivo inicial: e a primeira tela de quem chega, e
 * esperar um segundo download para ver o login seria pior do que antes. O
 * carregamento e a falha de download sao tratados no ApplicationShell
 * (Suspense) e no LimiteDeErro.
 */
const Empresas = lazy(() => import('@/paginas/Empresas'))
const Funcionarios = lazy(() => import('@/paginas/Funcionarios'))
const FuncionarioDetalhe = lazy(() => import('@/paginas/FuncionarioDetalhe'))
const Cargos = lazy(() => import('@/paginas/Cargos'))
const Importacoes = lazy(() => import('@/paginas/Importacoes'))
const Rubricas = lazy(() => import('@/paginas/Rubricas'))
const RegrasAnalise = lazy(() => import('@/paginas/RegrasAnalise'))
const Folhas = lazy(() => import('@/paginas/Folhas'))
const FolhaDetalhe = lazy(() => import('@/paginas/FolhaDetalhe'))
const Parametros = lazy(() => import('@/paginas/Parametros'))
const Status = lazy(() => import('@/paginas/Status'))
const Painel = lazy(() => import('@/paginas/Painel'))
const Inconsistencias = lazy(() => import('@/paginas/Inconsistencias'))
const Auditoria = lazy(() => import('@/paginas/Auditoria'))

export default function App() {
  return (
    <BrowserRouter>
      <ProvedorSessao>
        <Routes>
          <Route path="/entrar" element={<Entrar />} />

          <Route element={<RotaProtegida />}>
            <Route element={<ApplicationShell />}>
              <Route path="/empresas" element={<Empresas />} />
              <Route path="/funcionarios" element={<Funcionarios />} />
              <Route path="/funcionarios/:id" element={<FuncionarioDetalhe />} />
              <Route path="/cargos" element={<Cargos />} />
              <Route path="/importacoes" element={<Importacoes />} />
              <Route path="/rubricas" element={<Rubricas />} />
              <Route path="/regras-analise" element={<RegrasAnalise />} />
              <Route path="/painel" element={<Painel />} />
              <Route path="/inconsistencias" element={<Inconsistencias />} />
              <Route path="/auditoria" element={<Auditoria />} />
              <Route path="/folhas" element={<Folhas />} />
              <Route path="/folhas/:id" element={<FolhaDetalhe />} />
              <Route path="/parametros" element={<Parametros />} />
              <Route path="/status" element={<Status />} />
            </Route>
          </Route>

          <Route path="*" element={<Navigate to="/painel" replace />} />
        </Routes>
      </ProvedorSessao>
    </BrowserRouter>
  )
}
