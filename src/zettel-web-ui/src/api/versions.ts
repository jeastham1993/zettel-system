import { get } from './client'
import type { NoteVersion } from './types'

export function listVersions(noteId: string): Promise<NoteVersion[]> {
  return get<NoteVersion[]>(`/api/notes/${encodeURIComponent(noteId)}/versions`)
}

export function getVersion(noteId: string, versionId: number): Promise<NoteVersion> {
  return get<NoteVersion>(`/api/notes/${encodeURIComponent(noteId)}/versions/${versionId}`)
}
