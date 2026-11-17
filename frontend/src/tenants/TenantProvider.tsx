import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { api, apiErrorMessage } from '../api/client'
import type { Tenant } from '../api/types'
import { TenantContext } from './context'
import type { TenantResource } from './context'

export function TenantProvider({ children }: { children: ReactNode }) {
  const [resource, setResource] = useState<TenantResource>({ status: 'loading' })
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [revision, setRevision] = useState(0)
  useEffect(() => {
    const controller = new AbortController()
    void api.get<Tenant[]>('/tenants/mine', { signal: controller.signal }).then(
      ({ data }) => { if (!controller.signal.aborted) setResource({ status: 'success', tenants: data }) },
      (error: unknown) => {
        if (!controller.signal.aborted) setResource({ status: 'error', message: apiErrorMessage(error) })
      },
    )
    return () => controller.abort()
  }, [revision])
  const tenants = resource.status === 'success' ? resource.tenants : []
  const selectedTenant = tenants.find((tenant) => tenant.tenantId === selectedId) ?? tenants[0] ?? null
  function selectTenant(id: string) {
    if (tenants.some((tenant) => tenant.tenantId === id)) setSelectedId(id)
  }
  function reload() {
    setResource({ status: 'loading' })
    setRevision((value) => value + 1)
  }
  return <TenantContext value={{ resource, selectedTenant, selectTenant, reload }}>{children}</TenantContext>
}
