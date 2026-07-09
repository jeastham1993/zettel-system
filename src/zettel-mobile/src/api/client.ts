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
  
  // Debug: log the exact URL being used
  console.log('Using base URL:', url)
  
  // Remove trailing slashes only, preserve port and path
  return url.replace(/\/+$/, '')
}

// Add this helper function to test connectivity
export async function testConnection(): Promise<{ success: boolean, error?: string }> {
  try {
    const baseUrl = getBaseUrl()
    console.log('Testing connection to:', baseUrl)
    
    const controller = new AbortController()
    const timeoutId = setTimeout(() => controller.abort(), 15000)
    
    try {
      // First try the health endpoint
      const healthResponse = await fetch(`${baseUrl}/api/health`, {
        method: 'GET',
        headers: {
          'Accept': 'application/json',
          'Content-Type': 'application/json'
        },
        signal: controller.signal
      })
      
      clearTimeout(timeoutId)
      console.log('Health check response:', healthResponse.status)
      
      // If health check works, we're good
      if (healthResponse.ok) {
        return { success: true }
      }
      
      // If health check fails but we can load notes, consider it a success
      // This handles cases where /api/health doesn't exist but the server is working
      console.log('Health endpoint failed, trying notes endpoint as fallback...')
      
      const notesResponse = await fetch(`${baseUrl}/api/notes?take=1`, {
        method: 'GET',
        headers: {
          'Accept': 'application/json',
          'Content-Type': 'application/json'
        },
        signal: controller.signal
      })
      
      if (notesResponse.ok) {
        console.log('Notes endpoint works, considering connection successful')
        return { success: true }
      }
      
      const body = await healthResponse.text().catch(() => '')
      return { 
        success: false, 
        error: `Health check failed (${healthResponse.status}) but notes endpoint also failed. Server may be misconfigured.`
      }
    } catch (error) {
      clearTimeout(timeoutId)
      console.error('Connection test failed:', error)
      
      if (error instanceof DOMException && error.name === 'AbortError') {
        return { success: false, error: 'Connection timed out after 15 seconds' }
      }
      
      return { success: false, error: error.message || 'Unknown connection error' }
    }
  } catch (error) {
    console.error('Connection setup failed:', error)
    return { success: false, error: error.message || 'Failed to setup connection test' }
  }
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
