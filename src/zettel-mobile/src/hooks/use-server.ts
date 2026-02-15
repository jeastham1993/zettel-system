import { useState, useCallback, useEffect, useSyncExternalStore } from 'react'
import { serverStore, storage, type ConnectionState } from '../stores/server-store'
import { checkServerHealth } from '../api/health'

/**
 * Subscribe to MMKV changes for a specific key and return a reactive value.
 * Uses useSyncExternalStore so React re-renders whenever the underlying
 * MMKV value is written — even from outside this hook (e.g. foreground sync).
 */
function useMMKVValue<T>(key: string, getter: () => T): T {
  const subscribe = useCallback(
    (onStoreChange: () => void) => {
      const listener = storage.addOnValueChangedListener((changedKey) => {
        if (changedKey === key) onStoreChange()
      })
      return () => listener.remove()
    },
    [key],
  )

  return useSyncExternalStore(subscribe, getter, getter)
}

export function useServer() {
  const serverUrl = useMMKVValue('server-url', () => serverStore.getServerUrl())
  const connectionState = useMMKVValue<ConnectionState>(
    'connection-state',
    () => serverStore.getConnectionState(),
  )
  const [isChecking, setIsChecking] = useState(false)

  const isConfigured = !!serverUrl

  const setServerUrl = useCallback((url: string) => {
    serverStore.setServerUrl(url)
  }, [])

  const clearServerUrl = useCallback(() => {
    serverStore.clearServerUrl()
    serverStore.setConnectionState('disconnected')
  }, [])

  const checkConnection = useCallback(
    async (url?: string) => {
      const targetUrl = url ?? serverUrl
      if (!targetUrl) return false

      setIsChecking(true)
      serverStore.setConnectionState('checking')

      try {
        await checkServerHealth(targetUrl)
        serverStore.setConnectionState('connected')
        return true
      } catch (err) {
        console.warn('[server] health check failed:', err)
        serverStore.setConnectionState('disconnected')
        return false
      } finally {
        setIsChecking(false)
      }
    },
    [serverUrl],
  )

  // Check connection on mount if URL is configured
  useEffect(() => {
    if (serverUrl) {
      checkConnection()
    }
  }, [serverUrl, checkConnection])

  return {
    serverUrl,
    setServerUrl,
    clearServerUrl,
    connectionState,
    isConfigured,
    isChecking,
    checkConnection,
  }
}
