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

export interface ImportFile {
  fileName: string
  content: string
}

export interface ImportResult {
  total: number
  imported: number
  skipped: number
  noteIds: string[]
}

export interface ReEmbedResult {
  queued: number
}

export interface GraphNode {
  id: string
  title: string
  edgeCount: number
}

export interface GraphEdge {
  source: string
  target: string
  type: 'wikilink' | 'semantic'
  weight: number
}

export interface GraphData {
  nodes: GraphNode[]
  edges: GraphEdge[]
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

export interface NoteVersion {
  id: number
  noteId: string
  title: string
  content: string
  savedAt: string
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

export type ContentGenerationStatus = 'Pending' | 'Generated' | 'Approved' | 'Rejected'

export type ContentPieceStatus = 'Draft' | 'Approved' | 'Rejected'

export type ContentMedium = 'blog' | 'social'

export interface ContentGeneration {
  id: string
  seedNoteId: string
  clusterNoteIds: string[]
  topicSummary: string
  status: ContentGenerationStatus
  generatedAt: string
  reviewedAt: string | null
  pieces?: ContentPiece[]
}

export interface ContentPiece {
  id: string
  generationId: string
  medium: ContentMedium
  title: string | null
  body: string
  status: ContentPieceStatus
  sequence: number
  createdAt: string
  approvedAt: string | null
  description: string | null
  generatedTags: string[]
  editorFeedback: string | null
  sentToDraftAt: string | null
  draftReference: string | null
}

export interface VoiceExample {
  id: string
  medium: string
  title: string | null
  content: string
  source: string | null
  createdAt: string
}

export interface VoiceConfig {
  id: string
  medium: string
  styleNotes: string | null
  updatedAt: string
}

export interface KbHealthScorecard {
  totalNotes: number
  embeddedPercent: number
  orphanCount: number
  averageConnections: number
}

export interface UnconnectedNote {
  id: string
  title: string
  createdAt: string
  suggestionCount: number
}

export interface ClusterSummary {
  hubNoteId: string
  hubTitle: string
  noteCount: number
}

export interface UnusedSeedNote {
  id: string
  title: string
  connectionCount: number
}

export interface KbHealthOverview {
  scorecard: KbHealthScorecard
  newAndUnconnected: UnconnectedNote[]
  richestClusters: ClusterSummary[]
  neverUsedAsSeeds: UnusedSeedNote[]
}

export interface ConnectionSuggestion {
  noteId: string
  title: string
  similarity: number
}

export interface UnembeddedNote {
  id: string
  title: string
  createdAt: string
  embedStatus: EmbedStatus
  embedError: string | null
}

export interface LargeNote {
  id: string
  title: string
  updatedAt: string
  characterCount: number
}

export interface SummarizeNoteResponse {
  noteId: string
  originalLength: number
  summarizedLength: number
  stillLarge: boolean
}

export interface SuggestedNote {
  title: string
  content: string
}

export interface SplitSuggestion {
  noteId: string
  originalTitle: string
  notes: SuggestedNote[]
}

export interface ApplySplitRequest {
  notes: SuggestedNote[]
}

export interface ApplySplitResponse {
  originalNoteId: string
  createdNoteIds: string[]
}

// Research Agent types
export interface ResearchTask {
  id: string
  query: string
  sourceType: 'WebSearch' | 'Arxiv'
  motivation: string
  status: 'Pending' | 'Blocked' | 'Completed' | 'Failed'
}

export interface ResearchAgenda {
  id: string
  triggeredFromNoteId?: string
  status: 'Pending' | 'Approved' | 'Executing' | 'Completed' | 'Cancelled'
  tasks: ResearchTask[]
  createdAt: string
  approvedAt?: string
}

export interface ResearchFinding {
  id: string
  taskId: string
  title: string
  synthesis: string
  sourceUrl: string
  sourceType: 'WebSearch' | 'Arxiv'
  status: 'Pending' | 'Accepted' | 'Dismissed'
  createdAt: string
  reviewedAt?: string
}
