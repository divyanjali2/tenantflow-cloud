export interface LoginRequest { email: string; password: string }
export interface LoginResponse { accessToken: string; tokenType: string; expiresIn: number }
export interface CurrentUser { userId: string; email: string }
export type TenantRole = 'Owner' | 'Admin' | 'Member'
export interface Tenant {
  tenantId: string
  name: string
  slug: string
  role: TenantRole
  createdAtUtc: string
}
