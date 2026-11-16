import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import { AxiosError } from 'axios'
import type { AxiosResponse, InternalAxiosRequestConfig } from 'axios'
import App from '../src/App'
import { api } from '../src/api/client'
import type { Project, ProjectTask, TenantMember } from '../src/api/work'
import type { CurrentUser, Tenant, TenantRole } from '../src/api/types'
import { clearSession, confirmSession, openSession } from '../src/auth/session'

// Contract fixtures are test-only. Functional pages always use the real Axios client.
const originalAdapter = api.defaults.adapter
let user: CurrentUser
let tenants: Tenant[]
let projects: Project[]
let tasks: ProjectTask[]
let members: TenantMember[]
let calls: { method: string; url: string; body: Record<string, unknown>; params: Record<string, unknown> }[]
const projectPath = (index = 0) => `/tenants/${tenants[index].tenantId}/projects`
const taskPath = (index = 0) => `${projectPath(index)}/${projects[index].id}/tasks`
const response = (config: InternalAxiosRequestConfig, data: unknown, status = 200): AxiosResponse => ({ config, data: JSON.parse(JSON.stringify(data)) as unknown, status, statusText: 'Test response', headers: {} })
function failure(config: InternalAxiosRequestConfig, status: number, data: unknown = {}) {
  return new AxiosError('Test request failure', 'ERR_BAD_REQUEST', config, undefined, response(config, data, status))
}
beforeAll(() => {
  // JSDOM does not implement native dialog modality; browsers provide focus trapping.
  HTMLDialogElement.prototype.showModal = function () { this.setAttribute('open', '') }
  HTMLDialogElement.prototype.close = function () { this.removeAttribute('open') }
})
beforeEach(() => {
  clearSession()
  user = { userId: crypto.randomUUID(), email: 'current@example.test' }
  confirmSession(user, openSession(crypto.randomUUID(), 3600))
  tenants = ['A', 'B'].map((name) => ({ tenantId: crypto.randomUUID(), name: `Workspace ${name}`, slug: name.toLowerCase(), role: name === 'A' ? 'Owner' : 'Admin', createdAtUtc: '2026-01-01T00:00:00Z' }))
  projects = tenants.map((tenant, index) => ({ id: crypto.randomUUID(), tenantId: tenant.tenantId, name: index ? 'Beta project' : 'Alpha project', description: 'A real API contract', status: 'Planning', createdByUserId: user.userId, createdAtUtc: '2026-01-01T00:00:00Z', updatedAtUtc: '2026-01-01T00:00:00Z' }))
  members = [{ userId: user.userId, email: user.email, role: 'Owner', createdAtUtc: '2026-01-01T00:00:00Z' }, { userId: crypto.randomUUID(), email: 'teammate@example.test', role: 'Member', createdAtUtc: '2026-01-01T00:00:00Z' }]
  tasks = projects.flatMap((project, index) => (['ToDo', 'InProgress', 'Completed'] as const).map((status, position) => ({ id: crypto.randomUUID(), tenantId: project.tenantId, projectId: project.id, title: `${index ? 'Beta' : 'Alpha'} task ${position + 1}`, description: 'Keep this description', status, priority: position === 0 ? 'High' : 'Medium', assignedToUserId: position === 0 ? user.userId : position === 1 ? members[1].userId : null, dueDateUtc: '2027-01-15T03:04:56Z', createdByUserId: user.userId, createdAtUtc: '2026-01-01T00:00:00Z', updatedAtUtc: '2026-01-01T00:00:00Z' })))
  calls = []
  api.defaults.adapter = adapter
})
afterEach(() => { cleanup(); clearSession(); api.defaults.adapter = originalAdapter })

