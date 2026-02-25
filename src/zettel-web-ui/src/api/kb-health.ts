import { get, post } from './client'
import type {
  KbHealthOverview,
  ConnectionSuggestion,
  Note,
  UnembeddedNote,
  LargeNote,
  SummarizeNoteResponse,
  SplitSuggestion,
  ApplySplitResponse,
  SuggestedNote,
} from './types'

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

export function getNotesWithoutEmbeddings(): Promise<UnembeddedNote[]> {
  return get<UnembeddedNote[]>('/api/kb-health/missing-embeddings')
}

export function requeueNoteEmbedding(noteId: string): Promise<void> {
  return post<void>(`/api/kb-health/missing-embeddings/${encodeURIComponent(noteId)}/requeue`)
}

export function getLargeNotes(): Promise<LargeNote[]> {
  return get<LargeNote[]>('/api/kb-health/large-notes')
}

export function summarizeNote(noteId: string): Promise<SummarizeNoteResponse> {
  return post<SummarizeNoteResponse>(`/api/kb-health/large-notes/${encodeURIComponent(noteId)}/summarize`)
}

export function getSplitSuggestions(noteId: string): Promise<SplitSuggestion> {
  return post<SplitSuggestion>(`/api/kb-health/large-notes/${encodeURIComponent(noteId)}/split-suggestions`)
}

export function applySplit(noteId: string, notes: SuggestedNote[]): Promise<ApplySplitResponse> {
  return post<ApplySplitResponse>(`/api/kb-health/large-notes/${encodeURIComponent(noteId)}/apply-split`, { notes })
}
