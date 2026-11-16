import { createContext, useContext } from 'react'
import type { Tenant } from '../api/types'

export type TenantResource =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'success'; tenants: Tenant[] }
interface TenantContextValue {
  resource: TenantResource
  selectedTenant: Tenant | null
  selectTenant: (id: string) => void
  reload: () => void
}
export const TenantContext = createContext<TenantContextValue | null>(null)
export function useTenants() {
  const value = useContext(TenantContext)
  if (!value) throw new Error('TenantProvider is required')
  return value
}
