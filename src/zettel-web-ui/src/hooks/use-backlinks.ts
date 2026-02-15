import { useQuery } from '@tanstack/react-query'
import { getBacklinks } from '@/api/notes'

export function useBacklinks(noteId: string | undefined) {
  return useQuery({
    queryKey: ['backlinks', noteId],
    queryFn: () => getBacklinks(noteId!),
    enabled: !!noteId,
  })
}
