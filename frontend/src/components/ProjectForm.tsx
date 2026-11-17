import { useState } from 'react'
import type { FormEvent } from 'react'
import { projectStatuses, statusLabel, workApi } from '../api/work'
import type { Project, ProjectStatus } from '../api/work'
import { useMutation } from '../hooks/useMutation'
import { Modal } from './Modal'
import { WorkFeedback } from './WorkFeedback'

export function ProjectForm({ tenantId, project, onClose, onSaved }: { tenantId: string; project?: Project; onClose: () => void; onSaved: () => void }) {
  const [name, setName] = useState(project?.name ?? '')
  const [description, setDescription] = useState(project?.description ?? '')
  const [status, setStatus] = useState<ProjectStatus>(project?.status ?? 'Planning')
  const [validation, setValidation] = useState<string | null>(null)
  const mutation = useMutation()
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!name.trim()) { setValidation('Project name cannot be blank.'); return }
    setValidation(null)
    const input = { name: name.trim(), description: description || null, status }
    void mutation.run((signal) => project ? workApi.updateProject(tenantId, project.id, input, signal) : workApi.createProject(tenantId, input, signal), onSaved)
  }
  return <Modal title={project ? 'Edit project' : 'Create project'} busy={mutation.busy} onClose={onClose}>
    <form onSubmit={submit} className="work-form">
      <WorkFeedback message={validation ?? mutation.error} error />
      <fieldset disabled={mutation.busy} className="form-fields">
        <div><label className="field-label" htmlFor="project-name">Project name</label><input autoFocus className="field" id="project-name" required maxLength={200} value={name} onChange={(event) => setName(event.target.value)} /></div>
        <div><label className="field-label" htmlFor="project-description">Description <span>(optional)</span></label><textarea className="field" id="project-description" rows={4} value={description} onChange={(event) => setDescription(event.target.value)} /></div>
        <div><label className="field-label" htmlFor="project-status">Project status</label><select className="field" id="project-status" value={status} onChange={(event) => setStatus(event.target.value as ProjectStatus)}>{projectStatuses.map((value) => <option key={value} value={value}>{statusLabel(value)}</option>)}</select></div>
      </fieldset>
      <div className="dialog-actions"><button type="button" className="button-secondary" disabled={mutation.busy} onClick={onClose}>Cancel</button><button className="button-primary" type="submit" disabled={mutation.busy}>{mutation.busy ? 'Saving…' : project ? 'Save project' : 'Create project'}</button></div>
    </form>
  </Modal>
}
