import { get, TimeoutError, testConnection } from './client'
import type { HealthReport } from './types'

export function getHealth(): Promise<HealthReport> {
  return get<HealthReport>('/health')
}

/**
 * Check health of an arbitrary server URL (used during setup before
 * the server URL is persisted to MMKV).
 * Now uses the shared testConnection function for consistency.
 */
export async function checkServerHealth(serverUrl: string): Promise<HealthReport> {
  // Set the server URL temporarily for the test
  const originalUrl = localStorage.getItem('server-url')
  
  try {
    // Store temporarily for testConnection to use
    if (typeof window !== 'undefined') {
      localStorage.setItem('server-url', serverUrl)
    }
    
    const result = await testConnection()
    
    if (!result.success) {
      throw new Error(result.error || 'Connection test failed')
    }
    
    // If we got here, the connection worked - now get the actual health report
    const response = await fetch(`${serverUrl.replace(/\/+$/, '')}/health`)
    if (!response.ok) {
      throw new Error(`Server returned ${response.status}`)
    }
    return response.json() as Promise<HealthReport>
  } finally {
    // Restore original URL
    if (originalUrl && typeof window !== 'undefined') {
      localStorage.setItem('server-url', originalUrl)
    } else if (typeof window !== 'undefined') {
      localStorage.removeItem('server-url')
    }
  }
}
