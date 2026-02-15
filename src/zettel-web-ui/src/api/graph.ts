import { get } from './client'
import type { GraphData } from './types'

export function getGraph(threshold = 0.8): Promise<GraphData> {
  const params = new URLSearchParams({ threshold: String(threshold) })
  return get<GraphData>(`/api/graph?${params}`)
}
