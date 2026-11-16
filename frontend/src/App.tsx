import { Navigate, Outlet, Route, Routes, useLocation } from 'react-router-dom'
import { useSession } from './auth/useSession'
import { AppLayout } from './components/AppLayout'
import { LoadingState } from './components/States'
import { DashboardPage } from './pages/DashboardPage'
import { LoginPage } from './pages/LoginPage'
import { SettingsPage } from './pages/SettingsPage'
import { WorkspacePage } from './pages/WorkspacePage'
import { ProjectsPage } from './pages/ProjectsPage'
import { TasksPage } from './pages/TasksPage'
import { WorkScope } from './components/WorkScope'
import { TenantProvider } from './tenants/TenantProvider'

function ProtectedWorkspace() {
  const { status, user } = useSession()
  const location = useLocation()
  if (status === 'authenticating') return <LoadingState label="Verifying your account…" />
  if (status !== 'authenticated' || !user) return <Navigate to="/login" state={{ from: location.pathname }} replace />
  return <TenantProvider key={user.userId}><Outlet /></TenantProvider>
}
export default function App() {
  return <Routes>
    <Route path="/login" element={<LoginPage />} />
    <Route element={<ProtectedWorkspace />}><Route element={<AppLayout />}>
      <Route path="/dashboard" element={<DashboardPage />} />
      <Route element={<WorkScope />}>
        <Route path="/projects" element={<ProjectsPage />} />
        <Route path="/tasks" element={<TasksPage />} />
        <Route path="/projects/:projectId/tasks" element={<TasksPage />} />
      </Route>
      <Route path="/team" element={<WorkspacePage section="team" />} />
      <Route path="/settings" element={<SettingsPage />} />
    </Route></Route>
    <Route path="*" element={<Navigate to="/dashboard" replace />} />
  </Routes>
}
