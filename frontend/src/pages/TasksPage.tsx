import { useCallback, useEffect, useState } from 'react'
import { ArrowLeft, ChevronLeft, ChevronRight, Plus, Search } from 'lucide-react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { canManageWork, priorities, statusLabel, taskStatuses, workApi } from '../api/work'
import type { Priority, Project, ProjectTask, TaskFilters, TaskStatus } from '../api/work'
import { useSession } from '../auth/useSession'
import { DeleteConfirmation } from '../components/Modal'
import { EmptyState, ErrorState, LoadingState, PageHeader } from '../components/States'
import { TaskCard } from '../components/TaskCard'
import { TaskForm } from '../components/TaskForm'
import { WorkFeedback } from '../components/WorkFeedback'
import { useMutation } from '../hooks/useMutation'
import { useResource } from '../hooks/useResource'
import { useTenants } from '../tenants/context'

export function TasksPage() {
  const { selectedTenant } = useTenants()
  const tenantId = selectedTenant!.tenantId
  const { projectId } = useParams<{ projectId: string }>()
  const navigate = useNavigate()
  const loadProjects = useCallback((signal: AbortSignal) => workApi.projects(tenantId, signal), [tenantId])
  const projects = useResource(tenantId, loadProjects)
  if (projects.state.status === 'loading') return <LoadingState label="Loading projects…" />
  if (projects.state.status === 'error') return <ErrorState title="We couldn’t load projects" message={projects.state.message} onRetry={projects.reload} />
  const items = projects.state.data
  // An old/cross-tenant route ID never triggers a task request in the new workspace.
  const selected = projectId ? items.find((project) => project.id === projectId) : items[0]
  return <>
    <PageHeader eyebrow="YOUR WORKSPACE" title="Task board" description="Clarity on what matters. Space to make progress." />
    {items.length === 0 ? <EmptyState title="Your task board starts with a project"><p>Create a project first, then add tasks to plan your team’s work.</p><Link className="button-secondary mt-4" to="/projects">View projects</Link></EmptyState> : <>
      <div className="board-project-bar"><Link to="/projects" className="text-link m-0"><ArrowLeft size={15} />All projects</Link><div><label className="field-label" htmlFor="board-project">Project</label><select className="field" id="board-project" value={selected?.id ?? ''} onChange={(event) => navigate(`/projects/${event.target.value}/tasks`)}>{!selected && <option value="" disabled>Select a project in this workspace</option>}{items.map((project) => <option key={project.id} value={project.id}>{project.name}</option>)}</select></div></div>
      {selected ? <TaskBoard key={selected.id} project={selected} /> : <EmptyState title="Project not available in this workspace"><p>Select a project above to view its task board.</p></EmptyState>}
    </>}
  </>
}

type TaskDialog = { type: 'create' } | { type: 'edit' | 'delete'; task: ProjectTask } | null

