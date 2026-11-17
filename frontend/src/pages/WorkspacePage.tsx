import { CheckSquare2, FolderKanban, UsersRound } from 'lucide-react'
import { Link } from 'react-router-dom'
import { EmptyState, ErrorState, LoadingState, PageHeader } from '../components/States'
import { useTenants } from '../tenants/context'

const pages = {
  projects: { title: 'Projects', description: 'A shared home for your team’s next big idea.', icon: FolderKanban, message: 'Project management is up next.', detail: 'Your workspace is ready. Browsing and managing projects will be available in the next frontend step.' },
  tasks: { title: 'Tasks', description: 'Clarity on what matters. Space to make progress.', icon: CheckSquare2, message: 'Your task workspace is taking shape.', detail: 'Task lists, assignment, and filters will be connected in the next frontend step.' },
  team: { title: 'Team', description: 'The people behind the progress.', icon: UsersRound, message: 'A place for everyone on your team.', detail: 'Team management will be connected in a future frontend step.' },
}
export function WorkspacePage({ section }: { section: keyof typeof pages }) {
  const page = pages[section]
  const { resource, selectedTenant, reload } = useTenants()
  const Icon = page.icon
  return <><PageHeader eyebrow="YOUR WORKSPACE" title={page.title} description={page.description} />
    {resource.status === 'loading' ? <LoadingState /> : resource.status === 'error' ? <ErrorState message={resource.message} onRetry={reload} /> : !selectedTenant ? <EmptyState title="No workspace selected"><p>Ask a workspace owner to invite you, then refresh your workspace list on the overview page.</p><Link to="/dashboard" className="text-link">Back to overview</Link></EmptyState> :
      <section className="feature-placeholder"><span className="feature-icon"><Icon size={30} strokeWidth={1.5} /></span><span className="phase-badge">COMING NEXT</span><h2>{page.message}</h2><p>{page.detail}</p><span className="placeholder-tenant">Workspace: {selectedTenant.name}</span><Link to="/dashboard" className="button-secondary">Back to overview</Link></section>}
  </>
}
