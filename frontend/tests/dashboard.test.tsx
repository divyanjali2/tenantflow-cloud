import { act, cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import { AxiosError } from 'axios'
import type { AxiosResponse, InternalAxiosRequestConfig } from 'axios'
import App from '../src/App'
import { api } from '../src/api/client'
import type { DashboardSummary } from '../src/api/dashboard'
import type { Tenant } from '../src/api/types'
import { clearSession, confirmSession, openSession } from '../src/auth/session'

const originalAdapter = api.defaults.adapter
let tenants: Tenant[]
let summaries: DashboardSummary[]
let calls: string[]
const path = (index: number) => `/tenants/${tenants[index].tenantId}/dashboard/summary`
const response = (config: InternalAxiosRequestConfig, data: unknown): AxiosResponse => ({ config, data, status: 200, statusText: 'OK', headers: {} })
beforeEach(() => {
  clearSession()
  confirmSession({ userId: crypto.randomUUID(), email: 'analytics@example.test' }, openSession(crypto.randomUUID(), 3600))
  tenants = ['A', 'B'].map((name) => ({ tenantId: crypto.randomUUID(), name: `Workspace ${name}`, slug: name, role: 'Member', createdAtUtc: '2026-01-01T00:00:00Z' }))
  summaries = [
    { totalProjects: 5, activeProjects: 2, completedProjects: 1, totalTasks: 12, todoTasks: 3, inProgressTasks: 6, completedTasks: 3, overdueTasks: 2, totalMembers: 7, taskCompletionPercentage: 25 },
    { totalProjects: 9, activeProjects: 3, completedProjects: 4, totalTasks: 10, todoTasks: 2, inProgressTasks: 0, completedTasks: 8, overdueTasks: 1, totalMembers: 11, taskCompletionPercentage: 80 },
  ]
  calls = []
  api.defaults.adapter = adapter
})
afterEach(() => { cleanup(); clearSession(); api.defaults.adapter = originalAdapter })
async function adapter(config: InternalAxiosRequestConfig) {
  calls.push(config.url ?? '')
  if (config.url === '/tenants/mine') return response(config, tenants)
  const index = tenants.findIndex((_, position) => path(position) === config.url)
  if (index >= 0) return response(config, summaries[index])
  throw new Error('Unexpected dashboard endpoint')
}
function mount() { return render(<MemoryRouter initialEntries={['/dashboard']}><App /></MemoryRouter>) }
function stat(label: string, value: string) { expect(within(screen.getByRole('article', { name: label })).getByText(value)).toBeTruthy() }

describe('real dashboard summary integration', () => {
  it('renders every metric, chart values and completion progress from the response', async () => {
    mount()
    await screen.findByRole('article', { name: 'Total Projects' })
    stat('Total Projects', '5'); stat('Active Projects', '2'); stat('Total Tasks', '12')
    stat('Completed Tasks', '3'); stat('Team Members', '7'); stat('Overdue Tasks', '2'); stat('Task Completion', '25%')
    const progress = screen.getByRole('progressbar', { name: 'Task completion percentage' }) as HTMLProgressElement
    expect(progress.value).toBe(25)
    const taskReport = within(screen.getByRole('region', { name: 'Task status report' }))
    expect(taskReport.getByText('To Do')).toBeTruthy()
    expect(taskReport.getByText('In Progress')).toBeTruthy()
    expect(taskReport.getByText('6')).toBeTruthy()
    const projectReport = within(screen.getByRole('region', { name: 'Project status report' }))
    expect(projectReport.getByText('Other', { selector: 'dt' })).toBeTruthy()
    expect(projectReport.getByText('1', { selector: 'dd' })).toBeTruthy()
    expect(calls).toEqual(['/tenants/mine', path(0)])
  })

  it('refreshes real summary values', async () => {
    mount()
    await screen.findByRole('article', { name: 'Total Projects' })
    summaries[0] = { ...summaries[0], totalProjects: 15, activeProjects: 10 }
    await userEvent.setup().click(screen.getByRole('button', { name: 'Refresh statistics' }))
    await waitFor(() => stat('Total Projects', '15'))
    expect(calls.filter((call) => call === path(0))).toHaveLength(2)
  })

  it('displays zeros and meaningful empty states without synthetic chart slices', async () => {
    summaries[0] = { totalProjects: 0, activeProjects: 0, completedProjects: 0, totalTasks: 0, todoTasks: 0, inProgressTasks: 0, completedTasks: 0, overdueTasks: 0, totalMembers: 2, taskCompletionPercentage: 0 }
    mount()
    await screen.findByRole('heading', { name: 'Your workspace is ready to grow' })
    stat('Total Tasks', '0'); stat('Task Completion', '0%'); stat('Team Members', '2')
    expect(screen.getByText('No tasks to chart yet.')).toBeTruthy()
    expect(screen.getByText('No projects to chart yet.')).toBeTruthy()
  })

  it('hides old statistics immediately when switching tenants and ignores late responses', async () => {
    let finishOld: (() => void) | undefined
    let delayed = false
    api.defaults.adapter = (config) => delayed && config.url === path(0)
      ? new Promise((resolve) => { finishOld = () => resolve(response(config, summaries[0])) })
      : adapter(config)
    mount()
    await screen.findByRole('article', { name: 'Total Projects' })
    delayed = true
    const actor = userEvent.setup()
    await actor.click(screen.getByRole('button', { name: 'Refresh statistics' }))
    await waitFor(() => expect(finishOld).toBeTypeOf('function'))
    expect(screen.queryByRole('article', { name: 'Total Projects' })).toBeNull()
    expect(screen.getByRole('status', { name: 'Loading dashboard statistics' })).toBeTruthy()
    await actor.selectOptions(screen.getByLabelText('Workspace'), tenants[1].tenantId)
    await screen.findByRole('article', { name: 'Total Projects' })
    stat('Total Projects', '9'); stat('Total Tasks', '10'); stat('Task Completion', '80%')
    await act(async () => finishOld!())
    stat('Total Projects', '9'); stat('Team Members', '11'); stat('Overdue Tasks', '1')
  })

  it('shows failures and retries without displaying invented zero values', async () => {
    let attempts = 0
    api.defaults.adapter = async (config) => {
      if (config.url === path(0) && ++attempts === 1) throw new AxiosError('Offline', 'ERR_NETWORK', config)
      return adapter(config)
    }
    mount()
    await screen.findByRole('alert')
    expect(screen.queryByRole('article', { name: 'Total Projects' })).toBeNull()
    await userEvent.setup().click(screen.getByRole('button', { name: 'Try again' }))
    await screen.findByRole('article', { name: 'Total Projects' })
    stat('Total Projects', '5')
  })

  it('clears the dashboard on an authenticated 401', async () => {
    api.defaults.adapter = async (config) => {
      if (config.url === path(0)) throw new AxiosError('Unauthorized', 'ERR_BAD_REQUEST', config, undefined, { ...response(config, {}), status: 401 })
      return adapter(config)
    }
    mount()
    await screen.findByRole('heading', { name: 'Good to have you back.' })
    expect(screen.queryByRole('article', { name: 'Total Projects' })).toBeNull()
  })
})
