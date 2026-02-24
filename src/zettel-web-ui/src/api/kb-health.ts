import { get, post } from './client'
import type { KbHealthOverview, ConnectionSuggestion, Note } from './types'

export function getKbHealthOverview(): Promise<KbHealthOverview> {
  return get<KbHealthOverview>('/api/kb-health/overview')
}

export function getConnectionSuggestions(
  noteId: string,
  limit = 5,
): Promise<ConnectionSuggestion[]> {
  return get<ConnectionSuggestion[]>(
    `/api/kb-health/orphan/${encodeURIComponent(noteId)}/suggestions?limit=${limit}`,
  )
}

export function addLink(orphanId: string, targetNoteId: string): Promise<Note> {
  return post<Note>(`/api/kb-health/orphan/${encodeURIComponent(orphanId)}/link`, {
    targetNoteId,
  })
}
