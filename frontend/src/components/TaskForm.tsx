import { useState } from 'react'
import type { FormEvent } from 'react'
import { priorities, statusLabel, taskStatuses, workApi } from '../api/work'
import type { Priority, ProjectTask, TaskStatus, TenantMember } from '../api/work'
import { useMutation } from '../hooks/useMutation'
import { Modal } from './Modal'
import { WorkFeedback } from './WorkFeedback'

function localDateInput(value: string | null | undefined) {
  if (!value) return ''
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '' : new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16)
}

export function TaskForm({ tenantId, projectId, task, members, membersReady, onClose, onSaved }: {
  tenantId: string; projectId: string; task?: ProjectTask; members: TenantMember[]; membersReady: boolean
  onClose: () => void; onSaved: () => void
}) {
  const [title, setTitle] = useState(task?.title ?? '')
  const [description, setDescription] = useState(task?.description ?? '')
  const [status, setStatus] = useState<TaskStatus>(task?.status ?? 'ToDo')
  const [priority, setPriority] = useState<Priority>(task?.priority ?? 'Medium')
  const [assignee, setAssignee] = useState(task?.assignedToUserId ?? '')
  const initialDueDate = localDateInput(task?.dueDateUtc)
  const [dueDate, setDueDate] = useState(initialDueDate)
  const [validation, setValidation] = useState<string | null>(null)
  const mutation = useMutation()
  const missingAssignee = assignee && !members.some((member) => member.userId === assignee)
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!title.trim()) { setValidation('Task title cannot be blank.'); return }
    if (dueDate && !Number.isFinite(new Date(dueDate).getTime())) { setValidation('Enter a valid due date and time.'); return }
    setValidation(null)
    const input = {
      title: title.trim(), description: description || null, status, priority, assignedToUserId: assignee || null,
      // Preserve original seconds/precision when the due date field has not been edited.
      dueDateUtc: dueDate === initialDueDate ? task?.dueDateUtc ?? null : dueDate ? new Date(dueDate).toISOString() : null,
    }
    void mutation.run((signal) => task ? workApi.updateTask(tenantId, projectId, task.id, input, signal) : workApi.createTask(tenantId, projectId, input, signal), onSaved)
  }
  return <Modal title={task ? 'Edit task' : 'Create task'} busy={mutation.busy} onClose={onClose}>
    <form className="work-form" onSubmit={submit}>
      <WorkFeedback message={validation ?? mutation.error} error />
      <fieldset className="form-fields" disabled={mutation.busy}>
        <div><label className="field-label" htmlFor="task-title">Task title</label><input autoFocus className="field" id="task-title" required maxLength={200} value={title} onChange={(event) => setTitle(event.target.value)} /></div>
        <div><label className="field-label" htmlFor="task-description">Description <span>(optional)</span></label><textarea className="field" id="task-description" rows={3} value={description} onChange={(event) => setDescription(event.target.value)} /></div>
        <div className="form-columns"><div><label className="field-label" htmlFor="task-status">Task status</label><select className="field" id="task-status" value={status} onChange={(event) => setStatus(event.target.value as TaskStatus)}>{taskStatuses.map((value) => <option key={value} value={value}>{statusLabel(value)}</option>)}</select></div>
          <div><label className="field-label" htmlFor="task-priority">Priority</label><select className="field" id="task-priority" value={priority} onChange={(event) => setPriority(event.target.value as Priority)}>{priorities.map((value) => <option key={value}>{value}</option>)}</select></div></div>
        <div><label className="field-label" htmlFor="task-assignee">Assignee</label><select className="field" id="task-assignee" value={assignee} disabled={!membersReady} onChange={(event) => setAssignee(event.target.value)}><option value="">Unassigned</option>{missingAssignee && <option value={assignee}>Current assignee ({assignee})</option>}{members.map((member) => <option key={member.userId} value={member.userId}>{member.email}</option>)}</select>{!membersReady && <p className="field-hint">Assignees are unavailable. The current assignment will be preserved.</p>}</div>
        <div><label className="field-label" htmlFor="task-due">Due date and time <span>(optional)</span></label><input className="field" id="task-due" type="datetime-local" value={dueDate} onChange={(event) => setDueDate(event.target.value)} /><p className="field-hint">Your local time. Saved as UTC.</p></div>
      </fieldset>
      <div className="dialog-actions"><button type="button" className="button-secondary" disabled={mutation.busy} onClick={onClose}>Cancel</button><button className="button-primary" disabled={mutation.busy}>{mutation.busy ? 'Saving…' : task ? 'Save task' : 'Create task'}</button></div>
    </form>
  </Modal>
}
