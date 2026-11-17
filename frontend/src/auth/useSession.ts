import { useSyncExternalStore } from 'react'
import { getSessionSnapshot, subscribeToSession } from './session'

export function useSession() {
  return useSyncExternalStore(subscribeToSession, getSessionSnapshot)
}
