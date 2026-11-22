import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import * as authApi from '../api/auth'
import type { User } from '../api/types'

export type AuthStatus = 'idle' | 'loading' | 'authenticated' | 'unauthenticated'

export const useAuthStore = defineStore('auth', () => {
  const user = ref<User | null>(null)
  const status = ref<AuthStatus>('idle')

  const isAuthenticated = computed(() => status.value === 'authenticated' && user.value !== null)
  const isAdmin = computed(() => user.value?.role === 'Admin')

  // Restore the session on first navigation; the server remains the
  // authority, this only seeds the UI state.
  async function bootstrap(): Promise<void> {
    if (status.value !== 'idle') return

    status.value = 'loading'

    try {
      user.value = await authApi.me()
      status.value = 'authenticated'
    } catch {
      user.value = null
      status.value = 'unauthenticated'
    }
  }

  async function login(userName: string, password: string): Promise<void> {
    user.value = await authApi.login({ userName, password })
    status.value = 'authenticated'
  }

  // The session is cleared locally even if the API call fails: the local
  // state must not claim an authenticated session.
  async function logout(): Promise<void> {
    try {
      await authApi.logout()
    } catch {
      // The failure is not actionable from the shell; the session is cleared
      // unconditionally below.
    } finally {
      user.value = null
      status.value = 'unauthenticated'
    }
  }

  return { user, status, isAuthenticated, isAdmin, bootstrap, login, logout }
})
