import { AlertCircle, Building2, LoaderCircle } from 'lucide-react'
import type { ReactNode } from 'react'

export function LoadingState({ label = 'Loading your workspace…' }: { label?: string }) {
  return <div className="state-panel" role="status"><LoaderCircle className="animate-spin text-teal-700" size={25} aria-hidden="true" /><p>{label}</p></div>
}
export function ErrorState({ message, onRetry, title = 'We couldn’t load your workspaces' }: { message: string; onRetry: () => void; title?: string }) {
  return <div className="state-panel" role="alert"><AlertCircle className="text-amber-700" size={28} aria-hidden="true" />
    <h2>{title}</h2><p>{message}</p><button className="button-secondary" onClick={onRetry}>Try again</button></div>
}
export function EmptyState({ title, children }: { title: string; children: ReactNode }) {
  return <div className="state-panel"><span className="empty-icon"><Building2 size={26} aria-hidden="true" /></span>
    <h2>{title}</h2><div className="max-w-md text-sm leading-6 text-slate-500">{children}</div></div>
}
export function PageHeader({ eyebrow, title, description }: { eyebrow: string; title: string; description: string }) {
  return <div className="page-heading"><p className="eyebrow">{eyebrow}</p><h1>{title}</h1><p className="page-description">{description}</p></div>
}
