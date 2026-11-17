import axios from 'axios'
import { api, apiErrorMessage } from './client'
import { workApi } from './work'
import type { TenantMember } from './work'

export type AssignableTenantRole = 'Admin' | 'Member'
export interface AddMemberRequest { email: string; role: AssignableTenantRole }
export interface ChangeMemberRoleRequest { role: AssignableTenantRole }
const tenantPath = (tenantId: string) => `/tenants/${encodeURIComponent(tenantId)}`
const memberPath = (tenantId: string, userId: string) => `${tenantPath(tenantId)}/members/${encodeURIComponent(userId)}`

export const teamApi = {
  // Share the existing member-list contract/client with task assignment.
  members: workApi.members,
  add: async (tenantId: string, input: AddMemberRequest, signal: AbortSignal) =>
    (await api.post<TenantMember>(`${tenantPath(tenantId)}/invitations`, input, { signal })).data,
  changeRole: async (tenantId: string, userId: string, input: ChangeMemberRoleRequest, signal: AbortSignal) =>
    (await api.patch<TenantMember>(`${memberPath(tenantId, userId)}/role`, input, { signal })).data,
  remove: async (tenantId: string, userId: string, signal: AbortSignal) => {
    await api.delete(memberPath(tenantId, userId), { signal })
  },
}

export function teamErrorMessage(error: unknown, action: 'add' | 'role' | 'remove') {
  if (axios.isAxiosError(error)) {
    if (error.response?.status === 409) return 'This user is already a member of this workspace. Refresh the team list to see their membership.'
    if (error.response?.status === 404 && action === 'add') return 'No registered user was found for that email, or this workspace is no longer accessible. Check the email and try again.'
    if (error.response?.status === 403) return 'Only a workspace Owner can manage team members. Your permissions may have changed.'
  }
  return apiErrorMessage(error)
}
