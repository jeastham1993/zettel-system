import { get } from './client'
import type { Note } from './types'

export type DiscoverMode = 'random' | 'orphans' | 'today'

export function discoverNotesByMode(mode: DiscoverMode, limit = 5): Promise<Note[]> {
  const params = new URLSearchParams({ mode, limit: String(limit) })
  return get<Note[]>(`/api/discover?${params}`)
}
