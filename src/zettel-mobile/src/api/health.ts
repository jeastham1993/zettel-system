import { get, TimeoutError } from './client'
import type { HealthReport } from './types'

const HEALTH_TIMEOUT_MS = 5_000

export function getHealth(): Promise<HealthReport> {
  return get<HealthReport>('/health')
}

/**
 * Check health of an arbitrary server URL (used during setup before
 * the server URL is persisted to MMKV).
 */
export async function checkServerHealth(serverUrl: string): Promise<HealthReport> {
  const url = serverUrl.replace(/\/+$/, '')
  const endpoint = `${url}/health`
  const controller = new AbortController()
  const timeoutId = setTimeout(() => controller.abort(), HEALTH_TIMEOUT_MS)
  try {
    const response = await fetch(endpoint, { signal: controller.signal })
    if (!response.ok) {
      throw new Error(`Server returned ${response.status}`)
    }
    return response.json() as Promise<HealthReport>
  } catch (err) {
    if (err instanceof DOMException && err.name === 'AbortError') {
      throw new TimeoutError(endpoint, HEALTH_TIMEOUT_MS)
    }
    throw err
  } finally {
    clearTimeout(timeoutId)
  }
}
