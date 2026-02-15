import { useQuery } from '@tanstack/react-query'
import { discoverNotes } from '@/api/notes'
import { discoverNotesByMode, type DiscoverMode } from '@/api/discover'

export function useDiscover(limit = 5) {
  return useQuery({
    queryKey: ['discover', limit],
    queryFn: () => discoverNotes(limit),
    staleTime: 5 * 60 * 1000,
  })
}

export function useDiscoverByMode(mode: DiscoverMode, limit = 5) {
  return useQuery({
    queryKey: ['discover', mode, limit],
    queryFn: () => discoverNotesByMode(mode, limit),
    staleTime: 5 * 60 * 1000,
  })
}
