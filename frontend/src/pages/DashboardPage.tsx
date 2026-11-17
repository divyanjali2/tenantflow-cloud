import { ArrowUpRight, Building2, CalendarDays, Check, CheckSquare2, FolderKanban, ShieldCheck, UsersRound } from 'lucide-react'
import { Link } from 'react-router-dom'
import { useSession } from '../auth/useSession'
import { EmptyState, ErrorState, LoadingState, PageHeader } from '../components/States'
import { useTenants } from '../tenants/context'
import { lazy, Suspense } from 'react'

const DashboardAnalytics = lazy(() => import('../components/DashboardAnalytics').then((module) => ({ default: module.DashboardAnalytics })))

const shortcuts = [
  { to: '/projects', title: 'Projects', description: 'Give your team’s next big idea a home.', icon: FolderKanban, color: 'shortcut-purple' },
  { to: '/tasks', title: 'Tasks', description: 'Make room for the work that matters.', icon: CheckSquare2, color: 'shortcut-amber' },
  { to: '/team', title: 'Team', description: 'Good things happen when you work together.', icon: UsersRound, color: 'shortcut-blue' },
]
export function DashboardPage() {
  const { user } = useSession()
  const { resource, selectedTenant, reload } = useTenants()
  return <><PageHeader eyebrow="WORKSPACE OVERVIEW" title="Your workspace, in focus." description="A little less scattered. A little more together. Welcome to your team’s home." />
    {resource.status === 'loading' ? <LoadingState /> : resource.status === 'error' ? <ErrorState message={resource.message} onRetry={reload} /> : !selectedTenant ?
      <EmptyState title="Your next chapter starts with a workspace."><p>You don’t belong to a workspace yet. Ask a workspace owner to invite your registered email address.</p><button className="button-secondary mt-4" onClick={reload}>Refresh workspaces</button></EmptyState> : <>
        <section className="welcome-panel"><div><span className="welcome-badge"><span />YOUR SELECTED WORKSPACE</span><h2>{selectedTenant.name}</h2><p>A shared home for your projects, your people, and everything you’ll accomplish together.</p><Link to="/projects" className="welcome-link">Explore your workspace <ArrowUpRight size={17} /></Link></div><div className="welcome-art" aria-hidden="true"><div className="welcome-grid" /><div className="welcome-tile tile-one"><FolderKanban size={32} strokeWidth={1.3} /></div><div className="welcome-tile tile-two"><Check size={30} strokeWidth={1.7} /></div><span className="art-spark spark-one" /><span className="art-spark spark-two" /></div></section>
        <section className="stats-grid" aria-label="Workspace details">
          <div className="stat-card"><span className="stat-icon stat-teal"><Building2 size={20} /></span><p>Your workspaces</p><strong>{resource.tenants.length.toLocaleString()}</strong><span>Connected to your account</span></div>
          <div className="stat-card"><span className="stat-icon stat-purple"><ShieldCheck size={20} /></span><p>Your workspace role</p><strong>{selectedTenant.role}</strong><span>In {selectedTenant.name}</span></div>
          <div className="stat-card"><span className="stat-icon stat-amber"><CalendarDays size={20} /></span><p>Workspace created</p><strong className="stat-date">{new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric', year: 'numeric' }).format(new Date(selectedTenant.createdAtUtc))}</strong><span>Room to build something great</span></div>
        </section>
        <Suspense fallback={<LoadingState label="Loading dashboard statistics…" />}><DashboardAnalytics key={selectedTenant.tenantId} tenantId={selectedTenant.tenantId} /></Suspense>
        <section className="mt-9"><div className="section-heading"><h2>Make yourself at home</h2><span>Your workspace essentials</span></div><div className="shortcuts-grid">{shortcuts.map(({ to, title, description, icon: Icon, color }) => <Link className="shortcut-card" to={to} key={to}><div className="flex items-center justify-between"><span className={`shortcut-icon ${color}`}><Icon size={22} strokeWidth={1.7} /></span><ArrowUpRight size={18} className="text-slate-400" /></div><h3>{title}</h3><p>{description}</p><span className="shortcut-label">Open {title.toLowerCase()} <ArrowUpRight size={14} /></span></Link>)}</div></section>
        <section className="account-strip"><span className="account-check"><Check size={18} /></span><div><strong>You’re in the right place.</strong><p>Signed in as <span>{user?.email}</span></p></div><Link to="/settings">Account details <ArrowUpRight size={15} /></Link></section>
      </>}
  </>
}
