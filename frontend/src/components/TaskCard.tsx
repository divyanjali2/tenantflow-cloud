import { CalendarDays, Pencil, Trash2, UserRound } from 'lucide-react'
import { displayDate, statusLabel, taskStatuses } from '../api/work'
import type { ProjectTask, TaskStatus, TenantMember } from '../api/work'

export function TaskCard({ task, members, currentUserId, canWrite, membersReady, busy, onEdit, onDelete, onStatus, onAssignee }: {
  task: ProjectTask; members: TenantMember[]; currentUserId: string; canWrite: boolean; membersReady: boolean; busy: boolean
  onEdit: () => void; onDelete: () => void; onStatus: (status: TaskStatus) => void; onAssignee: (id: string | null) => void
}) {
  const assignedMember = members.find((member) => member.userId === task.assignedToUserId)
  const assigneeLabel = !task.assignedToUserId ? 'Unassigned' : assignedMember?.email ?? (task.assignedToUserId === currentUserId ? 'You' : `User ${task.assignedToUserId.slice(0, 8)}`)
  const canChangeStatus = canWrite || task.assignedToUserId === currentUserId
  return <article className="task-card" aria-label={`Task: ${task.title}`}>
    <div className="flex items-center justify-between gap-2"><span className={`work-badge priority-${task.priority.toLowerCase()}`}>{task.priority} priority</span>{canWrite && <div className="flex gap-1"><button className="icon-button" aria-label={`Edit task ${task.title}`} disabled={busy} onClick={onEdit}><Pencil size={14} /></button><button className="icon-button text-red-700" aria-label={`Delete task ${task.title}`} disabled={busy} onClick={onDelete}><Trash2 size={14} /></button></div>}</div>
    <h3>{task.title}</h3>{task.description && <p className="task-description">{task.description}</p>}
    <div className="task-meta"><span title={task.assignedToUserId ?? undefined}><UserRound size={14} aria-hidden="true" />{assigneeLabel}</span><span><CalendarDays size={14} aria-hidden="true" />{task.dueDateUtc ? `Due ${displayDate(task.dueDateUtc)}` : 'No due date'}</span></div>
    {canChangeStatus ? <div className="card-field"><label htmlFor={`status-${task.id}`}>Status</label><select id={`status-${task.id}`} aria-label={`Status for ${task.title}`} value={task.status} disabled={busy} onChange={(event) => onStatus(event.target.value as TaskStatus)}>{taskStatuses.map((value) => <option key={value} value={value}>{statusLabel(value)}</option>)}</select></div> : <p className="task-read-status">{statusLabel(task.status)}</p>}
    {canWrite && <div className="card-field"><label htmlFor={`assignee-${task.id}`}>Assignee</label><select id={`assignee-${task.id}`} aria-label={`Assignee for ${task.title}`} value={task.assignedToUserId ?? ''} disabled={busy || !membersReady} onChange={(event) => onAssignee(event.target.value || null)}><option value="">Unassigned</option>{task.assignedToUserId && !assignedMember && <option value={task.assignedToUserId}>{assigneeLabel}</option>}{members.map((member) => <option key={member.userId} value={member.userId}>{member.email}</option>)}</select></div>}
  </article>
}
