import { Outlet } from 'react-router-dom'
import { useTenants } from '../tenants/context'
import { EmptyState, ErrorState, LoadingState } from './States'

export function WorkScope() {
  const { resource, selectedTenant, reload } = useTenants()
  if (resource.status === 'loading') return <LoadingState />
  if (resource.status === 'error') return <ErrorState message={resource.message} onRetry={reload} />
  if (!selectedTenant) return <EmptyState title="No workspace yet"><p>Ask a workspace owner to invite your registered email address.</p><button className="button-secondary mt-4" onClick={reload}>Refresh workspaces</button></EmptyState>
  // Unmount forms, members, filters, project selection and requests on a tenant/role change.
  return <div key={`${selectedTenant.tenantId}:${selectedTenant.role}`}><Outlet /></div>
}
