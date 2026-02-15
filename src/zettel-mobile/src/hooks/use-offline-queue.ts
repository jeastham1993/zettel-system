import { useState, useCallback } from 'react'
import { offlineQueue } from '../stores/offline-queue'
import * as notesApi from '../api/notes'
import type { CreateNoteRequest } from '../api/types'

export function useOfflineQueue() {
  const [count, setCount] = useState(offlineQueue.getCount())
  const [isSyncing, setIsSyncing] = useState(false)

  const refreshCount = useCallback(() => {
    setCount(offlineQueue.getCount())
  }, [])

  const enqueue = useCallback(
    (content: string, tags: string[] = []) => {
      offlineQueue.enqueue(content, tags)
      refreshCount()
    },
    [refreshCount],
  )

  const syncAll = useCallback(async (): Promise<{ synced: number; failed: number }> => {
    const queue = offlineQueue.getQueue()
    if (queue.length === 0) return { synced: 0, failed: 0 }

    if (!offlineQueue.acquireSyncLock()) return { synced: 0, failed: 0 }

    setIsSyncing(true)
    let synced = 0
    let failed = 0

    try {
      for (const capture of queue) {
        try {
          const req: CreateNoteRequest = {
            title: 'auto',
            content: capture.content,
            status: 'Fleeting',
            source: capture.source,
            tags: capture.tags,
          }
          await notesApi.createNote(req)
          offlineQueue.dequeue(capture.id)
          synced++
        } catch (err) {
          console.warn('Failed to sync offline capture:', capture.id, err)
          failed++
        }
      }
    } finally {
      offlineQueue.releaseSyncLock()
      refreshCount()
      setIsSyncing(false)
    }

    return { synced, failed }
  }, [refreshCount])

  return {
    count,
    isSyncing,
    enqueue,
    syncAll,
    refreshCount,
  }
}
