import { get, post, del, getBlob, put } from './client'
import type { ContentGeneration, ContentPiece, VoiceExample, VoiceConfig, PagedResponse } from './types'

// Schedule types

export interface BlogScheduleSettings {
  enabled: boolean
  dayOfWeek: string
  timeOfDay: string
}

export interface SocialScheduleSettings {
  enabled: boolean
  timeOfDay: string
}

export interface ScheduleSettings {
  blog: BlogScheduleSettings
  social: SocialScheduleSettings
}

// Content generation

export function triggerGeneration(): Promise<ContentGeneration> {
  return post<ContentGeneration>('/api/content/generate')
}

export function listGenerations(skip = 0, take = 20): Promise<PagedResponse<ContentGeneration>> {
  const params = new URLSearchParams({ skip: String(skip), take: String(take) })
  return get<PagedResponse<ContentGeneration>>(`/api/content/generations?${params}`)
}

export function getGeneration(id: string): Promise<ContentGeneration> {
  return get<ContentGeneration>(`/api/content/generations/${encodeURIComponent(id)}`)
}

export function listPieces(params?: { medium?: string; status?: string; skip?: number; take?: number }): Promise<PagedResponse<ContentPiece>> {
  const qs = new URLSearchParams()
  if (params?.medium) qs.set('medium', params.medium)
  if (params?.status) qs.set('status', params.status)
  qs.set('skip', String(params?.skip ?? 0))
  qs.set('take', String(params?.take ?? 20))
  return get<PagedResponse<ContentPiece>>(`/api/content/pieces?${qs}`)
}

export function getPiece(id: string): Promise<ContentPiece> {
  return get<ContentPiece>(`/api/content/pieces/${encodeURIComponent(id)}`)
}

export function approvePiece(id: string): Promise<void> {
  return put<void>(`/api/content/pieces/${encodeURIComponent(id)}/approve`, {})
}

export function rejectPiece(id: string): Promise<void> {
  return put<void>(`/api/content/pieces/${encodeURIComponent(id)}/reject`, {})
}

export function deleteGeneration(id: string): Promise<void> {
  return del(`/api/content/generations/${encodeURIComponent(id)}`)
}

export function regenerateGeneration(id: string): Promise<ContentGeneration> {
  return post<ContentGeneration>(`/api/content/generations/${encodeURIComponent(id)}/regenerate`)
}

export function regenerateMedium(id: string, medium: 'blog' | 'social'): Promise<ContentPiece[]> {
  return post<ContentPiece[]>(`/api/content/generations/${encodeURIComponent(id)}/regenerate/${medium}`)
}

export function exportPiece(id: string): Promise<Blob> {
  return getBlob(`/api/content/pieces/${encodeURIComponent(id)}/export`)
}

export function updatePieceDescription(id: string, description: string): Promise<void> {
  return put<void>(`/api/content/pieces/${encodeURIComponent(id)}/description`, { description })
}

export function updatePieceTags(id: string, tags: string[]): Promise<void> {
  return put<void>(`/api/content/pieces/${encodeURIComponent(id)}/tags`, { tags })
}

export function sendToDraft(id: string): Promise<ContentPiece> {
  return post<ContentPiece>(`/api/content/pieces/${encodeURIComponent(id)}/send-to-draft`)
}

// Voice

export function listVoiceExamples(): Promise<VoiceExample[]> {
  return get<VoiceExample[]>('/api/content/voice/examples')
}

export function addVoiceExample(data: { medium: string; title?: string; content: string; source?: string }): Promise<VoiceExample> {
  return post<VoiceExample>('/api/content/voice/examples', data)
}

export function deleteVoiceExample(id: string): Promise<void> {
  return del(`/api/content/voice/examples/${encodeURIComponent(id)}`)
}

export function getVoiceConfig(medium?: string): Promise<VoiceConfig[]> {
  const params = new URLSearchParams()
  if (medium) params.set('medium', medium)
  const qs = params.toString()
  return get<VoiceConfig[]>(`/api/content/voice/config${qs ? `?${qs}` : ''}`)
}

export function updateVoiceConfig(data: { medium: string; styleNotes?: string }): Promise<VoiceConfig> {
  return put<VoiceConfig>('/api/content/voice/config', data)
}

// Schedule

export function getSchedule(): Promise<ScheduleSettings> {
  return get<ScheduleSettings>('/api/content/schedule')
}

export function updateBlogSchedule(data: BlogScheduleSettings): Promise<BlogScheduleSettings> {
  return put<BlogScheduleSettings>('/api/content/schedule/blog', data)
}

export function updateSocialSchedule(data: SocialScheduleSettings): Promise<SocialScheduleSettings> {
  return put<SocialScheduleSettings>('/api/content/schedule/social', data)
}
