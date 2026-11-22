// Wire types mirror the API's System.Text.Json output: camelCase properties,
// enums serialized as PascalCase strings (see JsonStringEnumConverter in
// TaskMan.Api/Program.cs).

export type TaskItemStatus = 'Open' | 'InProgress' | 'Completed' | 'Closed' | 'OnHold'

export type TaskPriority = 'Low' | 'Normal' | 'High' | 'Urgent'

export type UserRole = 'User' | 'Admin'

export interface User {
  id: string
  userName: string
  displayName: string
  role: UserRole
  isActive: boolean
  createdAt: string
  lastLoginAt: string | null
}

export interface UserSummary {
  id: string
  userName: string
  displayName: string
}

export interface Forum {
  id: string
  name: string
  slug: string
  description: string | null
  sortOrder: number
  isArchived: boolean
  createdAt: string
  taskCount: number
  openTaskCount: number
  lastActivityAt: string | null
}

export interface Attachment {
  id: string
  fileName: string
  contentType: string
  sizeBytes: number
  uploadedBy: UserSummary
  createdAt: string
}

export interface Page<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
}

export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
  errors?: Record<string, string[]>
}
