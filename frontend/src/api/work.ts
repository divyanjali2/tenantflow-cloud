import { api } from './client'
import type { TenantRole } from './types'

export const projectStatuses = ['Planning', 'Active', 'OnHold', 'Completed', 'Cancelled'] as const
export type ProjectStatus = typeof projectStatuses[number]
export const taskStatuses = ['ToDo', 'InProgress', 'Completed'] as const
export type TaskStatus = typeof taskStatuses[number]
export const priorities = ['Low', 'Medium', 'High'] as const
export type Priority = typeof priorities[number]
const statusLabels: Record<string, string> = { ToDo: 'To Do', InProgress: 'In Progress', OnHold: 'On Hold' }
export const statusLabel = (status: string) => statusLabels[status] ?? status

export interface ProjectInput { name: string; description: string | null; status: ProjectStatus }
export interface Project extends ProjectInput {
  id: string; tenantId: string; createdByUserId: string; createdAtUtc: string; updatedAtUtc: string
}
export interface TaskInput {
  title: string; description: string | null; status: TaskStatus; priority: Priority
  assignedToUserId: string | null; dueDateUtc: string | null
}
export interface ProjectTask extends TaskInput {
  id: string; tenantId: string; projectId: string; createdByUserId: string; createdAtUtc: string; updatedAtUtc: string
}
export interface TaskPage { items: ProjectTask[]; page: number; pageSize: number; totalCount: number; totalPages: number }
export interface TaskFilters { search?: string; status?: TaskStatus; priority?: Priority; page: number; pageSize: number }
export interface TenantMember { userId: string; email: string; role: TenantRole; createdAtUtc: string }

const projectsPath = (tenantId: string) => `/tenants/${encodeURIComponent(tenantId)}/projects`
const tasksPath = (tenantId: string, projectId: string) => `${projectsPath(tenantId)}/${encodeURIComponent(projectId)}/tasks`

export const workApi = {
  projects: async (tenantId: string, signal: AbortSignal) => (await api.get<Project[]>(projectsPath(tenantId), { signal })).data,
  createProject: async (tenantId: string, input: ProjectInput, signal: AbortSignal) => (await api.post<Project>(projectsPath(tenantId), input, { signal })).data,
  updateProject: async (tenantId: string, id: string, input: ProjectInput, signal: AbortSignal) => (await api.put<Project>(`${projectsPath(tenantId)}/${encodeURIComponent(id)}`, input, { signal })).data,
  deleteProject: async (tenantId: string, id: string, signal: AbortSignal) => { await api.delete(`${projectsPath(tenantId)}/${encodeURIComponent(id)}`, { signal }) },
  members: async (tenantId: string, signal: AbortSignal) => (await api.get<TenantMember[]>(`/tenants/${encodeURIComponent(tenantId)}/members`, { signal })).data,
  tasks: async (tenantId: string, projectId: string, filters: TaskFilters, signal: AbortSignal) => (await api.get<TaskPage>(tasksPath(tenantId, projectId), { params: filters, signal })).data,
  createTask: async (tenantId: string, projectId: string, input: TaskInput, signal: AbortSignal) => (await api.post<ProjectTask>(tasksPath(tenantId, projectId), input, { signal })).data,
  updateTask: async (tenantId: string, projectId: string, id: string, input: TaskInput, signal: AbortSignal) => (await api.put<ProjectTask>(`${tasksPath(tenantId, projectId)}/${encodeURIComponent(id)}`, input, { signal })).data,
  deleteTask: async (tenantId: string, projectId: string, id: string, signal: AbortSignal) => { await api.delete(`${tasksPath(tenantId, projectId)}/${encodeURIComponent(id)}`, { signal }) },
  taskStatus: async (tenantId: string, projectId: string, id: string, status: TaskStatus, signal: AbortSignal) => (await api.patch<ProjectTask>(`${tasksPath(tenantId, projectId)}/${encodeURIComponent(id)}/status`, { status }, { signal })).data,
  taskAssignee: async (tenantId: string, projectId: string, id: string, assignedToUserId: string | null, signal: AbortSignal) => (await api.patch<ProjectTask>(`${tasksPath(tenantId, projectId)}/${encodeURIComponent(id)}/assignee`, { assignedToUserId }, { signal })).data,
}

export const canManageWork = (role: TenantRole) => role === 'Owner' || role === 'Admin'
export function displayDate(value: string) {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? 'Date unavailable' : new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' }).format(date)
}
