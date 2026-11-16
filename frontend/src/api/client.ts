import axios from 'axios'
import { clearSession, getAccessToken } from '../auth/session'

export const api = axios.create({
  baseURL: '/api', timeout: 15_000, headers: { Accept: 'application/json' },
})
api.interceptors.request.use((config) => {
  const token = getAccessToken()
  if (token && config.url !== '/auth/login') config.headers.set('Authorization', `Bearer ${token}`)
  return config
})
api.interceptors.response.use(
  (response) => response,
  (error: unknown) => {
    if (axios.isAxiosError(error) && error.response?.status === 401) {
      const token = getAccessToken()
      // A late response from an older session must not clear a newer login.
      if (token && error.config?.headers.get('Authorization') === `Bearer ${token}`) clearSession('expired')
    }
    return Promise.reject(error)
  },
)
export function apiErrorMessage(error: unknown): string {
  if (!axios.isAxiosError(error)) return 'Something went wrong. Please try again.'
  if (!error.response) return 'We couldn’t connect to TenantFlow. Please try again.'
  if (error.response.status === 401) return 'Your email or password is incorrect, or your session has expired.'
  if (error.response.status === 403) return 'You don’t have permission to view this information.'
  if (error.response.status === 404) return 'This resource is no longer available in the selected workspace. Refresh and try again.'
  if (error.response.status === 400) {
    const body: unknown = error.response.data
    if (body && typeof body === 'object' && 'errors' in body && body.errors && typeof body.errors === 'object') {
      const messages = Object.values(body.errors).flatMap((value: unknown) => Array.isArray(value) ? value.filter((item: unknown): item is string => typeof item === 'string') : [])
      if (messages.length) return messages.join(' ')
    }
    return 'Please check the form values and try again.'
  }
  if (error.response.status >= 500) return 'TenantFlow is temporarily unavailable. Please try again shortly.'
  return 'We couldn’t complete your request. Please check your details and try again.'
}