function TaskBoard({ project }: { project: Project }) {
  const { selectedTenant } = useTenants()
  const tenantId = selectedTenant!.tenantId
  const canWrite = canManageWork(selectedTenant!.role)
  const { user } = useSession()
  const [filters, setFilters] = useState<TaskFilters>({ page: 1, pageSize: 20 })
  const [searchDraft, setSearchDraft] = useState('')
  const [dialog, setDialog] = useState<TaskDialog>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const mutation = useMutation()
  const loadTasks = useCallback((signal: AbortSignal) => workApi.tasks(tenantId, project.id, filters, signal), [tenantId, project.id, filters])
  const tasks = useResource(`${tenantId}:${project.id}:${JSON.stringify(filters)}`, loadTasks)
  const loadMembers = useCallback((signal: AbortSignal) => canWrite ? workApi.members(tenantId, signal) : Promise.resolve([]), [tenantId, canWrite])
  const memberResource = useResource(`${tenantId}:members:${canWrite}`, loadMembers)
  const members = memberResource.state.status === 'success' ? memberResource.state.data : []
  const membersReady = memberResource.state.status === 'success'
  useEffect(() => {
    if (tasks.state.status === 'success' && filters.page > Math.max(1, tasks.state.data.totalPages))
      setFilters((previous) => ({ ...previous, page: Math.max(1, tasks.state.status === 'success' ? tasks.state.data.totalPages : 1) }))
  }, [tasks.state, filters.page])
  function filter(patch: Partial<TaskFilters>) { setFilters((previous) => ({ ...previous, ...patch, page: 1 })) }
  function saved(message: string) { setDialog(null); setNotice(message); tasks.reload() }
  function patchTask(action: (signal: AbortSignal) => Promise<unknown>, message: string) {
    setNotice(null)
    void mutation.run(action, () => saved(message))
  }
  const filtered = !!(filters.search || filters.status || filters.priority)
  return <section aria-label={`Task board for ${project.name}`}>
    <div className="board-heading"><div><h2>{project.name}</h2><p>One shared view of the work ahead.</p></div>{canWrite && <button className="button-primary" disabled={mutation.busy} onClick={() => setDialog({ type: 'create' })}><Plus size={17} />Create task</button>}</div>
    <WorkFeedback message={notice} /><WorkFeedback message={mutation.error} error />
    {canWrite && memberResource.state.status === 'error' && <div className="members-warning"><WorkFeedback message={`Assignees could not be loaded. ${memberResource.state.message}`} error /><button className="button-secondary" onClick={memberResource.reload}>Retry assignees</button></div>}
    <div className="task-filters"><form className="task-search" onSubmit={(event) => { event.preventDefault(); filter({ search: searchDraft.trim() || undefined }) }}><label className="sr-only" htmlFor="task-search">Search tasks</label><Search size={16} aria-hidden="true" /><input id="task-search" placeholder="Search task titles…" value={searchDraft} onChange={(event) => setSearchDraft(event.target.value)} /><button className="button-secondary" type="submit">Search</button></form>
      <div><label className="field-label" htmlFor="filter-status">Filter by status</label><select className="field" id="filter-status" value={filters.status ?? ''} onChange={(event) => filter({ status: event.target.value ? event.target.value as TaskStatus : undefined })}><option value="">All statuses</option>{taskStatuses.map((value) => <option key={value} value={value}>{statusLabel(value)}</option>)}</select></div>
      <div><label className="field-label" htmlFor="filter-priority">Filter by priority</label><select className="field" id="filter-priority" value={filters.priority ?? ''} onChange={(event) => filter({ priority: event.target.value ? event.target.value as Priority : undefined })}><option value="">All priorities</option>{priorities.map((value) => <option key={value}>{value}</option>)}</select></div>
      {filtered && <button className="text-link m-0" onClick={() => { setSearchDraft(''); setFilters({ page: 1, pageSize: filters.pageSize }) }}>Clear filters</button>}
    </div>
    {tasks.state.status === 'loading' ? <LoadingState label="Loading tasks…" /> : tasks.state.status === 'error' ? <ErrorState title="We couldn’t load tasks" message={tasks.state.message} onRetry={tasks.reload} /> : <>
      <div className="list-summary"><span>{tasks.state.data.totalCount} {tasks.state.data.totalCount === 1 ? 'task' : 'tasks'}{filtered ? ' matching filters' : ''} · Column counts show this page</span><button className="text-link m-0" onClick={tasks.reload}>Refresh tasks</button></div>
      {tasks.state.data.totalCount === 0 && <p className="board-empty" role="status">{filtered ? 'No tasks match these filters.' : 'No tasks yet. Your next step starts here.'}</p>}
      <div className="kanban-board">{taskStatuses.map((status) => {
        const items = tasks.state.status === 'success' ? tasks.state.data.items.filter((task) => task.status === status) : []
        return <section className={`kanban-column column-${status.toLowerCase()}`} key={status} aria-label={`${statusLabel(status)} tasks`}><header><h3><span />{statusLabel(status)}</h3><span className="column-count">{items.length}</span></header><div className="kanban-items">{items.length === 0 ? <p className="column-empty">No tasks on this page</p> : items.map((task) => <TaskCard key={task.id} task={task} members={members} currentUserId={user!.userId} canWrite={canWrite} membersReady={membersReady} busy={mutation.busy}
          onEdit={() => setDialog({ type: 'edit', task })} onDelete={() => setDialog({ type: 'delete', task })}
          onStatus={(value) => patchTask((signal) => workApi.taskStatus(tenantId, project.id, task.id, value, signal), 'Task status updated.')}
          onAssignee={(id) => patchTask((signal) => workApi.taskAssignee(tenantId, project.id, task.id, id, signal), id ? 'Task assigned.' : 'Task unassigned.')} />)}</div></section>
      })}</div>
      <div className="pagination"><div><label htmlFor="task-page-size">Tasks per page</label><select id="task-page-size" value={filters.pageSize} onChange={(event) => filter({ pageSize: Number(event.target.value) })}>{[10, 20, 50, 100].map((size) => <option key={size}>{size}</option>)}</select></div><span>Page {tasks.state.data.page} of {Math.max(1, tasks.state.data.totalPages)}</span><div className="flex gap-2"><button className="button-secondary" aria-label="Previous page" disabled={filters.page <= 1} onClick={() => setFilters((previous) => ({ ...previous, page: previous.page - 1 }))}><ChevronLeft size={16} />Previous</button><button className="button-secondary" aria-label="Next page" disabled={filters.page >= tasks.state.data.totalPages} onClick={() => setFilters((previous) => ({ ...previous, page: previous.page + 1 }))}>Next<ChevronRight size={16} /></button></div></div>
    </>}
    {dialog?.type === 'create' && <TaskForm tenantId={tenantId} projectId={project.id} members={members} membersReady={membersReady} onClose={() => setDialog(null)} onSaved={() => saved('Task created.')} />}
    {dialog?.type === 'edit' && <TaskForm tenantId={tenantId} projectId={project.id} task={dialog.task} members={members} membersReady={membersReady} onClose={() => setDialog(null)} onSaved={() => saved('Task updated.')} />}
    {dialog?.type === 'delete' && <DeleteConfirmation kind="task" name={dialog.task.title} onClose={() => setDialog(null)} onDelete={(signal) => workApi.deleteTask(tenantId, project.id, dialog.task.id, signal)} onDeleted={() => saved('Task deleted.')} />}
  </section>
}
