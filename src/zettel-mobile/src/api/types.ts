export type EmbedStatus = 'Pending' | 'Processing' | 'Completed' | 'Failed' | 'Stale'

export type NoteStatus = 'Permanent' | 'Fleeting'

export type NoteType = 'Regular' | 'Structure' | 'Source'

export type SourceType = 'book' | 'article' | 'web' | 'podcast' | 'other'

export interface NoteTag {
  noteId: string
  tag: string
}

export interface Note {
  id: string
  title: string
  content: string
  createdAt: string
  updatedAt: string
  embedStatus: EmbedStatus
  embeddingModel: string | null
  embedError: string | null
  embedRetryCount: number
  embedUpdatedAt: string | null
  tags: NoteTag[]
  status: NoteStatus
  source: string | null
  noteType: NoteType
  sourceAuthor: string | null
  sourceTitle: string | null
  sourceUrl: string | null
  sourceYear: number | null
  sourceType: SourceType | null
}

export interface CreateNoteRequest {
  title: string
  content: string
  tags?: string[]
  status?: NoteStatus
  source?: string
  noteType?: NoteType
  sourceAuthor?: string
  sourceTitle?: string
  sourceUrl?: string
  sourceYear?: number
  sourceType?: SourceType
}

export interface UpdateNoteRequest {
  title: string
  content: string
  tags?: string[]
  noteType?: NoteType
  sourceAuthor?: string
  sourceTitle?: string
  sourceUrl?: string
  sourceYear?: number
  sourceType?: SourceType
}

export interface InboxCountResult {
  count: number
}

export interface SearchResult {
  noteId: string
  title: string
  snippet: string
  rank: number
}

export type SearchType = 'fulltext' | 'semantic' | 'hybrid'

export interface TitleSearchResult {
  noteId: string
  title: string
}

export interface BacklinkResult {
  id: string
  title: string
}

export interface DuplicateCheckResult {
  isDuplicate: boolean
  similarNoteId: string | null
  similarNoteTitle: string | null
  similarity: number
}

export interface HealthReport {
  status: string
  entries: Record<string, HealthEntry>
}

export interface HealthEntry {
  status: string
  description: string | null
  data: Record<string, unknown>
}

export interface PagedResponse<T> {
  items: T[]
  totalCount: number
}
