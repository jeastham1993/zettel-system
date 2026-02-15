import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import * as notesApi from '@/api/notes'
import type { CreateNoteRequest } from '@/api/types'

const FLEETING_AUTO_TITLE = 'auto'

export function useInbox() {
  return useQuery({
    queryKey: ['inbox'],
    queryFn: notesApi.listInbox,
  })
}

export function useInboxCount() {
  return useQuery({
    queryKey: ['inbox', 'count'],
    queryFn: notesApi.inboxCount,
    refetchInterval: 30_000,
    refetchIntervalInBackground: false,
  })
}

export function usePromoteNote() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => notesApi.promoteNote(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['inbox'] })
      queryClient.invalidateQueries({ queryKey: ['notes'] })
    },
  })
}

export function useCaptureNote() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (req: { content: string; tags?: string[] }) => {
      const createReq: CreateNoteRequest = {
        title: FLEETING_AUTO_TITLE,
        content: req.content,
        status: 'Fleeting',
        source: 'web',
        tags: req.tags,
      }
      return notesApi.createNote(createReq)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['inbox'] })
      queryClient.invalidateQueries({ queryKey: ['notes'] })
    },
  })
}

export function useMergeNote() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (args: { fleetingId: string; targetId: string }) =>
      notesApi.mergeNote(args.fleetingId, args.targetId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['inbox'] })
      queryClient.invalidateQueries({ queryKey: ['notes'] })
    },
  })
}
