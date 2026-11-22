// The return URL travels through the query string, so only same-origin
// absolute paths are accepted. Everything else falls back to the default
// destination.
export function safeReturnTo(value: unknown): string | null {
  if (typeof value !== 'string') return null
  if (!value.startsWith('/') || value.startsWith('//') || value.startsWith('\\')) return null

  return value
}
