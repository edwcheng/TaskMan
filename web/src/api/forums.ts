import { client } from './client'
import type { Forum } from './types'

export async function list(includeArchived = true): Promise<Forum[]> {
  const response = await client.get<Forum[]>('/forums', {
    params: { includeArchived },
  })

  return response.data
}

export async function getByIdOrSlug(idOrSlug: string): Promise<Forum> {
  const response = await client.get<Forum>(`/forums/${encodeURIComponent(idOrSlug)}`)

  return response.data
}
