import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import Entrar from './Entrar'
import { ProvedorSessao } from '@/auth/SessaoContexto'

function responder(corpo: unknown, status = 200): Response {
  return new Response(JSON.stringify(corpo), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

const SEM_SESSAO = () => responder({ title: 'Sessao invalida' }, 401)

function renderizar() {
  return render(
    <MemoryRouter>
      <ProvedorSessao>
        <Entrar />
      </ProvedorSessao>
    </MemoryRouter>,
  )
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('Entrar', () => {
  it('mostra o formulario quando nao ha sessao para restaurar', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation(SEM_SESSAO))

    renderizar()

    expect(await screen.findByLabelText('E-mail')).toBeInTheDocument()
    expect(screen.getByLabelText('Senha')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Entrar' })).toBeInTheDocument()
  })

  it('mostra a mensagem do backend quando a credencial e invalida', async () => {
    const fetchSimulado = vi.fn().mockImplementation((url: string) => {
      if (String(url).includes('/entrar')) {
        return Promise.resolve(responder({ detail: 'E-mail ou senha incorretos.' }, 401))
      }
      return Promise.resolve(SEM_SESSAO())
    })

    vi.stubGlobal('fetch', fetchSimulado)

    renderizar()

    await userEvent.type(await screen.findByLabelText('E-mail'), 'lucas@x.com')
    await userEvent.type(screen.getByLabelText('Senha'), 'errada')
    await userEvent.click(screen.getByRole('button', { name: 'Entrar' }))

    const alerta = await screen.findByRole('alert')
    expect(alerta).toHaveTextContent('E-mail ou senha incorretos.')
  })

  it('nao guarda o access token em localStorage', async () => {
    const fetchSimulado = vi.fn().mockImplementation((url: string) => {
      if (String(url).includes('/entrar')) {
        return Promise.resolve(
          responder({
            accessToken: 'token-secreto-de-teste',
            expiraEm: new Date().toISOString(),
            usuario: {
              id: '1',
              idOrganizacao: '2',
              nome: 'Lucas',
              email: 'lucas@x.com',
              perfil: 'AnalistaRh',
            },
          }),
        )
      }
      return Promise.resolve(SEM_SESSAO())
    })

    vi.stubGlobal('fetch', fetchSimulado)

    renderizar()

    await userEvent.type(await screen.findByLabelText('E-mail'), 'lucas@x.com')
    await userEvent.type(screen.getByLabelText('Senha'), 'certa')
    await userEvent.click(screen.getByRole('button', { name: 'Entrar' }))

    await waitFor(() => {
      expect(fetchSimulado).toHaveBeenCalledWith(
        expect.stringContaining('/api/autenticacao/entrar'),
        expect.objectContaining({ credentials: 'include' }),
      )
    })

    // O token vive so em memoria. Se ele encostar no localStorage, qualquer XSS
    // rouba a sessao inteira - e este teste existe para travar isso.
    expect(JSON.stringify(window.localStorage)).not.toContain('token-secreto-de-teste')
    expect(window.localStorage.length).toBe(0)
  })
})
