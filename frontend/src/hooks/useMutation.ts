import { useEffect, useRef, useState } from 'react'
import { apiErrorMessage } from '../api/client'

export function useMutation(describeError: (error: unknown) => string = apiErrorMessage) {
  const lifecycle = useRef<AbortController | null>(null)
  const pending = useRef(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  useEffect(() => {
    const controller = new AbortController()
    lifecycle.current = controller
    return () => controller.abort()
  }, [])
  async function run(action: (signal: AbortSignal) => Promise<unknown>, onSuccess: () => void) {
    const controller = lifecycle.current
    if (!controller || controller.signal.aborted || pending.current) return
    pending.current = true
    setBusy(true)
    setError(null)
    try {
      await action(controller.signal)
      if (!controller.signal.aborted) onSuccess()
    } catch (cause: unknown) {
      if (!controller.signal.aborted) setError(describeError(cause))
    } finally {
      pending.current = false
      if (!controller.signal.aborted) setBusy(false)
    }
  }
  return { busy, error, run }
}
