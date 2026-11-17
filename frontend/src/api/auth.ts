import { clearSession, confirmSession, getAccessToken, openSession } from '../auth/session'
import { api } from './client'
import type { CurrentUser, LoginRequest, LoginResponse } from './types'

export async function signIn(credentials: LoginRequest) {
  const { data } = await api.post<LoginResponse>('/auth/login', credentials)
  if (!data.accessToken || data.tokenType.toLowerCase() !== 'bearer' || !Number.isFinite(data.expiresIn) || data.expiresIn <= 0)
    throw new Error('Invalid sign-in response')
  const generation = openSession(data.accessToken, data.expiresIn)
  try {
    const { data: user } = await api.get<CurrentUser>('/auth/me')
    if (!confirmSession(user, generation)) throw new Error('Session ended')
  } catch (error: unknown) {
    if (getAccessToken() === data.accessToken) clearSession()
    throw error
  }
}
