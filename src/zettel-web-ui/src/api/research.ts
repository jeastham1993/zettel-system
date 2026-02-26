import { get, post } from './client'
import type { ResearchAgenda, ResearchFinding, Note } from './types'

export function triggerResearch(sourceNoteId?: string): Promise<ResearchAgenda> {
  return post<ResearchAgenda>('/api/research/trigger', { sourceNoteId: sourceNoteId ?? null })
}

export function approveAgenda(agendaId: string, blockedTaskIds: string[] = []): Promise<void> {
  return post<void>(`/api/research/agenda/${encodeURIComponent(agendaId)}/approve`, { blockedTaskIds })
}

export function getResearchFindings(): Promise<ResearchFinding[]> {
  return get<ResearchFinding[]>('/api/research/findings')
}

export function acceptFinding(findingId: string): Promise<Note> {
  return post<Note>(`/api/research/findings/${encodeURIComponent(findingId)}/accept`)
}

export function dismissFinding(findingId: string): Promise<void> {
  return post<void>(`/api/research/findings/${encodeURIComponent(findingId)}/dismiss`)
}
