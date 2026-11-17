import { useCallback, useState } from 'react'
import { ArrowUpRight, CalendarDays, FolderKanban, Pencil, Plus, Trash2 } from 'lucide-react'
import { Link } from 'react-router-dom'
import { canManageWork, displayDate, statusLabel, workApi } from '../api/work'
import type { Project } from '../api/work'
import { DeleteConfirmation } from '../components/Modal'
import { ProjectForm } from '../components/ProjectForm'
import { EmptyState, ErrorState, LoadingState, PageHeader } from '../components/States'
import { WorkFeedback } from '../components/WorkFeedback'
import { useResource } from '../hooks/useResource'
import { useTenants } from '../tenants/context'

type ProjectDialog = { type: 'create' } | { type: 'edit' | 'delete'; project: Project } | null

export function ProjectsPage() {
  const { selectedTenant } = useTenants()
  const tenantId = selectedTenant!.tenantId
  const role = selectedTenant!.role
  const loader = useCallback((signal: AbortSignal) => workApi.projects(tenantId, signal), [tenantId])
  const { state, reload } = useResource(tenantId, loader)
  const [dialog, setDialog] = useState<ProjectDialog>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const canWrite = canManageWork(role)
  function saved(message: string) { setDialog(null); setNotice(message); reload() }
  return <>
    <div className="work-page-header"><PageHeader eyebrow="YOUR WORKSPACE" title="Projects" description="A shared home for your team’s next big idea." />{canWrite && <button className="button-primary" onClick={() => setDialog({ type: 'create' })}><Plus size={17} />Create project</button>}</div>
    <WorkFeedback message={notice} />
    {state.status === 'loading' ? <LoadingState label="Loading projects…" /> : state.status === 'error' ? <ErrorState title="We couldn’t load projects" message={state.message} onRetry={reload} /> : state.data.length === 0 ?
      <EmptyState title="A fresh start for your next idea"><p>{canWrite ? 'Create a project to bring your team’s work together.' : 'No projects yet. A workspace Owner or Admin can create one.'}</p></EmptyState> : <>
        <div className="list-summary"><span>{state.data.length} {state.data.length === 1 ? 'project' : 'projects'}</span><button className="text-link m-0" onClick={reload}>Refresh projects</button></div>
        <div className="project-grid">{state.data.map((project) => <article className="panel project-card" key={project.id}>
          <div className="flex items-center justify-between gap-3"><span className="shortcut-icon shortcut-purple"><FolderKanban size={23} aria-hidden="true" /></span><span className={`work-badge badge-${project.status.toLowerCase()}`}>{statusLabel(project.status)}</span></div>
          <h2><Link to={`/projects/${project.id}/tasks`}>{project.name}</Link></h2><p className="project-description">{project.description || 'No description yet.'}</p>
          <p className="project-created"><CalendarDays size={14} aria-hidden="true" />Created {displayDate(project.createdAtUtc)}</p>
          <div className="project-actions"><Link className="text-link m-0" to={`/projects/${project.id}/tasks`}>Open task board <ArrowUpRight size={15} aria-hidden="true" /></Link><div className="flex gap-1">
            {canWrite && <button className="icon-button" aria-label={`Edit project ${project.name}`} onClick={() => setDialog({ type: 'edit', project })}><Pencil size={16} /></button>}
            {role === 'Owner' && <button className="icon-button text-red-700" aria-label={`Delete project ${project.name}`} onClick={() => setDialog({ type: 'delete', project })}><Trash2 size={16} /></button>}
          </div></div>
        </article>)}</div>
      </>}
    {dialog?.type === 'create' && <ProjectForm tenantId={tenantId} onClose={() => setDialog(null)} onSaved={() => saved('Project created.')} />}
    {dialog?.type === 'edit' && <ProjectForm tenantId={tenantId} project={dialog.project} onClose={() => setDialog(null)} onSaved={() => saved('Project updated.')} />}
    {dialog?.type === 'delete' && <DeleteConfirmation kind="project" name={dialog.project.name} onClose={() => setDialog(null)} onDelete={(signal) => workApi.deleteProject(tenantId, dialog.project.id, signal)} onDeleted={() => saved('Project deleted.')} />}
  </>
}
