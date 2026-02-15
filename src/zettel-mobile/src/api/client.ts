import { serverStore } from '../stores/server-store'

export class ApiError extends Error {
  status: number
  statusText: string

  constructor(status: number, statusText: string, message?: string) {
    super(message ?? `${status} ${statusText}`)
    this.name = 'ApiError'
    this.status = status
    this.statusText = statusText
  }
}

export class TimeoutError extends Error {
  constructor(url: string, ms: number) {
    super(`Request to ${url} timed out after ${ms}ms`)
    this.name = 'TimeoutError'
  }
}

const DEFAULT_TIMEOUT_MS = 10_000

function getBaseUrl(): string {
  const url = serverStore.getServerUrl()
  if (!url) {
    throw new Error('Server URL not configured. Please set it in Settings.')
  }
  return url.replace(/\/+$/, '')
}

async function handleResponse<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const body = await response.text().catch(() => '')
    throw new ApiError(response.status, response.statusText, body || undefined)
  }
  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}

export async function get<T>(path: string): Promise<T> {
  const url = `${getBaseUrl()}${path}`
  const controller = new AbortController()
  const timeoutId = setTimeout(() => controller.abort(), DEFAULT_TIMEOUT_MS)
  try {
    const response = await fetch(url, { signal: controller.signal })
    return handleResponse<T>(response)
  } catch (err) {
    if (err instanceof DOMException && err.name === 'AbortError') {
      throw new TimeoutError(url, DEFAULT_TIMEOUT_MS)
    }
    throw err
  } finally {
    clearTimeout(timeoutId)
  }
}

export async function post<T>(path: string, body?: unknown): Promise<T> {
  const url = `${getBaseUrl()}${path}`
  const controller = new AbortController()
  const timeoutId = setTimeout(() => controller.abort(), DEFAULT_TIMEOUT_MS)
  try {
    const response = await fetch(url, {
      method: 'POST',
      headers: body !== undefined ? { 'Content-Type': 'application/json' } : {},
      body: body !== undefined ? JSON.stringify(body) : undefined,
      signal: controller.signal,
    })
    return handleResponse<T>(response)
  } catch (err) {
    if (err instanceof DOMException && err.name === 'AbortError') {
      throw new TimeoutError(url, DEFAULT_TIMEOUT_MS)
    }
    throw err
  } finally {
    clearTimeout(timeoutId)
  }
}

export async function put<T>(path: string, body: unknown): Promise<T> {
  const url = `${getBaseUrl()}${path}`
  const controller = new AbortController()
  const timeoutId = setTimeout(() => controller.abort(), DEFAULT_TIMEOUT_MS)
  try {
    const response = await fetch(url, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
      signal: controller.signal,
    })
    return handleResponse<T>(response)
  } catch (err) {
    if (err instanceof DOMException && err.name === 'AbortError') {
      throw new TimeoutError(url, DEFAULT_TIMEOUT_MS)
    }
    throw err
  } finally {
    clearTimeout(timeoutId)
  }
}

export async function del(path: string): Promise<void> {
  const url = `${getBaseUrl()}${path}`
  const controller = new AbortController()
  const timeoutId = setTimeout(() => controller.abort(), DEFAULT_TIMEOUT_MS)
  try {
    const response = await fetch(url, { method: 'DELETE', signal: controller.signal })
    return handleResponse<void>(response)
  } catch (err) {
    if (err instanceof DOMException && err.name === 'AbortError') {
      throw new TimeoutError(url, DEFAULT_TIMEOUT_MS)
    }
    throw err
  } finally {
    clearTimeout(timeoutId)
  }
}
