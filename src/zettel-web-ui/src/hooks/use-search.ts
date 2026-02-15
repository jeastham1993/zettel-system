import { useQuery } from '@tanstack/react-query'
import { search } from '@/api/search'
import { useDebounce } from './use-debounce'
import type { SearchType } from '@/api/types'

export function useSearch(query: string, type: SearchType = 'hybrid') {
  const debouncedQuery = useDebounce(query, 250)

  return useQuery({
    queryKey: ['search', debouncedQuery, type],
    queryFn: () => search(debouncedQuery, type),
    enabled: debouncedQuery.length > 0,
  })
}
