import { useState } from 'react'
import type { FormEvent } from 'react'
import { teamApi, teamErrorMessage } from '../api/team'
import type { AssignableTenantRole } from '../api/team'
import type { TenantMember } from '../api/work'
import { useMutation } from '../hooks/useMutation'
import { Modal } from './Modal'
import { WorkFeedback } from './WorkFeedback'

export function AddMemberDialog({ tenantId, onClose, onSaved }: { tenantId: string; onClose: () => void; onSaved: () => void }) {
  const [email, setEmail] = useState('')
  const [role, setRole] = useState<AssignableTenantRole>('Member')
  const [validation, setValidation] = useState<string | null>(null)
  const mutation = useMutation((error) => teamErrorMessage(error, 'add'))
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!email.trim()) { setValidation('Enter the registered user’s email address.'); return }
    setValidation(null)
    void mutation.run((signal) => teamApi.add(tenantId, { email: email.trim(), role }, signal), onSaved)
  }
  return <Modal title="Add Member" busy={mutation.busy} onClose={onClose}>
    <p className="dialog-description" id="add-member-description">Add an existing registered user to this workspace immediately. No email invitation is sent.</p>
    <form className="work-form mt-5" onSubmit={submit} aria-describedby="add-member-description">
      <WorkFeedback message={validation ?? mutation.error} error />
      <fieldset className="form-fields" disabled={mutation.busy}>
        <div><label className="field-label" htmlFor="member-email">Registered user’s email</label><input className="field" id="member-email" type="email" autoComplete="email" autoFocus required value={email} onChange={(event) => setEmail(event.target.value)} /></div>
        <div><label className="field-label" htmlFor="new-member-role">Tenant role</label><select className="field" id="new-member-role" value={role} onChange={(event) => setRole(event.target.value as AssignableTenantRole)}><option value="Member">Member</option><option value="Admin">Admin</option></select><p className="field-hint">Admins can manage projects and tasks. Members can view work and update their assigned task status.</p></div>
      </fieldset>
      <div className="dialog-actions"><button type="button" className="button-secondary" disabled={mutation.busy} onClick={onClose}>Cancel</button><button type="submit" className="button-primary" disabled={mutation.busy}>{mutation.busy ? 'Adding…' : 'Add Member'}</button></div>
    </form>
  </Modal>
}

export function ChangeMemberRoleDialog({ tenantId, member, onClose, onSaved }: { tenantId: string; member: TenantMember; onClose: () => void; onSaved: () => void }) {
  const [role, setRole] = useState<AssignableTenantRole>(member.role === 'Admin' ? 'Admin' : 'Member')
  const mutation = useMutation((error) => teamErrorMessage(error, 'role'))
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (member.role === 'Owner') return
    void mutation.run((signal) => teamApi.changeRole(tenantId, member.userId, { role }, signal), onSaved)
  }
  return <Modal title="Change member role" busy={mutation.busy} onClose={onClose}>
    <p className="dialog-description">Update workspace access for <strong>{member.email}</strong>.</p>
    <form className="work-form mt-5" onSubmit={submit}>
      <WorkFeedback message={mutation.error} error />
      <label className="field-label" htmlFor="change-member-role">Tenant role</label><select autoFocus className="field" id="change-member-role" disabled={mutation.busy} value={role} onChange={(event) => setRole(event.target.value as AssignableTenantRole)}><option value="Admin">Admin</option><option value="Member">Member</option></select>
      <div className="dialog-actions"><button type="button" className="button-secondary" disabled={mutation.busy} onClick={onClose}>Cancel</button><button type="submit" className="button-primary" disabled={mutation.busy || member.role === 'Owner' || role === member.role}>{mutation.busy ? 'Saving…' : 'Save role'}</button></div>
    </form>
  </Modal>
}

export function RemoveMemberDialog({ tenantId, member, onClose, onRemoved }: { tenantId: string; member: TenantMember; onClose: () => void; onRemoved: () => void }) {
  const mutation = useMutation((error) => teamErrorMessage(error, 'remove'))
  return <Modal title="Remove member?" busy={mutation.busy} onClose={onClose}>
    <p className="dialog-description">Remove <strong>{member.email}</strong> from this workspace? They will lose access here. Their registered account and memberships in other workspaces will be preserved.</p>
    <WorkFeedback message={mutation.error} error />
    <div className="dialog-actions"><button type="button" className="button-secondary" autoFocus disabled={mutation.busy} onClick={onClose}>Cancel</button><button type="button" className="button-danger" disabled={mutation.busy || member.role === 'Owner'} onClick={() => { if (member.role !== 'Owner') void mutation.run((signal) => teamApi.remove(tenantId, member.userId, signal), onRemoved) }}>{mutation.busy ? 'Removing…' : 'Remove member'}</button></div>
  </Modal>
}
