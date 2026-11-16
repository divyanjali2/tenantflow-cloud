import type { CurrentUser } from '../api/types'

interface SessionSnapshot {
  status: 'anonymous' | 'authenticating' | 'authenticated'
  user: CurrentUser | null
  reason: 'expired' | null
}

// Memory-only: no cookies, localStorage, sessionStorage or URL tokens.
let accessToken: string | null = null
let generation = 0
let expiryTimer: ReturnType<typeof setTimeout> | undefined
let snapshot: SessionSnapshot = { status: 'anonymous', user: null, reason: null }
const listeners = new Set<() => void>()

function publish(next: SessionSnapshot) {
  snapshot = next
  listeners.forEach((listener) => listener())
}
export const getAccessToken = () => accessToken
export const getSessionSnapshot = () => snapshot
export function subscribeToSession(listener: () => void) {
  listeners.add(listener)
  return () => { listeners.delete(listener) }
}
export function clearSession(reason: SessionSnapshot['reason'] = null) {
  generation++
  clearTimeout(expiryTimer)
  accessToken = null
  publish({ status: 'anonymous', user: null, reason })
}
export function openSession(token: string, expiresIn: number) {
  clearTimeout(expiryTimer)
  const currentGeneration = ++generation
  accessToken = token
  publish({ status: 'authenticating', user: null, reason: null })
  expiryTimer = setTimeout(() => {
    if (generation === currentGeneration) clearSession('expired')
  }, Math.min(expiresIn * 1000, 2_147_483_647))
  return currentGeneration
}
export function confirmSession(user: CurrentUser, expectedGeneration: number) {
  if (generation !== expectedGeneration || !accessToken) return false
  publish({ status: 'authenticated', user, reason: null })
  return true
}