async function adapter(config: InternalAxiosRequestConfig): Promise<AxiosResponse> {
  const method = config.method ?? 'get'
  const url = config.url ?? ''
  const body: Record<string, unknown> = typeof config.data === 'string' ? JSON.parse(config.data) as Record<string, unknown> : {}
  const params = (config.params ?? {}) as Record<string, unknown>
  calls.push({ method, url, body, params })
  if (url === '/tenants/mine') return response(config, tenants)
  for (let index = 0; index < tenants.length; index++) {
    if (url === `/tenants/${tenants[index].tenantId}/members`) return response(config, members)
    if (url === projectPath(index)) {
      if (method === 'get') return response(config, projects.filter((project) => project.tenantId === tenants[index].tenantId))
      if (method === 'post') {
        const project = { ...projects[index], ...body, id: crypto.randomUUID() } as Project
        projects.push(project)
        return response(config, project, 201)
      }
    }
  }
  const project = projects.find((item) => url === `/tenants/${item.tenantId}/projects/${item.id}`)
  if (project && method === 'put') { Object.assign(project, body); return response(config, project) }
  if (project && method === 'delete') { projects = projects.filter((item) => item.id !== project.id); return response(config, null, 204) }
  const parent = projects.find((item) => url.startsWith(`/tenants/${item.tenantId}/projects/${item.id}/tasks`))
  if (parent) {
    const base = `/tenants/${parent.tenantId}/projects/${parent.id}/tasks`
    if (url === base && method === 'get') {
      let items = tasks.filter((task) => task.tenantId === parent.tenantId && task.projectId === parent.id)
      if (params.status) items = items.filter((task) => task.status === params.status)
      if (params.priority) items = items.filter((task) => task.priority === params.priority)
      if (params.search) items = items.filter((task) => task.title.toLowerCase().includes(String(params.search).toLowerCase()))
      const page = Number(params.page ?? 1), pageSize = Number(params.pageSize ?? 20)
      return response(config, { items: items.slice((page - 1) * pageSize, page * pageSize), page, pageSize, totalCount: items.length, totalPages: Math.ceil(items.length / pageSize) })
    }
    if (url === base && method === 'post') {
      const task = { ...tasks[0], ...body, id: crypto.randomUUID(), tenantId: parent.tenantId, projectId: parent.id } as ProjectTask
      tasks.push(task)
      return response(config, task, 201)
    }
    const task = tasks.find((item) => url === `${base}/${item.id}` || url === `${base}/${item.id}/status` || url === `${base}/${item.id}/assignee`)
    if (task && (method === 'put' || method === 'patch')) { Object.assign(task, body); return response(config, task) }
    if (task && method === 'delete') { tasks = tasks.filter((item) => item.id !== task.id); return response(config, null, 204) }
  }
  throw failure(config, 404)
}
function mount(path = '/projects') { return render(<MemoryRouter initialEntries={[path]}><App /></MemoryRouter>) }
async function mountBoard(role: TenantRole = 'Owner') {
  tenants[0].role = role
  mount(`/projects/${projects[0].id}/tasks`)
  await screen.findByRole('article', { name: `Task: ${tasks.find((task) => task.projectId === projects[0].id)!.title}` })
}

