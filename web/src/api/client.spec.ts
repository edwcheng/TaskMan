import { AxiosError } from 'axios'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ProblemDetails } from './types'
import {
  createClient,
  loginRedirectUrl,
  problemMessage,
  retryAfterSeconds,
  setUnauthorizedHandler,
} from './client'

function clientWithStatus(status: number, data?: unknown, headers?: Record<string, string>) {
  const instance = createClient()

  // Mirror the built-in adapters: they settle non-2xx responses themselves
  // (axios 1.x does not re-apply validateStatus to adapter results).
  instance.defaults.adapter = (async (config) => {
    const response = { data, status, statusText: '', headers: headers ?? {}, config }

    if (status < 200 || status >= 300) {
      throw new AxiosError(
        `Request failed with status code ${status}`,
        String(status),
        config,
        undefined,
        response
      )
    }

    return response
  }) as typeof instance.defaults.adapter

  return instance
}

beforeEach(() => {
  setUnauthorizedHandler(null)
})

afterEach(() => {
  vi.restoreAllMocks()
})

describe('createClient', () => {
  it('forwards successful responses', async () => {
    const instance = clientWithStatus(200, { id: '1' })

    const response = await instance.get('/tasks')

    expect(response.data).toEqual({ id: '1' })
  })

  it('invokes the configured handler on 401 with the current URL', async () => {
    const handler = vi.fn()
    setUnauthorizedHandler(handler)
    const instance = clientWithStatus(401)

    await instance.get('/tasks').catch(() => undefined)

    expect(handler).toHaveBeenCalledTimes(1)
    expect(handler).toHaveBeenCalledWith(window.location.pathname + window.location.search)
  })

  it('does not redirect on a failed login attempt', async () => {
    const handler = vi.fn()
    setUnauthorizedHandler(handler)
    const instance = clientWithStatus(401)

    await instance.post('/auth/login', { userName: 'x', password: 'y' }).catch(() => undefined)

    expect(handler).not.toHaveBeenCalled()
  })

  it('does not redirect on the bootstrap probe', async () => {
    const handler = vi.fn()
    setUnauthorizedHandler(handler)
    const instance = clientWithStatus(401)

    await instance.get('/auth/me').catch(() => undefined)

    expect(handler).not.toHaveBeenCalled()
  })

  it('rejects other statuses without a handler call', async () => {
    const handler = vi.fn()
    setUnauthorizedHandler(handler)
    const instance = clientWithStatus(404)

    await expect(instance.get('/tasks')).rejects.toBeInstanceOf(AxiosError)

    expect(handler).not.toHaveBeenCalled()
  })

  it('sets the CSRF header on outgoing requests', async () => {
    let seen: Record<string, unknown> = {}
    const instance = createClient()

    instance.defaults.adapter = (async (config) => {
      seen = config.headers as Record<string, unknown>

      return { data: {}, status: 200, statusText: '', headers: {}, config }
    }) as typeof instance.defaults.adapter

    await instance.get('/tasks')

    expect(seen['X-Requested-With']).toBe('TaskMan')
  })
})

describe('loginRedirectUrl', () => {
  it('points at the login page without a return for the root', () => {
    expect(loginRedirectUrl('/')).toBe('/login')
    expect(loginRedirectUrl('')).toBe('/login')
  })

  it('encodes the return URL, including queries', () => {
    expect(loginRedirectUrl('/tasks/5?tab=replies')).toBe(
      '/login?return=%2Ftasks%2F5%3Ftab%3Dreplies'
    )
  })
})

describe('problemMessage', () => {
  function errorWith(data?: ProblemDetails): AxiosError {
    const config = { url: '/x', method: 'get' } as never
    const response = { status: 400, statusText: '', headers: {}, config, data }

    return new AxiosError('Request failed', '400', config, undefined, response as never)
  }

  it('prefers the first field error from a validation problem', () => {
    const error = errorWith({
      status: 400,
      errors: { title: ['Title is required.'], forumId: ['Forum is required.'] },
    })

    expect(problemMessage(error, 'fallback')).toBe('Title is required.')
  })

  it('falls back to detail, then title, then the supplied message', () => {
    expect(problemMessage(errorWith({ detail: 'Invalid username or password.' }), 'fallback')).toBe(
      'Invalid username or password.'
    )
    expect(problemMessage(errorWith({ title: 'Invalid credentials' }), 'fallback')).toBe(
      'Invalid credentials'
    )
    expect(problemMessage(errorWith(undefined), 'fallback')).toBe('fallback')
    expect(problemMessage(new Error('boom'), 'fallback')).toBe('fallback')
  })
})

describe('retryAfterSeconds', () => {
  function errorWith(headers?: Record<string, string>): AxiosError {
    const config = { url: '/x', method: 'post' } as never
    const response = { status: 429, statusText: '', headers, config, data: {} }

    return new AxiosError('Too many requests', '429', config, undefined, response as never)
  }

  it('reads the Retry-After header', () => {
    expect(retryAfterSeconds(errorWith({ 'retry-after': '30' }))).toBe(30)
  })

  it('returns null when the header is absent or malformed', () => {
    expect(retryAfterSeconds(errorWith(undefined))).toBeNull()
    expect(retryAfterSeconds(errorWith({ 'retry-after': 'later' }))).toBeNull()
  })
})
