import { get, post, put, del } from './client'
import type { Note, NoteType, CreateNoteRequest, UpdateNoteRequest, ReEmbedResult, TitleSearchResult, SearchResult, InboxCountResult, PagedResponse, BacklinkResult, DuplicateCheckResult } from './types'

export function listNotes(skip = 0, take = 50, tag?: string, noteType?: NoteType): Promise<PagedResponse<Note>> {
  const params = new URLSearchParams({ skip: String(skip), take: String(take) })
  if (tag) params.set('tag', tag)
  if (noteType) params.set('noteType', noteType.toLowerCase())
  return get<PagedResponse<Note>>(`/api/notes?${params}`)
}

export function getNote(id: string): Promise<Note> {
  return get<Note>(`/api/notes/${encodeURIComponent(id)}`)
}

export function createNote(req: CreateNoteRequest): Promise<Note> {
  return post<Note>('/api/notes', req)
}

export function updateNote(id: string, req: UpdateNoteRequest): Promise<Note> {
  return put<Note>(`/api/notes/${encodeURIComponent(id)}`, req)
}

export function deleteNote(id: string): Promise<void> {
  return del(`/api/notes/${encodeURIComponent(id)}`)
}

export function reEmbedAll(): Promise<ReEmbedResult> {
  return post<ReEmbedResult>('/api/notes/re-embed')
}

export function searchTitles(query: string): Promise<TitleSearchResult[]> {
  const params = new URLSearchParams({ q: query })
  return get<TitleSearchResult[]>(`/api/notes/search-titles?${params}`)
}

export function getRelatedNotes(id: string, limit = 5): Promise<SearchResult[]> {
  const params = new URLSearchParams({ limit: String(limit) })
  return get<SearchResult[]>(`/api/notes/${encodeURIComponent(id)}/related?${params}`)
}

export function discoverNotes(limit = 5): Promise<SearchResult[]> {
  const params = new URLSearchParams({ limit: String(limit) })
  return get<SearchResult[]>(`/api/notes/discover?${params}`)
}

export function listInbox(): Promise<Note[]> {
  return get<PagedResponse<Note>>('/api/notes/inbox').then((r) => r.items)
}

export function inboxCount(): Promise<InboxCountResult> {
  return get<InboxCountResult>('/api/notes/inbox/count')
}

export function promoteNote(id: string, noteType?: NoteType): Promise<Note> {
  const params = noteType ? `?noteType=${noteType.toLowerCase()}` : ''
  return post<Note>(`/api/notes/${encodeURIComponent(id)}/promote${params}`)
}

export function getBacklinks(noteId: string): Promise<BacklinkResult[]> {
  return get<BacklinkResult[]>(`/api/notes/${encodeURIComponent(noteId)}/backlinks`)
}

export function mergeNote(fleetingId: string, targetId: string): Promise<Note> {
  return post<Note>(`/api/notes/${encodeURIComponent(fleetingId)}/merge/${encodeURIComponent(targetId)}`)
}

export function checkDuplicate(content: string): Promise<DuplicateCheckResult> {
  return post<DuplicateCheckResult>('/api/notes/check-duplicate', { content })
}

export function getSuggestedTags(noteId: string): Promise<string[]> {
  return get<string[]>(`/api/notes/${encodeURIComponent(noteId)}/suggested-tags`)
}
