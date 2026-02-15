import { useQuery } from '@tanstack/react-query'
import { getGraph } from '@/api/graph'

export function useGraph(threshold = 0.8) {
  return useQuery({
    queryKey: ['graph', threshold],
    queryFn: () => getGraph(threshold),
  })
}
