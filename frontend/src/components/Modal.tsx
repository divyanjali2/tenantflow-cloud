import { useEffect, useId, useRef } from 'react'
import type { ReactNode } from 'react'
import { X } from 'lucide-react'
import { useMutation } from '../hooks/useMutation'
import { WorkFeedback } from './WorkFeedback'

export function Modal({ title, busy = false, onClose, children }: { title: string; busy?: boolean; onClose: () => void; children: ReactNode }) {
  const dialog = useRef<HTMLDialogElement>(null)
  const titleId = useId()
  useEffect(() => {
    const element = dialog.current
    const previous = document.activeElement
    element?.showModal()
    return () => {
      element?.close()
      if (previous instanceof HTMLElement && previous.isConnected) previous.focus()
    }
  }, [])
  return <dialog ref={dialog} className="work-dialog" aria-labelledby={titleId} aria-busy={busy}
    onCancel={(event) => { event.preventDefault(); if (!busy) onClose() }}>
    <div className="dialog-header"><h2 id={titleId}>{title}</h2><button type="button" className="icon-button" aria-label="Close dialog" disabled={busy} onClick={onClose}><X size={19} /></button></div>
    {children}
  </dialog>
}

export function DeleteConfirmation({ kind, name, onClose, onDelete, onDeleted }: {
  kind: 'project' | 'task'; name: string; onClose: () => void
  onDelete: (signal: AbortSignal) => Promise<unknown>; onDeleted: () => void
}) {
  const mutation = useMutation()
  return <Modal title={`Delete ${kind}?`} busy={mutation.busy} onClose={onClose}>
    <p className="dialog-description">Permanently delete <strong>{name}</strong>? {kind === 'project' && 'All tasks in this project will also be deleted. '}This cannot be undone.</p>
    <WorkFeedback message={mutation.error} error />
    <div className="dialog-actions"><button className="button-secondary" autoFocus disabled={mutation.busy} onClick={onClose}>Cancel</button><button className="button-danger" disabled={mutation.busy} onClick={() => { void mutation.run(onDelete, onDeleted) }}>{mutation.busy ? 'Deleting…' : `Delete ${kind}`}</button></div>
  </Modal>
}
