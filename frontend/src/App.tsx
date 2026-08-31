import { BrowserRouter, Navigate, Route, Routes } from 'react-router'
import { ProvedorSessao } from '@/auth/SessaoContexto'
import { RotaProtegida } from '@/rotas/RotaProtegida'
import { ApplicationShell } from '@/layout/ApplicationShell'
import Entrar from '@/paginas/Entrar'
import Empresas from '@/paginas/Empresas'
import Funcionarios from '@/paginas/Funcionarios'
import FuncionarioDetalhe from '@/paginas/FuncionarioDetalhe'
import Cargos from '@/paginas/Cargos'
import Importacoes from '@/paginas/Importacoes'
import Rubricas from '@/paginas/Rubricas'
import RegrasAnalise from '@/paginas/RegrasAnalise'
import Folhas from '@/paginas/Folhas'
import FolhaDetalhe from '@/paginas/FolhaDetalhe'
import Parametros from '@/paginas/Parametros'
import Status from '@/paginas/Status'
import Painel from '@/paginas/Painel'
import Inconsistencias from '@/paginas/Inconsistencias'
import Auditoria from '@/paginas/Auditoria'

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

          <Route path="*" element={<Navigate to="/funcionarios" replace />} />
        </Routes>
      </ProvedorSessao>
    </BrowserRouter>
  )
}
