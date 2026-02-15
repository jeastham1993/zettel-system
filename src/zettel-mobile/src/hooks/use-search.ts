import { useQuery } from '@tanstack/react-query'
import { search } from '../api/search'
import { queryKeys } from './query-keys'
import { useDebounce } from './use-debounce'
import type { SearchType } from '../api/types'

export function useSearch(query: string, type: SearchType = 'hybrid') {
  const debouncedQuery = useDebounce(query, 300)

  return useQuery({
    queryKey: queryKeys.search.query(debouncedQuery, type),
    queryFn: () => search(debouncedQuery, type),
    enabled: debouncedQuery.length > 0,
  })
}
