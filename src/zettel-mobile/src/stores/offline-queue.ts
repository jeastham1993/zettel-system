import { createMMKV } from 'react-native-mmkv'

const storage = createMMKV({ id: 'offline-queue' })

const QUEUE_KEY = 'offline-queue'

export interface OfflineCapture {
  id: string
  content: string
  tags: string[]
  source: 'mobile'
  capturedAt: string
}

function generateId(): string {
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0
    const v = c === 'x' ? r : (r & 0x3) | 0x8
    return v.toString(16)
  })
}

let isSyncing = false

export const offlineQueue = {
  acquireSyncLock(): boolean {
    if (isSyncing) return false
    isSyncing = true
    return true
  },

  releaseSyncLock(): void {
    isSyncing = false
  },

  getQueue(): OfflineCapture[] {
    const raw = storage.getString(QUEUE_KEY)
    if (!raw) return []
    try {
      return JSON.parse(raw) as OfflineCapture[]
    } catch {
      return []
    }
  },

  getCount(): number {
    return this.getQueue().length
  },

  enqueue(content: string, tags: string[] = []): OfflineCapture {
    const capture: OfflineCapture = {
      id: generateId(),
      content,
      tags,
      source: 'mobile',
      capturedAt: new Date().toISOString(),
    }
    const queue = this.getQueue()
    queue.push(capture)
    storage.set(QUEUE_KEY, JSON.stringify(queue))
    return capture
  },

  dequeue(id: string): void {
    const queue = this.getQueue().filter((item) => item.id !== id)
    storage.set(QUEUE_KEY, JSON.stringify(queue))
  },

  clear(): void {
    storage.remove(QUEUE_KEY)
  },
}
