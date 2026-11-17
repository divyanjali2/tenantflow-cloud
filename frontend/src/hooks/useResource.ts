import { useEffect, useState } from 'react'
import { apiErrorMessage } from '../api/client'

export type Resource<T> = { status: 'loading' } | { status: 'error'; message: string } | { status: 'success'; data: T }

// load must be memoized by its caller. The key invalidates old data in the same
// render, before effects run. Abort plus the result guard also covers late responses.
export function useResource<T>(key: string, load: (signal: AbortSignal) => Promise<T>) {
  const [revision, setRevision] = useState(0)
  const requestKey = `${key}:${revision}`
  const [stored, setStored] = useState<{ key: string; state: Resource<T> }>({ key: requestKey, state: { status: 'loading' } })
  useEffect(() => {
    const controller = new AbortController()
    void load(controller.signal).then(
      (data) => { if (!controller.signal.aborted) setStored({ key: requestKey, state: { status: 'success', data } }) },
      (error: unknown) => { if (!controller.signal.aborted) setStored({ key: requestKey, state: { status: 'error', message: apiErrorMessage(error) } }) },
    )
    return () => controller.abort()
  }, [load, requestKey])
  const state: Resource<T> = stored.key === requestKey ? stored.state : { status: 'loading' }
  return { state, reload: () => setRevision((value) => value + 1) }
}
