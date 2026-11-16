import { AlertCircle, CheckCircle2 } from 'lucide-react'

export function WorkFeedback({ message, error = false }: { message: string | null; error?: boolean }) {
  if (!message) return null
  return <div className={error ? 'work-feedback work-feedback-error' : 'work-feedback'} role={error ? 'alert' : 'status'}>
    {error ? <AlertCircle size={17} aria-hidden="true" /> : <CheckCircle2 size={17} aria-hidden="true" />}<span>{message}</span>
  </div>
}
