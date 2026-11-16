import { useState } from 'react'
import { CalendarDays, CheckSquare2, ChevronRight, FolderKanban, LayoutDashboard, LogOut, Menu, Settings2, ShieldCheck, UsersRound, X } from 'lucide-react'
import { NavLink, Outlet, useLocation } from 'react-router-dom'
import { clearSession } from '../auth/session'
import { useSession } from '../auth/useSession'
import { useTenants } from '../tenants/context'
import { Brand } from './Brand'
import { TenantSelector } from './TenantSelector'

const navigation = [
  { to: '/dashboard', label: 'Overview', icon: LayoutDashboard },
  { to: '/projects', label: 'Projects', icon: FolderKanban },
  { to: '/tasks', label: 'Tasks', icon: CheckSquare2 },
  { to: '/team', label: 'Team', icon: UsersRound },
  { to: '/settings', label: 'Settings', icon: Settings2 },
]
function Navigation({ onNavigate }: { onNavigate?: () => void }) {
  return <nav aria-label="Main navigation" className="navigation">{navigation.map(({ to, label, icon: Icon }) =>
    <NavLink key={to} to={to} onClick={onNavigate} className={({ isActive }) => `nav-item ${isActive ? 'nav-item-active' : ''}`}>
      <Icon size={19} strokeWidth={1.8} aria-hidden="true" /><span>{label}</span>
    </NavLink>)}</nav>
}
export function AppLayout() {
  const { user } = useSession()
  const { selectedTenant } = useTenants()
  const location = useLocation()
  const [mobileNav, setMobileNav] = useState(false)
  const page = navigation.find((item) => item.to === location.pathname)?.label ?? 'Workspace'
  const initials = user?.email.slice(0, 2).toUpperCase() ?? ''
  return <div className="app-shell">
    <a href="#main-content" className="skip-link">Skip to content</a>
    <aside className="sidebar"><Brand /><p className="sidebar-label">WORKSPACE</p><Navigation />
      <div className="sidebar-bottom"><div className="workspace-note"><ShieldCheck size={20} aria-hidden="true" /><div><strong>{selectedTenant?.role ?? 'Your workspace'}</strong><p>{selectedTenant ? 'Current workspace role' : 'Select a workspace to begin'}</p></div></div>
        <button className="logout-button" onClick={() => clearSession()}><LogOut size={18} aria-hidden="true" />Sign out</button><p className="sidebar-footer">A little clarity. A lot of progress.</p></div>
    </aside>
    <div className="app-main"><header className="topbar">
      <div className="flex min-w-0 items-center gap-3"><button className="icon-button lg:hidden" aria-label={mobileNav ? 'Close navigation' : 'Open navigation'} aria-expanded={mobileNav} aria-controls="mobile-navigation" onClick={() => setMobileNav(!mobileNav)}>{mobileNav ? <X size={22} /> : <Menu size={22} />}</button><div className="breadcrumbs"><span>Workspace</span><ChevronRight size={14} aria-hidden="true" /><strong>{page}</strong></div></div>
      <div className="flex items-center gap-4"><span className="topbar-date"><CalendarDays size={15} aria-hidden="true" />{new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric', year: 'numeric' }).format(new Date())}</span><div className="user-avatar" title={user?.email} aria-label={`Signed in as ${user?.email}`}>{initials}</div></div>
    </header>
      {mobileNav && <div id="mobile-navigation" className="mobile-navigation lg:hidden"><Brand /><Navigation onNavigate={() => setMobileNav(false)} /><button className="logout-button" onClick={() => clearSession()}><LogOut size={18} />Sign out</button></div>}
      <div className="workspace-toolbar"><span className="toolbar-caption">Your space to move work forward.</span><TenantSelector /></div>
      <main id="main-content" className="content-area" tabIndex={-1}><Outlet /></main>
      <footer className="app-footer"><span>TenantFlow Cloud</span><span>Built for better teamwork.</span></footer>
    </div>
  </div>
}