describe('project management', () => {
  it('creates, edits and confirms deletion with the existing DTO and route', async () => {
    mount()
    await screen.findByRole('heading', { name: 'Alpha project' })
    const actor = userEvent.setup()
    await actor.click(screen.getByRole('button', { name: 'Create project' }))
    let dialog = screen.getByRole('dialog')
    await actor.type(within(dialog).getByLabelText('Project name'), 'New project')
    await actor.type(within(dialog).getByLabelText(/Description/), 'New description')
    await actor.selectOptions(within(dialog).getByLabelText('Project status'), 'Active')
    await actor.click(within(dialog).getByRole('button', { name: 'Create project' }))
    await screen.findByRole('heading', { name: 'New project' })
    const create = calls.find((call) => call.method === 'post')!
    expect(create.url).toBe(projectPath())
    expect(create.body).toEqual({ name: 'New project', description: 'New description', status: 'Active' })
    await actor.click(screen.getByRole('button', { name: 'Edit project New project' }))
    dialog = screen.getByRole('dialog')
    await actor.clear(within(dialog).getByLabelText('Project name'))
    await actor.type(within(dialog).getByLabelText('Project name'), 'Renamed project')
    await actor.click(within(dialog).getByRole('button', { name: 'Save project' }))
    await screen.findByRole('heading', { name: 'Renamed project' })
    expect(calls.find((call) => call.method === 'put')?.body).toEqual({ name: 'Renamed project', description: 'New description', status: 'Active' })
    await actor.click(screen.getByRole('button', { name: 'Delete project Renamed project' }))
    expect(calls.filter((call) => call.method === 'delete')).toHaveLength(0)
    await actor.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Cancel' }))
    expect(calls.filter((call) => call.method === 'delete')).toHaveLength(0)
    await actor.click(screen.getByRole('button', { name: 'Delete project Renamed project' }))
    await actor.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Delete project' }))
    await screen.findByText('Project deleted.')
    expect(screen.queryByRole('heading', { name: 'Renamed project' })).toBeNull()
  })

  it.each<TenantRole>(['Admin', 'Member'])('shows project actions appropriate to %s', async (role) => {
    tenants[0].role = role
    mount()
    await screen.findByRole('heading', { name: 'Alpha project' })
    expect(screen.queryByRole('button', { name: 'Create project' }) !== null).toBe(role === 'Admin')
    expect(screen.queryByRole('button', { name: 'Edit project Alpha project' }) !== null).toBe(role === 'Admin')
    expect(screen.queryByRole('button', { name: 'Delete project Alpha project' })).toBeNull()
    await userEvent.setup().click(screen.getByRole('link', { name: 'Alpha project' }))
    await screen.findByRole('article', { name: 'Task: Alpha task 1' })
  })

  it.each([400, 403, 404, 0])('keeps the form open and reports %s failures', async (status) => {
    api.defaults.adapter = async (config) => {
      if (config.method === 'post') throw status ? failure(config, status, { errors: { Name: ['The project name is invalid.'] } }) : new AxiosError('Network error', 'ERR_NETWORK', config)
      return adapter(config)
    }
    mount()
    await screen.findByRole('heading', { name: 'Alpha project' })
    const actor = userEvent.setup()
    await actor.click(screen.getByRole('button', { name: 'Create project' }))
    const dialog = screen.getByRole('dialog')
    await actor.type(within(dialog).getByLabelText('Project name'), 'Invalid project')
    await actor.click(within(dialog).getByRole('button', { name: 'Create project' }))
    expect(await within(dialog).findByRole('alert')).toBeTruthy()
    expect(screen.getByRole('dialog')).toBeTruthy()
    expect(projects).toHaveLength(2)
  })
})

