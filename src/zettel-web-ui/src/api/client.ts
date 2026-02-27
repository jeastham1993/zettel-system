import { getToken, redirectToLogin } from '../auth'

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

function authHeaders(): Record<string, string> {
  const token = getToken()
  return token ? { Authorization: `Bearer ${token}` } : {}
}

async function handleResponse<T>(response: Response): Promise<T> {
  if (response.status === 401) {
    // Token expired or missing — redirect back to Cognito login
    await redirectToLogin()
    throw new ApiError(401, 'Unauthorized', 'Session expired — redirecting to login')
  }
  if (!response.ok) {
    const body = await response.text().catch(() => '')
    throw new ApiError(response.status, response.statusText, body || undefined)
  }
  if (response.status === 204 || response.status === 202) return undefined as T
  return response.json() as Promise<T>
}

export async function get<T>(url: string): Promise<T> {
  const response = await fetch(url, { headers: authHeaders() })
  return handleResponse<T>(response)
}

export async function post<T>(url: string, body?: unknown): Promise<T> {
  const response = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...authHeaders() },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })
  return handleResponse<T>(response)
}

export async function put<T>(url: string, body: unknown): Promise<T> {
  const response = await fetch(url, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json', ...authHeaders() },
    body: JSON.stringify(body),
  })
  return handleResponse<T>(response)
}

export async function del(url: string): Promise<void> {
  const response = await fetch(url, {
    method: 'DELETE',
    headers: authHeaders(),
  })
  return handleResponse<void>(response)
}

export async function getBlob(url: string): Promise<Blob> {
  const response = await fetch(url, { headers: authHeaders() })
  if (response.status === 401) {
    await redirectToLogin()
    throw new ApiError(401, 'Unauthorized')
  }
  if (!response.ok) {
    throw new ApiError(response.status, response.statusText)
  }
  return response.blob()
}
