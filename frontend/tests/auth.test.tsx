import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { AxiosError } from 'axios'
import type { AxiosAdapter, AxiosResponse, InternalAxiosRequestConfig } from 'axios'
import App from '../src/App'
import { api } from '../src/api/client'
import { clearSession, confirmSession, getAccessToken, getSessionSnapshot, openSession } from '../src/auth/session'
import type { CurrentUser, Tenant } from '../src/api/types'

// Fixtures exist only in tests. No functional page imports test data or test tokens.
const originalAdapter = api.defaults.adapter
const user: CurrentUser = { userId: crypto.randomUUID(), email: 'member@example.test' }
const tenants: Tenant[] = [
  { tenantId: crypto.randomUUID(), name: 'First workspace', slug: 'first', role: 'Member', createdAtUtc: '2026-01-01T00:00:00Z' },
  { tenantId: crypto.randomUUID(), name: 'Second workspace', slug: 'second', role: 'Admin', createdAtUtc: '2026-02-01T00:00:00Z' },
]
const emptySummary = { totalProjects: 0, activeProjects: 0, completedProjects: 0, totalTasks: 0, todoTasks: 0, inProgressTasks: 0, completedTasks: 0, overdueTasks: 0, totalMembers: 2, taskCompletionPercentage: 0 }

function response(config: InternalAxiosRequestConfig, data: unknown, status = 200): AxiosResponse {
  return { data, status, statusText: status === 200 ? 'OK' : 'Unauthorized', headers: {}, config }
}
function rejectUnauthorized(config: InternalAxiosRequestConfig): never {
  throw new AxiosError('Unauthorized', 'ERR_BAD_REQUEST', config, undefined, response(config, {}, 401))
}
function authenticate() {
  const token = crypto.randomUUID()
  confirmSession(user, openSession(token, 3600))
  return token
}
function mount(path = '/dashboard') {
  return render(<MemoryRouter initialEntries={[path]}><App /></MemoryRouter>)
}
beforeEach(() => { clearSession(); localStorage.clear(); sessionStorage.clear() })
afterEach(() => { cleanup(); clearSession(); api.defaults.adapter = originalAdapter; vi.useRealTimers() })

