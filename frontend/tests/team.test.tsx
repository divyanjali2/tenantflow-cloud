import { act, cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import { AxiosError } from 'axios'
import type { AxiosResponse, InternalAxiosRequestConfig } from 'axios'
import App from '../src/App'
import { api } from '../src/api/client'
import type { Tenant, TenantRole } from '../src/api/types'
import type { TenantMember } from '../src/api/work'
import { clearSession, confirmSession, openSession } from '../src/auth/session'

// Test-only contract fixtures. Application pages never import sample records.
const originalAdapter = api.defaults.adapter
let userId: string
let tenants: Tenant[]
let records: Map<string, TenantMember[]>
let addedUser: TenantMember
let calls: { method: string; url: string; body: Record<string, unknown> }[]
const base = (index = 0) => `/tenants/${tenants[index].tenantId}`
const reply = (config: InternalAxiosRequestConfig, data: unknown, status = 200): AxiosResponse => ({ config, data: JSON.parse(JSON.stringify(data)) as unknown, status, statusText: 'Test response', headers: {} })
function failure(config: InternalAxiosRequestConfig, status: number, data: unknown = {}) {
  return new AxiosError('Test response', 'ERR_BAD_REQUEST', config, undefined, reply(config, data, status))
}
function member(email: string, role: TenantRole): TenantMember {
  return { userId: crypto.randomUUID(), email, role, createdAtUtc: '2026-01-01T00:00:00Z' }
}
beforeAll(() => {
  HTMLDialogElement.prototype.showModal = function () { this.setAttribute('open', '') }
  HTMLDialogElement.prototype.close = function () { this.removeAttribute('open') }
})
beforeEach(() => {
  clearSession()
  userId = crypto.randomUUID()
  confirmSession({ userId, email: 'owner@example.test' }, openSession(crypto.randomUUID(), 3600))
  tenants = ['A', 'B'].map((name) => ({ tenantId: crypto.randomUUID(), name: `Workspace ${name}`, slug: name.toLowerCase(), role: 'Owner', createdAtUtc: '2026-01-01T00:00:00Z' }))
  records = new Map([
    [tenants[0].tenantId, [{ ...member('owner@example.test', 'Owner'), userId }, member('admin@example.test', 'Admin'), member('member@example.test', 'Member'), member('other-owner@example.test', 'Owner')]],
    [tenants[1].tenantId, [member('second-workspace@example.test', 'Member')]],
  ])
  addedUser = member('new@example.test', 'Member')
  calls = []
  api.defaults.adapter = adapter
})
afterEach(() => { cleanup(); clearSession(); api.defaults.adapter = originalAdapter })

async function adapter(config: InternalAxiosRequestConfig): Promise<AxiosResponse> {
  const method = config.method ?? 'get', url = config.url ?? ''
  const body = typeof config.data === 'string' ? JSON.parse(config.data) as Record<string, unknown> : {}
  calls.push({ method, url, body })
  if (url === '/tenants/mine') return reply(config, tenants)
  for (let index = 0; index < tenants.length; index++) {
    const items = records.get(tenants[index].tenantId)!
    if (url === `${base(index)}/members` && method === 'get') return reply(config, items)
    if (url === `${base(index)}/invitations` && method === 'post') {
      if (items.some((item) => item.email === body.email)) throw failure(config, 409)
      if (body.email !== addedUser.email) throw failure(config, 404)
      const entry = { ...addedUser, role: body.role as TenantRole }
      items.push(entry)
      return reply(config, entry, 201)
    }
    const target = items.find((item) => url === `${base(index)}/members/${item.userId}` || url === `${base(index)}/members/${item.userId}/role`)
    if (target && method === 'patch') { target.role = body.role as TenantRole; return reply(config, target) }
    if (target && method === 'delete') { records.set(tenants[index].tenantId, items.filter((item) => item.userId !== target.userId)); return reply(config, null, 204) }
  }
  throw failure(config, 404)
}
function mount() { return render(<MemoryRouter initialEntries={['/team']}><App /></MemoryRouter>) }
async function addForm(email: string) {
  const actor = userEvent.setup()
  await actor.click(screen.getByRole('button', { name: 'Add Member' }))
  const dialog = screen.getByRole('dialog')
  await actor.type(within(dialog).getByLabelText('Registered user’s email'), email)
  return { actor, dialog }
}

describe('team directory', () => {
  it('shows real contract fields and protects every Owner row', async () => {
    mount()
    await screen.findByText('admin@example.test')
    const table = screen.getByRole('table')
    expect(within(table).getAllByRole('row')).toHaveLength(5)
    for (const entry of records.get(tenants[0].tenantId)!) expect(within(table).getByText(entry.userId)).toBeTruthy()
    expect(screen.getAllByText('Owner protected')).toHaveLength(2)
    expect(screen.queryByRole('button', { name: 'Remove owner@example.test' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Change role for other-owner@example.test' })).toBeNull()
    const actor = userEvent.setup()
    await actor.type(screen.getByLabelText('Search members by email'), 'ADMIN@EXAMPLE')
    expect(within(table).getAllByRole('row')).toHaveLength(2)
    await actor.clear(screen.getByLabelText('Search members by email'))
    await actor.type(screen.getByLabelText('Search members by email'), 'no-match')
    expect(screen.getByRole('heading', { name: 'No members match your search' })).toBeTruthy()
    await actor.click(screen.getByRole('button', { name: 'Clear search' }))
    expect(screen.getByText('admin@example.test')).toBeTruthy()
  })

  it('gives Admins read-only member access', async () => {
    tenants[0].role = 'Admin'
    mount()
    await screen.findByText('admin@example.test')
    expect(screen.queryByRole('button', { name: 'Add Member' })).toBeNull()
    expect(screen.queryByRole('button', { name: /Change role for/ })).toBeNull()
    expect(screen.queryByRole('button', { name: /^Remove / })).toBeNull()
    expect(calls.some((call) => call.url === `${base()}/members`)).toBe(true)
  })

  it('does not request or expose the member directory for Members', async () => {
    tenants[0].role = 'Member'
    mount()
    await screen.findByRole('heading', { name: 'Team directory access is limited' })
    expect(screen.queryByRole('table')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Add Member' })).toBeNull()
    expect(calls.map((call) => call.url)).toEqual(['/tenants/mine'])
  })

  it('adds a registered user, changes their role, and confirms membership removal', async () => {
    mount()
    await screen.findByText('admin@example.test')
    const { actor, dialog } = await addForm(addedUser.email)
    expect(within(dialog).getByText(/No email invitation is sent/)).toBeTruthy()
    expect(within(dialog).queryByRole('option', { name: 'Owner' })).toBeNull()
    await actor.selectOptions(within(dialog).getByLabelText('Tenant role'), 'Admin')
    await actor.click(within(dialog).getByRole('button', { name: 'Add Member' }))
    await screen.findByText(addedUser.email)
    expect(calls.find((call) => call.method === 'post')).toEqual({ method: 'post', url: `${base()}/invitations`, body: { email: addedUser.email, role: 'Admin' } })
    await actor.click(screen.getByRole('button', { name: `Change role for ${addedUser.email}` }))
    let current = screen.getByRole('dialog')
    expect(within(current).queryByRole('option', { name: 'Owner' })).toBeNull()
    await actor.selectOptions(within(current).getByLabelText('Tenant role'), 'Member')
    await actor.click(within(current).getByRole('button', { name: 'Save role' }))
    await screen.findByText('Member role updated.')
    await screen.findByText(addedUser.email)
    expect(calls.find((call) => call.method === 'patch')).toEqual({ method: 'patch', url: `${base()}/members/${addedUser.userId}/role`, body: { role: 'Member' } })
    await actor.click(screen.getByRole('button', { name: `Remove ${addedUser.email}` }))
    current = screen.getByRole('dialog')
    expect(calls.some((call) => call.method === 'delete')).toBe(false)
    await actor.click(within(current).getByRole('button', { name: 'Cancel' }))
    expect(calls.some((call) => call.method === 'delete')).toBe(false)
    await actor.click(screen.getByRole('button', { name: `Remove ${addedUser.email}` }))
    await actor.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Remove member' }))
    await screen.findByText('Member removed from this workspace.')
    expect(calls.find((call) => call.method === 'delete')).toEqual({ method: 'delete', url: `${base()}/members/${addedUser.userId}`, body: {} })
    expect(screen.queryByText(addedUser.email)).toBeNull()
  })

  it.each([400, 403, 404, 409, 0])('shows actionable add-member feedback for status %s without clearing the form', async (status) => {
    api.defaults.adapter = async (config) => {
      if (config.method === 'post') throw status ? failure(config, status, { errors: { Email: ['Enter a valid registered email.'] } }) : new AxiosError('Offline', 'ERR_NETWORK', config)
      return adapter(config)
    }
    mount()
    await screen.findByText('admin@example.test')
    const { actor, dialog } = await addForm(addedUser.email)
    await actor.click(within(dialog).getByRole('button', { name: 'Add Member' }))
    const alert = await within(dialog).findByRole('alert')
    if (status === 409) expect(alert.textContent).toContain('already a member')
    if (status === 404) expect(alert.textContent).toContain('registered user')
    if (status === 400) expect(alert.textContent).toContain('valid registered email')
    expect((within(dialog).getByLabelText('Registered user’s email') as HTMLInputElement).value).toBe(addedUser.email)
    expect(records.get(tenants[0].tenantId)).toHaveLength(4)
  })

  it('reports list failures and retries without hiding the account session', async () => {
    let attempts = 0
    api.defaults.adapter = async (config) => {
      if (config.url === `${base()}/members` && ++attempts === 1) throw failure(config, 403)
      return adapter(config)
    }
    mount()
    await screen.findByRole('alert')
    expect(screen.queryByRole('table')).toBeNull()
    await userEvent.setup().click(screen.getByRole('button', { name: 'Try again' }))
    await screen.findByText('admin@example.test')
  })

  it('clears the session and team page on an authenticated 401', async () => {
    api.defaults.adapter = async (config) => {
      if (config.url?.endsWith('/members')) throw failure(config, 401)
      return adapter(config)
    }
    mount()
    await screen.findByRole('heading', { name: 'Good to have you back.' })
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('clears search and dialogs when switching tenants and ignores late old responses', async () => {
    mount()
    await screen.findByText('admin@example.test')
    const actor = userEvent.setup()
    await actor.type(screen.getByLabelText('Search members by email'), 'admin')
    await actor.click(screen.getByRole('button', { name: 'Add Member' }))
    await actor.selectOptions(screen.getByLabelText('Workspace'), tenants[1].tenantId)
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(screen.queryByText('admin@example.test')).toBeNull()
    await screen.findByText('second-workspace@example.test')
    expect((screen.getByLabelText('Search members by email') as HTMLInputElement).value).toBe('')

    // Start an A request that deliberately resolves after returning to B.
    let finishOld: (() => void) | undefined
    api.defaults.adapter = (config) => config.url === `${base()}/members` ? new Promise((resolve) => { finishOld = () => resolve(reply(config, records.get(tenants[0].tenantId))) }) : adapter(config)
    await actor.selectOptions(screen.getByLabelText('Workspace'), tenants[0].tenantId)
    await waitFor(() => expect(finishOld).toBeTypeOf('function'))
    await actor.selectOptions(screen.getByLabelText('Workspace'), tenants[1].tenantId)
    await screen.findByText('second-workspace@example.test')
    await act(async () => finishOld!())
    expect(screen.queryByText('admin@example.test')).toBeNull()
    expect(screen.getByText('second-workspace@example.test')).toBeTruthy()
  })
})
