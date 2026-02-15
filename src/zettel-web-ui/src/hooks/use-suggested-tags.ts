import { useQuery } from '@tanstack/react-query'
import { getSuggestedTags } from '@/api/notes'

export function useSuggestedTags(noteId: string | undefined) {
  return useQuery({
    queryKey: ['suggested-tags', noteId],
    queryFn: () => getSuggestedTags(noteId!),
    enabled: !!noteId,
    staleTime: 5 * 60 * 1000,
    retry: false,
  })
}
