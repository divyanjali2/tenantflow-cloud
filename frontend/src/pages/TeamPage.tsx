import { useCallback, useState } from 'react'
import { LockKeyhole, Search, ShieldCheck, UserPlus, UserRound, UserRoundMinus } from 'lucide-react'
import { Link } from 'react-router-dom'
import { teamApi } from '../api/team'
import type { TenantMember } from '../api/work'
import { useSession } from '../auth/useSession'
import { AddMemberDialog, ChangeMemberRoleDialog, RemoveMemberDialog } from '../components/MemberDialogs'
import { EmptyState, ErrorState, LoadingState, PageHeader } from '../components/States'
import { WorkFeedback } from '../components/WorkFeedback'
import { useResource } from '../hooks/useResource'
import { useTenants } from '../tenants/context'

export function TeamPage() {
  const { selectedTenant } = useTenants()
  // The member directory endpoint is explicitly restricted to Owner and Admin.
  if (selectedTenant?.role !== 'Owner' && selectedTenant?.role !== 'Admin') return <>
    <PageHeader eyebrow="YOUR WORKSPACE" title="Team" description="The people behind the progress." />
    <EmptyState title="Team directory access is limited"><p>Only workspace Owners and Admins can view the member directory. You can continue working with your team’s projects and tasks.</p><Link className="button-secondary mt-4" to="/projects">View projects</Link></EmptyState>
  </>
  return <TeamDirectory />
}

type MemberDialog = { type: 'add' } | { type: 'role' | 'remove'; member: TenantMember } | null

function TeamDirectory() {
  const { selectedTenant } = useTenants()
  const { user } = useSession()
  const tenantId = selectedTenant!.tenantId
  const isOwner = selectedTenant!.role === 'Owner'
  const load = useCallback((signal: AbortSignal) => teamApi.members(tenantId, signal), [tenantId])
  const { state, reload } = useResource(`${tenantId}:team`, load)
  const [search, setSearch] = useState('')
  const [dialog, setDialog] = useState<MemberDialog>(null)
  const [notice, setNotice] = useState<string | null>(null)
  function saved(message: string) { setDialog(null); setNotice(message); reload() }
  const query = search.trim().toLowerCase()
  const members = state.status === 'success' ? state.data.filter((member) => member.email.toLowerCase().includes(query)) : []
  return <>
    <div className="work-page-header"><PageHeader eyebrow="YOUR WORKSPACE" title="Team" description="The people behind the progress. Manage your shared workspace access." />{isOwner && <button className="button-primary" onClick={() => { setNotice(null); setDialog({ type: 'add' }) }}><UserPlus size={17} aria-hidden="true" />Add Member</button>}</div>
    <WorkFeedback message={notice} />
    {!isOwner && <p className="team-access-note"><ShieldCheck size={16} aria-hidden="true" />You have view-only access to the team directory. Only an Owner can manage membership.</p>}
    <div className="team-search-bar"><div className="team-search"><Search size={17} aria-hidden="true" /><label className="sr-only" htmlFor="team-search">Search members by email</label><input className="field" id="team-search" type="search" placeholder="Search by email…" value={search} onChange={(event) => setSearch(event.target.value)} /></div><button className="button-secondary" onClick={reload}>Refresh members</button></div>
    {state.status === 'loading' ? <LoadingState label="Loading team members…" /> : state.status === 'error' ? <ErrorState title="We couldn’t load team members" message={state.message} onRetry={reload} /> : state.data.length === 0 ?
      <EmptyState title="No team members to display"><p>{isOwner ? 'Add an existing registered user to bring them into this workspace.' : 'No member records were returned for this workspace.'}</p></EmptyState> : members.length === 0 ?
      <EmptyState title="No members match your search"><p>Try another email address.</p><button className="button-secondary mt-4" onClick={() => setSearch('')}>Clear search</button></EmptyState> : <>
      <div className="list-summary"><span>{query ? `${members.length} of ${state.data.length}` : state.data.length} {state.data.length === 1 ? 'member' : 'members'} · {selectedTenant!.name}</span><span className="team-scroll-hint">Scroll the table to see all details</span></div>
      <div className="panel team-table-wrap" role="region" aria-label="Team members table" tabIndex={0}><table className="team-table"><caption className="sr-only">Members of {selectedTenant!.name}</caption><thead><tr><th scope="col">Member</th><th scope="col">User ID</th><th scope="col">Tenant role</th><th scope="col">Available actions</th></tr></thead><tbody>{members.map((member) => {
        const canManage = isOwner && member.role !== 'Owner' && member.userId !== user?.userId
        return <tr key={member.userId}><th scope="row"><div className="team-member-identity"><span className="team-avatar" aria-hidden="true"><UserRound size={18} /></span><span>{member.email}{member.userId === user?.userId && <span className="team-you">You</span>}</span></div></th><td><span className="team-user-id">{member.userId}</span></td><td><span className={`work-badge role-${member.role.toLowerCase()}`}>{member.role}</span></td><td>{canManage ? <div className="team-actions"><button className="button-secondary" aria-label={`Change role for ${member.email}`} onClick={() => { setNotice(null); setDialog({ type: 'role', member }) }}>Change role</button><button className="team-remove" aria-label={`Remove ${member.email}`} onClick={() => { setNotice(null); setDialog({ type: 'remove', member }) }}><UserRoundMinus size={15} aria-hidden="true" />Remove</button></div> : <span className="team-protected">{member.role === 'Owner' ? <><LockKeyhole size={13} aria-hidden="true" />Owner protected</> : 'View only'}</span>}</td></tr>
      })}</tbody></table></div>
    </>}
    {isOwner && dialog?.type === 'add' && <AddMemberDialog tenantId={tenantId} onClose={() => setDialog(null)} onSaved={() => saved('Member added. Workspace access is active immediately.')} />}
    {isOwner && dialog?.type === 'role' && <ChangeMemberRoleDialog tenantId={tenantId} member={dialog.member} onClose={() => setDialog(null)} onSaved={() => saved('Member role updated.')} />}
    {isOwner && dialog?.type === 'remove' && <RemoveMemberDialog tenantId={tenantId} member={dialog.member} onClose={() => setDialog(null)} onRemoved={() => saved('Member removed from this workspace.')} />}
  </>
}
