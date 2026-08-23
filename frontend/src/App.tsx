import { BrowserRouter, Navigate, Route, Routes } from 'react-router'
import { ProvedorSessao } from '@/auth/SessaoContexto'
import { RotaProtegida } from '@/rotas/RotaProtegida'
import Layout from '@/components/Layout'
import Entrar from '@/paginas/Entrar'
import Empresas from '@/paginas/Empresas'
import Funcionarios from '@/paginas/Funcionarios'
import FuncionarioDetalhe from '@/paginas/FuncionarioDetalhe'
import Cargos from '@/paginas/Cargos'
import Rubricas from '@/paginas/Rubricas'
import Folhas from '@/paginas/Folhas'
import FolhaDetalhe from '@/paginas/FolhaDetalhe'
import Status from '@/paginas/Status'

export default function App() {
  return (
    <BrowserRouter>
      <ProvedorSessao>
        <Routes>
          <Route path="/entrar" element={<Entrar />} />

          <Route element={<RotaProtegida />}>
            <Route element={<Layout />}>
              <Route path="/empresas" element={<Empresas />} />
              <Route path="/funcionarios" element={<Funcionarios />} />
              <Route path="/funcionarios/:id" element={<FuncionarioDetalhe />} />
              <Route path="/cargos" element={<Cargos />} />
              <Route path="/rubricas" element={<Rubricas />} />
              <Route path="/folhas" element={<Folhas />} />
              <Route path="/folhas/:id" element={<FolhaDetalhe />} />
              <Route path="/status" element={<Status />} />
            </Route>
          </Route>

          <Route path="*" element={<Navigate to="/empresas" replace />} />
        </Routes>
      </ProvedorSessao>
    </BrowserRouter>
  )
}
