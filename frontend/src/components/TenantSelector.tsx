import { Building2, ChevronDown } from 'lucide-react'
import { useTenants } from '../tenants/context'

export function TenantSelector() {
  const { resource, selectedTenant, selectTenant } = useTenants()
  return <div className="tenant-selector">
    <span className="tenant-selector-icon"><Building2 size={19} aria-hidden="true" /></span>
    <div className="min-w-0 flex-1"><label htmlFor="tenant-select">Workspace</label>
      <select id="tenant-select" value={selectedTenant?.tenantId ?? ''} disabled={resource.status !== 'success' || !selectedTenant}
        onChange={(event) => selectTenant(event.target.value)}>
        {!selectedTenant && <option value="">{resource.status === 'loading' ? 'Loading workspaces…' : resource.status === 'error' ? 'Unable to load' : 'No workspaces yet'}</option>}
        {resource.status === 'success' && resource.tenants.map((tenant) => <option key={tenant.tenantId} value={tenant.tenantId}>{tenant.name}</option>)}
      </select>
    </div><ChevronDown size={15} className="pointer-events-none shrink-0 text-slate-400" aria-hidden="true" />
  </div>
}