describe('task management', () => {
  it('creates, edits and deletes tasks, preserves due-date precision, and uses exact DTO fields', async () => {
    await mountBoard()
    const actor = userEvent.setup()
    await actor.click(screen.getByRole('button', { name: 'Create task' }))
    let dialog = screen.getByRole('dialog')
    await actor.type(within(dialog).getByLabelText('Task title'), 'New task')
    await actor.type(within(dialog).getByLabelText(/Description/), 'New task description')
    await actor.selectOptions(within(dialog).getByLabelText('Priority'), 'Low')
    await actor.selectOptions(within(dialog).getByLabelText('Assignee'), members[1].userId)
    fireEvent.change(within(dialog).getByLabelText(/Due date and time/), { target: { value: '2027-02-10T14:30' } })
    await actor.click(within(dialog).getByRole('button', { name: 'Create task' }))
    await screen.findByRole('article', { name: 'Task: New task' })
    expect(calls.find((call) => call.method === 'post')).toMatchObject({ url: taskPath(), body: { title: 'New task', description: 'New task description', status: 'ToDo', priority: 'Low', assignedToUserId: members[1].userId, dueDateUtc: new Date('2027-02-10T14:30').toISOString() } })
    await actor.click(screen.getByRole('button', { name: 'Edit task Alpha task 1' }))
    dialog = screen.getByRole('dialog')
    await actor.clear(within(dialog).getByLabelText('Task title'))
    await actor.type(within(dialog).getByLabelText('Task title'), 'Updated task')
    await actor.click(within(dialog).getByRole('button', { name: 'Save task' }))
    await screen.findByRole('article', { name: 'Task: Updated task' })
    expect(calls.find((call) => call.method === 'put')?.body).toEqual({ title: 'Updated task', description: 'Keep this description', status: 'ToDo', priority: 'High', assignedToUserId: user.userId, dueDateUtc: '2027-01-15T03:04:56Z' })
    await actor.click(screen.getByRole('button', { name: 'Delete task New task' }))
    expect(calls.filter((call) => call.method === 'delete')).toHaveLength(0)
    await actor.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Delete task' }))
    await screen.findByText('Task deleted.')
    expect(screen.queryByRole('article', { name: 'Task: New task' })).toBeNull()
  })

  it('patches status and assignment independently, including null to unassign', async () => {
    await mountBoard()
    const actor = userEvent.setup()
    const id = tasks[0].id
    await actor.selectOptions(screen.getByLabelText('Status for Alpha task 1'), 'Completed')
    await screen.findByText('Task status updated.')
    await screen.findByRole('article', { name: 'Task: Alpha task 1' })
    expect(within(screen.getByRole('region', { name: 'Completed tasks' })).getByRole('article', { name: 'Task: Alpha task 1' })).toBeTruthy()
    await actor.selectOptions(screen.getByLabelText('Assignee for Alpha task 1'), members[1].userId)
    await screen.findByText('Task assigned.')
    await screen.findByRole('article', { name: 'Task: Alpha task 1' })
    await actor.selectOptions(screen.getByLabelText('Assignee for Alpha task 1'), '')
    await screen.findByText('Task unassigned.')
    expect(calls.filter((call) => call.method === 'patch').map(({ url, body }) => ({ url, body }))).toEqual([
      { url: `${taskPath()}/${id}/status`, body: { status: 'Completed' } },
      { url: `${taskPath()}/${id}/assignee`, body: { assignedToUserId: members[1].userId } },
      { url: `${taskPath()}/${id}/assignee`, body: { assignedToUserId: null } },
    ])
  })

  it('allows Members to change only their own task status without requesting privileged member lists', async () => {
    await mountBoard('Member')
    expect(screen.queryByRole('button', { name: 'Create task' })).toBeNull()
    expect(screen.queryByRole('button', { name: /Edit task/ })).toBeNull()
    expect(screen.queryByLabelText('Status for Alpha task 2')).toBeNull()
    expect(screen.queryByLabelText('Status for Alpha task 3')).toBeNull()
    expect(screen.queryByLabelText('Assignee for Alpha task 1')).toBeNull()
    expect(calls.some((call) => call.url.endsWith('/members'))).toBe(false)
    await userEvent.setup().selectOptions(screen.getByLabelText('Status for Alpha task 1'), 'InProgress')
    await screen.findByText('Task status updated.')
    expect(calls.find((call) => call.method === 'patch')?.body).toEqual({ status: 'InProgress' })
  })

  it('uses paginated results, combines server filters and resets the page when a filter changes', async () => {
    tasks = Array.from({ length: 25 }, (_, index) => ({ ...tasks[0], id: crypto.randomUUID(), title: `Item ${index + 1}`, priority: index % 2 ? 'Low' : 'High' }))
    await mountBoard()
    const actor = userEvent.setup()
    expect(screen.getAllByRole('article')).toHaveLength(20)
    await actor.click(screen.getByRole('button', { name: 'Next page' }))
    await screen.findByRole('article', { name: 'Task: Item 25' })
    expect(screen.getAllByRole('article')).toHaveLength(5)
    expect(screen.getByText('Page 2 of 2')).toBeTruthy()
    await actor.selectOptions(screen.getByLabelText('Filter by priority'), 'High')
    await screen.findByRole('article', { name: 'Task: Item 1' })
    expect(screen.getByText('Page 1 of 1')).toBeTruthy()
    expect(screen.getAllByRole('article')).toHaveLength(13)
    await actor.type(screen.getByLabelText('Search tasks'), 'Item 25')
    await actor.click(screen.getByRole('button', { name: 'Search' }))
    await screen.findByRole('article', { name: 'Task: Item 25' })
    await waitFor(() => expect(screen.getAllByRole('article')).toHaveLength(1))
    await actor.selectOptions(screen.getByLabelText('Filter by status'), 'Completed')
    await screen.findByText('No tasks match these filters.')
    expect(calls.filter((call) => call.url === taskPath() && call.method === 'get').at(-1)?.params).toEqual({ search: 'Item 25', priority: 'High', status: 'Completed', page: 1, pageSize: 20 })
  })

  it('redirects to login and discards work data on a mutation 401', async () => {
    await mountBoard()
    api.defaults.adapter = async (config) => { if (config.method === 'patch') throw failure(config, 401); return adapter(config) }
    await userEvent.setup().selectOptions(screen.getByLabelText('Status for Alpha task 1'), 'Completed')
    await screen.findByRole('heading', { name: 'Good to have you back.' })
    expect(screen.queryByText('Alpha task 1')).toBeNull()
  })
})

