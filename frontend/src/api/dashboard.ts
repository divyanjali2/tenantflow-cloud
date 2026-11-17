import { api } from './client'

export interface DashboardSummary {
  totalProjects: number
  activeProjects: number
  completedProjects: number
  totalTasks: number
  todoTasks: number
  inProgressTasks: number
  completedTasks: number
  overdueTasks: number
  totalMembers: number
  taskCompletionPercentage: number
}

export async function getDashboardSummary(tenantId: string, signal: AbortSignal) {
  return (await api.get<DashboardSummary>(`/tenants/${encodeURIComponent(tenantId)}/dashboard/summary`, { signal })).data
}
