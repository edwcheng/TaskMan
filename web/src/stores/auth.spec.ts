import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import * as authApi from '../api/auth'
import type { User } from '../api/types'
import { useAuthStore } from './auth'

vi.mock('../api/auth', () => ({
  me: vi.fn(),
  login: vi.fn(),
  logout: vi.fn(),
  changePassword: vi.fn(),
}))

const api = vi.mocked(authApi)

const sampleUser: User = {
  id: '00000000-0000-0000-0000-000000000001',
  userName: 'admin',
  displayName: 'Administrator',
  role: 'Admin',
  isActive: true,
  createdAt: '2026-11-22T00:00:00Z',
  lastLoginAt: null,
}

beforeEach(() => {
  setActivePinia(createPinia())
  vi.clearAllMocks()
  api.me.mockRejectedValue(new Error('unauthorized'))
})

describe('useAuthStore', () => {
  it('marks the session unauthenticated when the bootstrap probe fails', async () => {
    const auth = useAuthStore()

    await auth.bootstrap()

    expect(api.me).toHaveBeenCalledTimes(1)
    expect(auth.status).toBe('unauthenticated')
    expect(auth.isAuthenticated).toBe(false)
    expect(auth.user).toBeNull()
  })

  it('restores the session from /auth/me', async () => {
    api.me.mockResolvedValue(sampleUser)
    const auth = useAuthStore()

    await auth.bootstrap()

    expect(auth.status).toBe('authenticated')
    expect(auth.isAuthenticated).toBe(true)
    expect(auth.isAdmin).toBe(true)
    expect(auth.user?.userName).toBe('admin')
  })

  it('bootstrap is a no-op after the first attempt', async () => {
    const auth = useAuthStore()

    await auth.bootstrap()
    await auth.bootstrap()

    expect(api.me).toHaveBeenCalledTimes(1)
  })

  it('login stores the returned profile', async () => {
    const plain: User = { ...sampleUser, role: 'User', userName: 'alice' }
    api.login.mockResolvedValue(plain)
    const auth = useAuthStore()

    await auth.login('alice', 'secret')

    expect(api.login).toHaveBeenCalledWith({ userName: 'alice', password: 'secret' })
    expect(auth.isAuthenticated).toBe(true)
    expect(auth.isAdmin).toBe(false)
  })

  it('logout clears the session even when the API call fails', async () => {
    api.me.mockResolvedValue(sampleUser)
    api.logout.mockRejectedValue(new Error('offline'))
    const auth = useAuthStore()

    await auth.bootstrap()
    await auth.logout()

    expect(auth.isAuthenticated).toBe(false)
    expect(auth.user).toBeNull()
    expect(auth.status).toBe('unauthenticated')
  })
})
