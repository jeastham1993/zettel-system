import { useQuery } from '@tanstack/react-query'
import { checkDuplicate } from '@/api/notes'

export function useDuplicateCheck(content: string, enabled = true) {
  return useQuery({
    queryKey: ['duplicate-check', content],
    queryFn: () => checkDuplicate(content),
    enabled: enabled && content.length > 50,
    staleTime: 60 * 1000,
    retry: false,
  })
}