describe('frontend authentication and tenant foundation', () => {
  it('validates required, email and password-length fields before calling the API', async () => {
    const adapter = vi.fn<AxiosAdapter>()
    api.defaults.adapter = adapter
    mount('/login')
    const actor = userEvent.setup()
    const email = screen.getByLabelText('Email address') as HTMLInputElement
    const password = screen.getByLabelText('Password') as HTMLInputElement
    const submit = screen.getByRole('button', { name: 'Sign in to your workspace' })
    await actor.click(submit)
    expect(email.validity.valueMissing).toBe(true)
    expect(password.validity.valueMissing).toBe(true)
    await actor.type(email, 'invalid-email')
    await actor.type(password, crypto.randomUUID())
    await actor.click(submit)
    expect(email.validity.typeMismatch).toBe(true)
    expect(adapter).not.toHaveBeenCalled()
    await actor.clear(email)
    await actor.type(email, 'validation@example.test')
    await actor.clear(password)
    await actor.type(password, crypto.randomUUID().slice(0, 5))
    expect(password.minLength).toBe(12)
    // JSDOM does not enforce minlength during checkValidity as browsers do;
    // test the form constraint and its actual value without inventing validation.
    expect(password.value.length).toBeLessThan(password.minLength)
  })

  it('redirects every protected route to login without making API requests', () => {
    const adapter = vi.fn<AxiosAdapter>()
    api.defaults.adapter = adapter
    for (const route of ['/dashboard', '/projects', '/tasks', '/team', '/settings']) {
      const view = mount(route)
      expect(screen.getByRole('heading', { name: 'Good to have you back.' })).toBeTruthy()
      view.unmount()
    }
    expect(adapter).not.toHaveBeenCalled()
  })

  it('logs in, verifies /auth/me, attaches the token and preserves the requested route', async () => {
    const token = crypto.randomUUID()
    const calls: string[] = []
    api.defaults.adapter = async (config) => {
      calls.push(config.url ?? '')
      if (config.url === '/auth/login') {
        expect(config.headers.has('Authorization')).toBe(false)
        return response(config, { accessToken: token, tokenType: 'Bearer', expiresIn: 3600 })
      }
      expect(config.headers.get('Authorization')).toBe(`Bearer ${token}`)
      if (config.url === '/auth/me') return response(config, user)
      if (config.url === '/tenants/mine') return response(config, tenants)
      if (config.url === `/tenants/${tenants[0].tenantId}/projects`) return response(config, [])
      throw new Error('Unexpected endpoint')
    }
    mount('/tasks')
    const actor = userEvent.setup()
    await actor.type(screen.getByLabelText('Email address'), user.email)
    await actor.type(screen.getByLabelText('Password'), crypto.randomUUID())
    await actor.click(screen.getByRole('button', { name: 'Sign in to your workspace' }))
    await screen.findByRole('heading', { name: 'Your task board starts with a project' }, { timeout: 5000 })
    expect(calls).toEqual(['/auth/login', '/auth/me', '/tenants/mine', `/tenants/${tenants[0].tenantId}/projects`])
    expect(getSessionSnapshot().user).toEqual(user)
    expect(localStorage.length).toBe(0)
    expect(sessionStorage.length).toBe(0)
  })

  it('keeps invalid credentials on the login form without opening a session', async () => {
    api.defaults.adapter = async (config) => rejectUnauthorized(config)
    mount('/login')
    const actor = userEvent.setup()
    await actor.type(screen.getByLabelText('Email address'), user.email)
    await actor.type(screen.getByLabelText('Password'), crypto.randomUUID())
    await actor.click(screen.getByRole('button', { name: 'Sign in to your workspace' }))
    await screen.findByRole('alert')
    expect(getAccessToken()).toBeNull()
    expect(screen.getByRole('heading', { name: 'Good to have you back.' })).toBeTruthy()
  })

  it('clears the session and redirects to login when an authenticated request gets 401', async () => {
    authenticate()
    api.defaults.adapter = async (config) => rejectUnauthorized(config)
    mount()
    await screen.findByRole('heading', { name: 'Good to have you back.' })
    expect(getAccessToken()).toBeNull()
    expect(screen.getByRole('status').textContent).toContain('session has ended')
  })

  it('switches tenants from real contract responses and logs out without retaining workspace data', async () => {
    authenticate()
    api.defaults.adapter = async (config) => response(config, config.url?.endsWith('/dashboard/summary') ? emptySummary : tenants)
    mount()
    await screen.findByRole('heading', { name: 'First workspace' })
    const actor = userEvent.setup()
    await actor.selectOptions(screen.getByLabelText('Workspace'), tenants[1].tenantId)
    expect(screen.getByRole('heading', { name: 'Second workspace' })).toBeTruthy()
    expect(screen.getByText('Admin', { selector: '.stat-card > strong' })).toBeTruthy()
    await actor.click(screen.getByRole('button', { name: 'Sign out' }))
    expect(screen.getByRole('heading', { name: 'Good to have you back.' })).toBeTruthy()
    expect(screen.queryByText('Second workspace')).toBeNull()
    expect(getAccessToken()).toBeNull()
  })

  it('shows an empty state and refreshes memberships without inventing workspace data', async () => {
    authenticate()
    let attempts = 0
    api.defaults.adapter = async (config) => response(config, config.url?.endsWith('/dashboard/summary') ? emptySummary : ++attempts === 1 ? [] : tenants)
    mount()
    await screen.findByRole('heading', { name: 'Your next chapter starts with a workspace.' })
    await userEvent.setup().click(screen.getByRole('button', { name: 'Refresh workspaces' }))
    await screen.findByRole('heading', { name: 'First workspace' })
  })

  it('offers a retry after a network error and preserves the signed-in account', async () => {
    authenticate()
    let attempts = 0
    api.defaults.adapter = async (config) => {
      if (config.url?.endsWith('/dashboard/summary')) return response(config, emptySummary)
      if (++attempts === 1) throw new AxiosError('Network unavailable', 'ERR_NETWORK', config)
      return response(config, tenants)
    }
    mount()
    await screen.findByRole('alert')
    expect(getSessionSnapshot().status).toBe('authenticated')
    await userEvent.setup().click(screen.getByRole('button', { name: 'Try again' }))
    await screen.findByRole('heading', { name: 'First workspace' })
  })

  it('does not clear a newer session when an old request returns 401', async () => {
    authenticate()
    let rejectOldRequest: (() => void) | undefined
    api.defaults.adapter = (config) => new Promise((_resolve, reject) => {
      rejectOldRequest = () => reject(new AxiosError('Unauthorized', 'ERR_BAD_REQUEST', config, undefined, response(config, {}, 401)))
    })
    const request = api.get('/auth/me').catch(() => undefined)
    await waitFor(() => expect(rejectOldRequest).toBeTypeOf('function'))
    const newToken = authenticate()
    rejectOldRequest!()
    await request
    expect(getAccessToken()).toBe(newToken)
  })

  it('expires memory-only sessions and rejects late account verification', () => {
    vi.useFakeTimers()
    const generation = openSession(crypto.randomUUID(), 1)
    vi.advanceTimersByTime(1001)
    expect(getAccessToken()).toBeNull()
    expect(getSessionSnapshot().reason).toBe('expired')
    expect(confirmSession(user, generation)).toBe(false)
  })
})
