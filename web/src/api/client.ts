import axios, { type AxiosError, type AxiosInstance } from 'axios'
import type { ProblemDetails } from './types'

// The API rejects non-GET /api calls that lack this header (CSRF guard,
// see PLAN.md section 9).
export const CsrfHeader = 'X-Requested-With'
export const CsrfHeaderValue = 'TaskMan'

export type UnauthorizedHandler = (currentUrl: string) => void

let unauthorizedHandler: UnauthorizedHandler | null = null

// Tests swap in a spy handler; passing null restores the default.
export function setUnauthorizedHandler(handler: UnauthorizedHandler | null): void {
  unauthorizedHandler = handler
}

// Build the login URL for a protected page. The value is validated again
// before use (see safeReturnTo) so the query string cannot smuggle an
// external target.
export function loginRedirectUrl(currentUrl: string): string {
  return currentUrl && currentUrl !== '/'
    ? `/login?return=${encodeURIComponent(currentUrl)}`
    : '/login'
}

const defaultUnauthorizedHandler: UnauthorizedHandler = (currentUrl) => {
  window.location.assign(loginRedirectUrl(currentUrl))
}

// The login form surfaces its own errors, and the bootstrap probe is owned
// by the auth store, which the router guard acts on.
function isExcludedFromRedirect(url: string | undefined): boolean {
  return url === '/auth/login' || url === '/auth/me'
}

export function createClient(): AxiosInstance {
  const instance = axios.create({
    baseURL: '/api',
    headers: { [CsrfHeader]: CsrfHeaderValue },
  })

  instance.interceptors.response.use(
    (response) => response,
    (error: AxiosError) => {
      if (error.response?.status === 401 && !isExcludedFromRedirect(error.config?.url)) {
        const handler = unauthorizedHandler ?? defaultUnauthorizedHandler
        handler(window.location.pathname + window.location.search)
      }

      return Promise.reject(error)
    }
  )

  return instance
}

export const client = createClient()

// Best-effort human message from an RFC 7807 problem body: validation errors
// first, then detail, then title, then the supplied fallback.
export function problemMessage(error: unknown, fallback: string): string {
  const data = (error as AxiosError<ProblemDetails> | undefined)?.response?.data
  const fieldError = data?.errors ? Object.values(data.errors).flat().find(Boolean) : undefined

  return fieldError ?? data?.detail ?? data?.title ?? fallback
}

export function retryAfterSeconds(error: unknown): number | null {
  const value = (error as AxiosError | undefined)?.response?.headers?.['retry-after']
  const parsed = Number(value)

  return Number.isFinite(parsed) ? parsed : null
}
