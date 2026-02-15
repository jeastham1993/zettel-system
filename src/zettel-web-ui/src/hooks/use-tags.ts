import { useQuery } from '@tanstack/react-query'
import { searchTags } from '@/api/tags'
import { useDebounce } from './use-debounce'

export function useTags(query: string) {
  const debouncedQuery = useDebounce(query, 200)

  return useQuery({
    queryKey: ['tags', debouncedQuery],
    queryFn: () => searchTags(debouncedQuery),
    enabled: debouncedQuery.length > 0,
  })
}