describe('tenant isolation', () => {
  it('clears project dialogs and ignores late project responses on tenant switching', async () => {
    let resolveOld: (() => void) | undefined
    api.defaults.adapter = (config) => config.url === projectPath() ? new Promise((resolve) => { resolveOld = () => resolve(response(config, [projects[0]])) }) : adapter(config)
    mount()
    await screen.findByRole('option', { name: 'Workspace B' })
    await waitFor(() => expect(resolveOld).toBeTypeOf('function'))
    await userEvent.setup().selectOptions(screen.getByLabelText('Workspace'), tenants[1].tenantId)
    await screen.findByRole('heading', { name: 'Beta project' })
    await act(async () => resolveOld!())
    expect(screen.queryByRole('heading', { name: 'Alpha project' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Delete project Beta project' })).toBeNull()
  })

  it('clears board dialogs and filters, and never pairs the new tenant with an old project ID', async () => {
    await mountBoard()
    const actor = userEvent.setup()
    await actor.type(screen.getByLabelText('Search tasks'), 'Alpha')
    await actor.click(screen.getByRole('button', { name: 'Search' }))
    await screen.findByRole('article', { name: 'Task: Alpha task 1' })
    await actor.click(screen.getByRole('button', { name: 'Edit task Alpha task 1' }))
    await actor.selectOptions(screen.getByLabelText('Workspace'), tenants[1].tenantId)
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(screen.queryByRole('article', { name: 'Task: Alpha task 1' })).toBeNull()
    await screen.findByRole('heading', { name: 'Project not available in this workspace' })
    expect(calls.some((call) => call.url.includes(`/tenants/${tenants[1].tenantId}/projects/${projects[0].id}`))).toBe(false)
    await actor.selectOptions(screen.getByLabelText('Project', { exact: true }), projects[1].id)
    await screen.findByRole('article', { name: 'Task: Beta task 1' })
    expect((screen.getByLabelText('Search tasks') as HTMLInputElement).value).toBe('')
    expect(calls.filter((call) => call.url === taskPath(1)).at(-1)?.params).toEqual({ page: 1, pageSize: 20 })
  })

  it('refuses a project ID outside the current tenant before requesting its tasks', async () => {
    mount(`/projects/${projects[1].id}/tasks`)
    await screen.findByRole('heading', { name: 'Project not available in this workspace' })
    expect(calls.some((call) => call.url.includes('/tasks'))).toBe(false)
  })
})
