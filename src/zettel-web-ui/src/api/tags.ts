import { get } from './client'

export function searchTags(query: string): Promise<string[]> {
  const params = new URLSearchParams({ q: query })
  return get<string[]>(`/api/tags?${params}`)
}
