import { get } from './client'
import type { SearchResult, SearchType } from './types'

export function search(query: string, type: SearchType = 'hybrid'): Promise<SearchResult[]> {
  const params = new URLSearchParams({ q: query, type })
  return get<SearchResult[]>(`/api/search?${params}`)
}
