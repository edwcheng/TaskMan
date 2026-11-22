import { client } from './client'
import type { User } from './types'

export interface LoginPayload {
  userName: string
  password: string
}

export interface ChangePasswordPayload {
  currentPassword: string
  newPassword: string
}

export async function me(): Promise<User> {
  const response = await client.get<User>('/auth/me')

  return response.data
}

export async function login(payload: LoginPayload): Promise<User> {
  const response = await client.post<User>('/auth/login', payload)

  return response.data
}

export async function logout(): Promise<void> {
  await client.post('/auth/logout')
}

export async function changePassword(payload: ChangePasswordPayload): Promise<void> {
  await client.post('/auth/change-password', payload)
}
