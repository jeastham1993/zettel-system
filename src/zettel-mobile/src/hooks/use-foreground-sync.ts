import { useEffect, useRef } from 'react'
import { AppState, type AppStateStatus } from 'react-native'
import { useQueryClient } from '@tanstack/react-query'
import { useOfflineQueue } from './use-offline-queue'
import { offlineQueue } from '../stores/offline-queue'
import { serverStore } from '../stores/server-store'
import { checkServerHealth } from '../api/health'

/**
 * Syncs the offline queue when the app returns to the foreground.
 *
 * Flow:
 * 1. AppState transitions to 'active'
 * 2. Read the queue directly from MMKV (avoids stale closure over React state)
 * 3. If non-empty, check server connectivity via /health
 * 4. If connected, replay all queued captures via syncAll()
 * 5. Invalidate query caches so lists reflect the synced notes
 * 6. Fire onSyncResult so the caller can show notifications
 */
export function useForegroundSync(
  onSyncResult?: (result: { synced: number; failed: number }) => void,
) {
  const { syncAll, refreshCount } = useOfflineQueue()
  const queryClient = useQueryClient()
  const appState = useRef<AppStateStatus>(AppState.currentState)

  useEffect(() => {
    const subscription = AppState.addEventListener('change', async (nextState) => {
      if (appState.current.match(/inactive|background/) && nextState === 'active') {
        refreshCount()

        // Read directly from MMKV to avoid stale closure over React state
        const queueCount = offlineQueue.getCount()
        if (queueCount > 0) {
          const serverUrl = serverStore.getServerUrl()
          if (!serverUrl) return

          try {
            await checkServerHealth(serverUrl)
            serverStore.setConnectionState('connected')

            const result = await syncAll()

            // Invalidate query caches so inbox and note lists reflect synced notes
            if (result.synced > 0) {
              queryClient.invalidateQueries({ queryKey: ['inbox'] })
              queryClient.invalidateQueries({ queryKey: ['inbox', 'count'] })
              queryClient.invalidateQueries({ queryKey: ['notes'] })
            }

            if (onSyncResult && (result.synced > 0 || result.failed > 0)) {
              onSyncResult(result)
            }
          } catch (err) {
            console.warn('Foreground sync health check failed:', err)
            serverStore.setConnectionState('disconnected')
          }
        }
      }

      appState.current = nextState
    })

    return () => subscription.remove()
  }, [syncAll, refreshCount, onSyncResult, queryClient])
}
