import { useState } from 'react'
import type { FormEvent } from 'react'
import { ArrowRight, Check, Eye, EyeOff, Layers2, LoaderCircle, LockKeyhole } from 'lucide-react'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { signIn } from '../api/auth'
import { apiErrorMessage } from '../api/client'
import { useSession } from '../auth/useSession'
import { Brand } from '../components/Brand'

const destinations = ['/dashboard', '/projects', '/tasks', '/team', '/settings']
export function LoginPage() {
  const session = useSession()
  const navigate = useNavigate()
  const location = useLocation()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [visible, setVisible] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const state: unknown = location.state
  const from = state && typeof state === 'object' && 'from' in state && typeof state.from === 'string' && (destinations.includes(state.from) || /^\/projects\/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\/tasks$/i.test(state.from)) ? state.from : '/dashboard'
  if (session.status === 'authenticated') return <Navigate to={from} replace />
  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (submitting) return
    setSubmitting(true)
    setError(null)
    try {
      await signIn({ email: email.trim(), password })
      setPassword('')
      navigate(from, { replace: true })
    } catch (cause: unknown) {
      setError(apiErrorMessage(cause))
    } finally { setSubmitting(false) }
  }
  return <main className="login-page">
    <section className="login-story"><Brand inverse /><div className="login-story-copy"><span className="login-kicker"><span />A calmer way to work</span><h1>Great work starts<br />with a little<br /><em>common ground.</em></h1><p>Bring your people, projects, and priorities together. Find your focus. Move forward as one.</p><div className="login-illustration" aria-hidden="true"><div className="orbit orbit-one" /><div className="orbit orbit-two" /><div className="orbit-center"><Layers2 size={42} strokeWidth={1.2} /></div><div className="orbit-check"><Check size={23} /></div><div className="orbit-dot" /></div></div><p className="login-story-footer">One workspace. Shared momentum.</p></section>
    <section className="login-form-section"><div className="login-mobile-brand"><Brand /></div><div className="login-form-container"><span className="login-lock"><LockKeyhole size={23} strokeWidth={1.6} /></span><p className="eyebrow mt-7">WELCOME TO TENANTFLOW</p><h2>Good to have you back.</h2><p className="login-subtitle">Sign in to pick up where your team left off.</p>
      {session.reason === 'expired' && !error && <div className="notice" role="status">Your session has ended. Please sign in again.</div>}
      {error && <div id="login-error" className="form-error" role="alert">{error}</div>}
      <form onSubmit={(event) => { void handleSubmit(event) }} className="login-form" aria-describedby={error ? 'login-error' : undefined}>
        <div><label className="field-label" htmlFor="email">Email address</label><input className="field" id="email" type="email" autoComplete="username" placeholder="you@company.com" required maxLength={256} value={email} onChange={(event) => setEmail(event.target.value)} disabled={submitting} /></div>
        <div><label className="field-label" htmlFor="password">Password</label><div className="password-field"><input className="field pr-12" id="password" type={visible ? 'text' : 'password'} autoComplete="current-password" required minLength={12} maxLength={128} value={password} onChange={(event) => setPassword(event.target.value)} disabled={submitting} /><button type="button" className="password-toggle" onClick={() => setVisible(!visible)} aria-label={visible ? 'Hide password' : 'Show password'} aria-pressed={visible}>{visible ? <EyeOff size={18} /> : <Eye size={18} />}</button></div></div>
        <button className="button-primary w-full justify-center" disabled={submitting} type="submit">{submitting ? <><LoaderCircle size={18} className="animate-spin" />Signing in…</> : <>Sign in to your workspace<ArrowRight size={18} /></>}</button>
      </form><p className="login-security"><LockKeyhole size={14} aria-hidden="true" />For your security, sign in again after refreshing or closing this page.</p>
    </div><p className="login-copyright">TenantFlow Cloud · A place for your team to thrive.</p></section>
  </main>
}
