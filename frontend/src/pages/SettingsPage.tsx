import { LogOut, Mail, ShieldCheck } from 'lucide-react'
import { clearSession } from '../auth/session'
import { useSession } from '../auth/useSession'
import { PageHeader } from '../components/States'
import { useTenants } from '../tenants/context'

export function SettingsPage() {
  const { user } = useSession()
  const { selectedTenant } = useTenants()
  return <><PageHeader eyebrow="ACCOUNT" title="A space that feels like you." description="Your account and current workspace, at a glance." />
    <section className="panel settings-panel"><h2>Account details</h2><p className="text-sm text-slate-500">The account you’re currently signed in with.</p><dl className="settings-details"><div><dt><Mail size={17} />Email address</dt><dd>{user?.email}</dd></div><div><dt><ShieldCheck size={17} />Workspace role</dt><dd>{selectedTenant ? `${selectedTenant.role} · ${selectedTenant.name}` : 'No workspace selected'}</dd></div></dl><div className="settings-signout"><div><strong>Ready to step away?</strong><p>Sign out of your session on this device.</p></div><button className="button-secondary" onClick={() => clearSession()}><LogOut size={16} />Sign out</button></div></section>
  </>
}
