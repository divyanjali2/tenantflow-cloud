import { useCallback } from 'react'
import { Bar, BarChart, CartesianGrid, Pie, PieChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { CheckCircle2, CheckSquare2, Clock3, FolderKanban, RefreshCw, TrendingUp, UsersRound } from 'lucide-react'
import { getDashboardSummary } from '../api/dashboard'
import type { DashboardSummary } from '../api/dashboard'
import { useResource } from '../hooks/useResource'
import { EmptyState, ErrorState } from './States'

export function DashboardSkeleton() {
  return <section className="dashboard-skeleton" role="status" aria-label="Loading dashboard statistics" aria-busy="true"><span className="sr-only">Loading dashboard statistics…</span><div className="analytics-stats">{Array.from({ length: 7 }, (_, index) => <div className="panel skeleton-stat" key={index}><span /><span /><span /></div>)}</div><div className="analytics-charts"><div className="panel skeleton-chart" /><div className="panel skeleton-chart" /></div></section>
}

export function DashboardAnalytics({ tenantId }: { tenantId: string }) {
  const load = useCallback((signal: AbortSignal) => getDashboardSummary(tenantId, signal), [tenantId])
  const { state, reload } = useResource(`${tenantId}:dashboard`, load)
  return <section className="dashboard-analytics" aria-label="Workspace analytics">
    <div className="section-heading"><h2>Workspace at a glance</h2><button className="button-secondary" disabled={state.status === 'loading'} onClick={reload}><RefreshCw size={14} aria-hidden="true" />Refresh statistics</button></div>
    {state.status === 'loading' ? <DashboardSkeleton /> : state.status === 'error' ? <ErrorState title="We couldn’t load dashboard statistics" message={state.message} onRetry={reload} /> : <SummaryContent summary={state.data} />}
  </section>
}

function SummaryContent({ summary }: { summary: DashboardSummary }) {
  const percentage = new Intl.NumberFormat(undefined, { maximumFractionDigits: 2 }).format(summary.taskCompletionPercentage)
  const stats = [
    { label: 'Total Projects', value: summary.totalProjects.toLocaleString(), icon: FolderKanban, color: 'stat-purple' },
    { label: 'Active Projects', value: summary.activeProjects.toLocaleString(), icon: TrendingUp, color: 'stat-teal' },
    { label: 'Total Tasks', value: summary.totalTasks.toLocaleString(), icon: CheckSquare2, color: 'stat-purple' },
    { label: 'Completed Tasks', value: summary.completedTasks.toLocaleString(), icon: CheckCircle2, color: 'stat-teal' },
    { label: 'Team Members', value: summary.totalMembers.toLocaleString(), icon: UsersRound, color: 'stat-purple' },
    { label: 'Overdue Tasks', value: summary.overdueTasks.toLocaleString(), icon: Clock3, color: 'stat-amber' },
    { label: 'Task Completion', value: `${percentage}%`, icon: TrendingUp, color: 'stat-teal' },
  ]
  const taskData = [
    { name: 'To Do', value: summary.todoTasks, fill: '#94a3b8' },
    { name: 'In Progress', value: summary.inProgressTasks, fill: '#c69b42' },
    { name: 'Completed', value: summary.completedTasks, fill: '#28745b' },
  ]
  const projectData = [
    { name: 'Active', value: summary.activeProjects },
    { name: 'Completed', value: summary.completedProjects },
    { name: 'Other', value: summary.totalProjects - summary.activeProjects - summary.completedProjects },
  ]
  return <>
    <div className="analytics-stats">{stats.map(({ label, value, icon: Icon, color }) => <article className="stat-card" aria-label={label} key={label}><span className={`stat-icon ${color}`}><Icon size={18} aria-hidden="true" /></span><p>{label}</p><strong>{value}</strong></article>)}</div>
    {summary.totalProjects === 0 && summary.totalTasks === 0 && <div className="mt-5"><EmptyState title="Your workspace is ready to grow"><p>No projects or tasks yet. Create a project to start tracking your team’s progress.</p></EmptyState></div>}
    <div className="analytics-charts">
      <section className="panel analytics-chart" aria-label="Task status report"><h3>Task status</h3><p className="chart-caption">How your work is moving forward.</p>
        {summary.totalTasks === 0 ? <p className="chart-empty">No tasks to chart yet.</p> : <div className="chart-canvas" aria-hidden="true"><ResponsiveContainer width="100%" height="100%" initialDimension={{ width: 320, height: 240 }}><PieChart><Pie data={taskData} dataKey="value" nameKey="name" innerRadius={65} outerRadius={90} paddingAngle={2} isAnimationActive={false} /><Tooltip /></PieChart></ResponsiveContainer></div>}
        <dl className="chart-values">{taskData.map((item) => <div key={item.name}><dt><span style={{ backgroundColor: item.fill }} />{item.name}</dt><dd>{item.value.toLocaleString()}</dd></div>)}</dl>
      </section>
      <section className="panel analytics-chart" aria-label="Project status report"><h3>Project status</h3><p className="chart-caption">Other includes Planning, On Hold, and Cancelled.</p>
        {summary.totalProjects === 0 ? <p className="chart-empty">No projects to chart yet.</p> : <div className="chart-canvas" aria-hidden="true"><ResponsiveContainer width="100%" height="100%" initialDimension={{ width: 320, height: 240 }}><BarChart data={projectData} margin={{ top: 10, right: 10, left: -20, bottom: 0 }}><CartesianGrid strokeDasharray="3 3" vertical={false} /><XAxis dataKey="name" tick={{ fontSize: 11 }} /><YAxis allowDecimals={false} tick={{ fontSize: 11 }} /><Tooltip /><Bar dataKey="value" name="Projects" fill="#407b62" radius={[5, 5, 0, 0]} isAnimationActive={false} /></BarChart></ResponsiveContainer></div>}
        <dl className="chart-values">{projectData.map((item) => <div key={item.name}><dt>{item.name}</dt><dd>{item.value.toLocaleString()}</dd></div>)}</dl>
      </section>
    </div>
    <section className="panel completion-panel" aria-label="Task completion progress"><div><h3>Task completion</h3><p>{summary.completedTasks.toLocaleString()} of {summary.totalTasks.toLocaleString()} tasks completed</p></div><strong>{percentage}%</strong><progress aria-label="Task completion percentage" max={100} value={summary.taskCompletionPercentage} /><p className="completion-note">Overdue tasks have a due date before the current UTC time and are not completed.</p></section>
  </>
}
