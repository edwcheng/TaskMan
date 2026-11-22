import { describe, expect, it } from 'vitest'
import { safeReturnTo } from './returnUrl'

describe('safeReturnTo', () => {
  it('accepts same-origin absolute paths, including queries', () => {
    expect(safeReturnTo('/tasks')).toBe('/tasks')
    expect(safeReturnTo('/tasks/12?tab=replies')).toBe('/tasks/12?tab=replies')
  })

  it('rejects relative, protocol-relative, external, and non-string values', () => {
    expect(safeReturnTo('tasks')).toBeNull()
    expect(safeReturnTo('//evil.example')).toBeNull()
    expect(safeReturnTo('\\evil.example')).toBeNull()
    expect(safeReturnTo('https://evil.example')).toBeNull()
    expect(safeReturnTo(undefined)).toBeNull()
    expect(safeReturnTo(null)).toBeNull()
    expect(safeReturnTo(42)).toBeNull()
  })
})
