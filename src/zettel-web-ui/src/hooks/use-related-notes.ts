import { useQuery } from '@tanstack/react-query'
import { getRelatedNotes } from '@/api/notes'

export function useRelatedNotes(noteId: string | undefined) {
  return useQuery({
    queryKey: ['related-notes', noteId],
    queryFn: () => getRelatedNotes(noteId!),
    enabled: !!noteId,
  })
}
